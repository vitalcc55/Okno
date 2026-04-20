using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using WinRT;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Windows.Capture;

public sealed class GraphicsCaptureService(
    AuditLogOptions auditLogOptions,
    IMonitorManager monitorManager,
    IWindowManager windowManager) : ICaptureService, IWaitVisualProbe
{
    private const int GraphicsCaptureBufferCount = 1;
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const uint D3D11SdkVersion = 7;
    private const int DwmwaExtendedFrameBounds = 9;
    private static readonly TimeSpan WindowsGraphicsCaptureTimeout = TimeSpan.FromSeconds(3);
    private static readonly DirectXPixelFormat GraphicsCapturePixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private static readonly Guid GraphicsCaptureItemInteropId = typeof(IGraphicsCaptureItemInterop).GUID;
    private static readonly Guid GraphicsCaptureItemInterfaceId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiDeviceId = typeof(IDxgiDevice).GUID;
    private readonly Lazy<IDirect3DDevice> _device = new(CreateDirect3DDevice, LazyThreadSafetyMode.ExecutionAndPublication);

    public async Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken)
    {
        try
        {
            CaptureResolvedTarget resolvedTarget = ResolveTarget(target);
            CaptureBackend backend = CaptureBackendSelector.Select(target.Scope, GraphicsCaptureSession.IsSupported());
            if (backend == CaptureBackend.Unsupported)
            {
                throw new CaptureOperationException("Windows Graphics Capture недоступен в текущей сессии.");
            }

            CaptureExecutionResult execution = await CaptureRasterizedAsync(
                target,
                resolvedTarget,
                backend,
                cancellationToken).ConfigureAwait(false);
            string artifactPath = WriteArtifact(execution.Capture.PngBytes, execution.Target);

            DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
            CaptureMetadata metadata = new(
                Scope: target.Scope.ToContractValue(),
                TargetKind: execution.Target.TargetKind,
                Hwnd: execution.Target.Window?.Hwnd,
                Title: execution.Target.Window?.Title,
                ProcessName: execution.Target.Window?.ProcessName,
                Bounds: execution.Target.Bounds,
                CoordinateSpace: execution.Target.CoordinateSpace,
                PixelWidth: execution.Capture.PixelWidth,
                PixelHeight: execution.Capture.PixelHeight,
                CapturedAtUtc: capturedAtUtc,
                ArtifactPath: artifactPath,
                MimeType: "image/png",
                ByteSize: execution.Capture.PngBytes.Length,
                SessionRunId: auditLogOptions.RunId,
                EffectiveDpi: execution.Target.EffectiveDpi,
                DpiScale: execution.Target.DpiScale,
                MonitorId: execution.Target.Monitor?.Descriptor.MonitorId,
                MonitorFriendlyName: execution.Target.Monitor?.Descriptor.FriendlyName,
                MonitorGdiDeviceName: execution.Target.Monitor?.Descriptor.GdiDeviceName,
                FrameBounds: execution.Target.FrameBounds,
                CaptureReference: TryCreateCaptureReference(target.Scope, execution.Target, execution.Capture, capturedAtUtc));

            return new CaptureResult(metadata, execution.Capture.PngBytes);
        }
        catch (CaptureOperationException)
        {
            throw;
        }
        catch (COMException exception)
        {
            throw new CaptureOperationException("Windows отказала в получении снимка выбранной цели.", exception);
        }
    }

    public async Task<WaitVisualSample> CaptureVisualSampleAsync(
        WindowDescriptor targetWindow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);

        CaptureTarget target = new(CaptureScope.Window, targetWindow);
        try
        {
            CaptureResolvedTarget resolvedTarget = ResolveTarget(target);
            CaptureBackend backend = CaptureBackendSelector.Select(target.Scope, GraphicsCaptureSession.IsSupported());
            if (backend == CaptureBackend.Unsupported)
            {
                throw new CaptureOperationException("Windows Graphics Capture недоступен в текущей сессии.");
            }

            WaitVisualSample execution = await CaptureVisualSampleAsync(
                target,
                resolvedTarget,
                backend,
                cancellationToken).ConfigureAwait(false);
            return execution;
        }
        catch (CaptureOperationException)
        {
            throw;
        }
        catch (COMException exception)
        {
            throw new CaptureOperationException("Windows отказала в получении визуального wait baseline для выбранного окна.", exception);
        }
    }

    private async Task<WaitVisualSample> CaptureVisualSampleAsync(
        CaptureTarget request,
        CaptureResolvedTarget resolvedTarget,
        CaptureBackend backend,
        CancellationToken cancellationToken)
    {
        if (backend == CaptureBackend.DesktopGdiFallback)
        {
            throw new CaptureOperationException("Visual wait probe не поддерживает desktop-scoped fallback path.");
        }

        try
        {
            WgcCaptureOutcome outcome = await CaptureSoftwareBitmapAsync(
                resolvedTarget,
                cancellationToken,
                cancellationToken).ConfigureAwait(false);
            using (outcome)
            {
                WindowDescriptor authoritativeWindow = BuildAuthoritativeWgcProbeWindow(
                    request,
                    resolvedTarget);
                WaitVisualComparisonData comparisonData = WaitVisualComparisonDataBuilder.CreateFromSoftwareBitmap(
                    outcome.Bitmap,
                    cancellationToken);
                SoftwareBitmap evidenceBitmap = outcome.DetachBitmap();
                try
                {
                    WaitVisualEvidenceFrame evidenceFrame = new SoftwareBitmapWaitVisualEvidenceFrame(
                        authoritativeWindow,
                        evidenceBitmap);
                    return new WaitVisualSample(
                        authoritativeWindow,
                        evidenceFrame.PixelWidth,
                        evidenceFrame.PixelHeight,
                        comparisonData,
                        evidenceFrame);
                }
                catch
                {
                    evidenceBitmap.Dispose();
                    throw;
                }
            }
        }
        catch (WgcAcquisitionException exception)
        {
            throw new CaptureOperationException(exception.Message, exception);
        }
    }

    public async Task WriteVisualEvidenceAsync(
        WaitVisualEvidenceFrame frame,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            if (frame is not SoftwareBitmapWaitVisualEvidenceFrame softwareBitmapFrame)
            {
                throw new CaptureOperationException("Runtime получил неподдерживаемый visual evidence frame.");
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] pngBytes = await EncodePngAsync(softwareBitmapFrame.GetBitmap(), cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, pngBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CaptureOperationException("Runtime не смог записать visual wait artifact на диск.", exception);
        }
        catch (IOException exception)
        {
            throw new CaptureOperationException("Runtime не смог записать visual wait artifact на диск.", exception);
        }
        catch (ExternalException exception)
        {
            throw new CaptureOperationException("Runtime не смог закодировать visual wait artifact в PNG.", exception);
        }
    }

    private async Task<CaptureExecutionResult> CaptureRasterizedAsync(
        CaptureTarget request,
        CaptureResolvedTarget resolvedTarget,
        CaptureBackend backend,
        CancellationToken cancellationToken)
    {
        if (backend == CaptureBackend.DesktopGdiFallback)
        {
            CaptureResolvedTarget fallbackTarget = ResolveTarget(request);
            return new(fallbackTarget, CaptureDesktopWithGdi(fallbackTarget));
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(WindowsGraphicsCaptureTimeout);

        try
        {
            WgcCaptureOutcome outcome = await CaptureSoftwareBitmapAsync(
                resolvedTarget,
                cancellationToken,
                timeoutSource.Token).ConfigureAwait(false);
            using (outcome)
            {
                CaptureResolvedTarget authoritativeTarget = BuildAuthoritativeWgcTarget(
                    request,
                    resolvedTarget,
                    outcome.AcceptedContentSize);
                byte[] pngBytes = await EncodePngAsync(outcome.Bitmap, cancellationToken).ConfigureAwait(false);
                return new(
                    authoritativeTarget,
                    new RasterizedCapture(
                        pngBytes,
                        outcome.Bitmap.PixelWidth,
                        outcome.Bitmap.PixelHeight));
            }
        }
        catch (WgcAcquisitionException exception)
        {
            return FallbackOrThrow(request, exception);
        }
    }

    private CaptureExecutionResult FallbackOrThrow(
        CaptureTarget request,
        WgcAcquisitionException exception)
    {
        if (WgcAcquisitionFailurePolicy.Evaluate(request.Scope) == WgcAcquisitionFailureAction.FallbackToDesktopGdi)
        {
            CaptureResolvedTarget fallbackTarget = ResolveTarget(request);
            return new(fallbackTarget, CaptureDesktopWithGdi(fallbackTarget));
        }

        throw new CaptureOperationException(exception.Message, exception);
    }

    private async Task<WgcCaptureOutcome> CaptureSoftwareBitmapAsync(
        CaptureResolvedTarget resolvedTarget,
        CancellationToken userCancellationToken,
        CancellationToken acquisitionCancellationToken)
    {
        try
        {
            GraphicsCaptureItem item = resolvedTarget.Scope == CaptureScope.Window
                ? CreateItemForWindow(new IntPtr(resolvedTarget.Window!.Hwnd))
                : CreateItemForMonitor(new IntPtr(resolvedTarget.Monitor!.CaptureHandle));
            SizeInt32 size = item.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                throw new WgcAcquisitionException("У выбранной цели capture недопустимый размер.");
            }

            using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device.Value,
                GraphicsCapturePixelFormat,
                GraphicsCaptureBufferCount,
                size);
            using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
            session.StartCapture();
            return await CaptureStableSoftwareBitmapAsync(
                framePool,
                size,
                acquisitionCancellationToken).ConfigureAwait(false);
        }
        catch (WgcAcquisitionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!userCancellationToken.IsCancellationRequested)
        {
            throw new WgcAcquisitionException("Windows Graphics Capture не вернул frame вовремя для выбранной цели.");
        }
        catch (COMException exception)
        {
            throw new WgcAcquisitionException("Windows Graphics Capture не смог получить frame для выбранной цели.", exception);
        }
        catch (InvalidCastException exception)
        {
            throw new WgcAcquisitionException("Windows Graphics Capture не поддержал выбранную цель в текущей сессии.", exception);
        }
    }

    private async Task<WgcCaptureOutcome> CaptureStableSoftwareBitmapAsync(
        Direct3D11CaptureFramePool framePool,
        SizeInt32 initialSize,
        CancellationToken cancellationToken)
    {
        WgcFrameSize expectedSize = WgcFrameSize.FromSizeInt32(initialSize);
        bool recreateAttempted = false;

        while (true)
        {
            using Direct3D11CaptureFrame frame = await WaitForNextFrameAsync(framePool, cancellationToken).ConfigureAwait(false);
            WgcFrameSize contentSize = WgcFrameSize.FromSizeInt32(frame.ContentSize);

            switch (WgcFrameSizingPolicy.Evaluate(expectedSize, contentSize, recreateAttempted))
            {
                case WgcFrameSizingDecision.Accept:
                {
                    SoftwareBitmap softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);

                    if (softwareBitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                        && softwareBitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied)
                    {
                        return new WgcCaptureOutcome(softwareBitmap, contentSize);
                    }

                    SoftwareBitmap converted = SoftwareBitmap.Convert(
                        softwareBitmap,
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied);
                    softwareBitmap.Dispose();
                    return new WgcCaptureOutcome(converted, contentSize);
                }

                case WgcFrameSizingDecision.RecreateAndRetry:
                    RecreateFramePool(framePool, contentSize);
                    expectedSize = contentSize;
                    recreateAttempted = true;
                    continue;

                case WgcFrameSizingDecision.Fail:
                    throw CreateGeometryMismatchException(expectedSize, contentSize, recreateAttempted);

                default:
                    throw new InvalidOperationException("WGC frame sizing policy returned an unsupported decision.");
            }
        }
    }

    private void RecreateFramePool(
        Direct3D11CaptureFramePool framePool,
        WgcFrameSize contentSize)
    {
        try
        {
            framePool.Recreate(
                _device.Value,
                GraphicsCapturePixelFormat,
                GraphicsCaptureBufferCount,
                contentSize.ToSizeInt32());
        }
        catch (COMException exception)
        {
            throw new WgcAcquisitionException(
                $"Windows Graphics Capture не смог выполнить Recreate для ContentSize {contentSize}.",
                exception);
        }
    }

    private static WgcAcquisitionException CreateGeometryMismatchException(
        WgcFrameSize expectedSize,
        WgcFrameSize contentSize,
        bool recreateAttempted)
    {
        if (!contentSize.IsValid)
        {
            return new WgcAcquisitionException(
                $"Windows Graphics Capture вернул недопустимый ContentSize {contentSize}. Runtime не будет сохранять frame с invalid geometry.");
        }

        if (!recreateAttempted)
        {
            return new WgcAcquisitionException(
                $"Windows Graphics Capture вернул ContentSize {contentSize}, который не совпадает с ожидаемым размером frame pool {expectedSize}. Runtime требует Recreate перед сохранением кадра.");
        }

        return new WgcAcquisitionException(
            $"Windows Graphics Capture не стабилизировал ContentSize после Recreate. Ожидался {expectedSize}, получен {contentSize}.");
    }

    private static async Task<Direct3D11CaptureFrame> WaitForNextFrameAsync(
        Direct3D11CaptureFramePool framePool,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<Direct3D11CaptureFrame> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TypedEventHandler<Direct3D11CaptureFramePool, object>? handler = null;
        CancellationTokenRegistration registration = default;

        handler = (sender, _) =>
        {
            try
            {
                Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
                if (frame is null)
                {
                    return;
                }

                if (!completion.TrySetResult(frame))
                {
                    frame.Dispose();
                }
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        };

        framePool.FrameArrived += handler;
        registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        try
        {
            Direct3D11CaptureFrame? immediateFrame = framePool.TryGetNextFrame();
            if (immediateFrame is not null && !completion.TrySetResult(immediateFrame))
            {
                immediateFrame.Dispose();
            }

            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
            framePool.FrameArrived -= handler;
        }
    }

    private static RasterizedCapture CaptureDesktopWithGdi(CaptureResolvedTarget resolvedTarget)
    {
        if (resolvedTarget.Bounds.Width <= 0 || resolvedTarget.Bounds.Height <= 0)
        {
            throw new CaptureOperationException("У выбранной цели capture недопустимый размер.");
        }

        try
        {
            using Bitmap bitmap = new(resolvedTarget.Bounds.Width, resolvedTarget.Bounds.Height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                resolvedTarget.Bounds.Left,
                resolvedTarget.Bounds.Top,
                0,
                0,
                new System.Drawing.Size(resolvedTarget.Bounds.Width, resolvedTarget.Bounds.Height),
                CopyPixelOperation.SourceCopy);

            using MemoryStream stream = new();
            bitmap.Save(stream, ImageFormat.Png);
            return new RasterizedCapture(stream.ToArray(), bitmap.Width, bitmap.Height);
        }
        catch (Win32Exception exception)
        {
            throw new CaptureOperationException("Native desktop capture fallback не смог получить пиксели с экрана.", exception);
        }
        catch (ExternalException exception)
        {
            throw new CaptureOperationException("Native desktop capture fallback не смог закодировать PNG.", exception);
        }
    }

    private static async Task<byte[]> EncodePngAsync(SoftwareBitmap softwareBitmap, CancellationToken cancellationToken)
    {
        using InMemoryRandomAccessStream stream = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(cancellationToken);
        encoder.SetSoftwareBitmap(softwareBitmap);
        await encoder.FlushAsync().AsTask(cancellationToken);

        ulong size = stream.Size;
        if (size > int.MaxValue)
        {
            throw new CaptureOperationException("PNG capture оказался слишком большим для текущего V1 wire-format.");
        }

        using DataReader reader = new(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)size).AsTask(cancellationToken);
        byte[] bytes = new byte[(int)size];
        reader.ReadBytes(bytes);
        return bytes;
    }


    private CaptureResolvedTarget BuildAuthoritativeWgcTarget(
        CaptureTarget request,
        CaptureResolvedTarget initialTarget,
        WgcFrameSize acceptedContentSize)
    {
        CaptureResolvedTarget? refreshedTarget = TryResolveTarget(request);
        return CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            acceptedContentSize);
    }

    private WindowDescriptor BuildAuthoritativeWgcProbeWindow(
        CaptureTarget request,
        CaptureResolvedTarget initialTarget)
    {
        CaptureResolvedTarget? refreshedTarget = TryResolveTarget(request);
        return CaptureResolvedTargetPolicy.BuildAuthoritativeWgcProbeWindow(
            initialTarget,
            refreshedTarget);
    }

    private static InputCaptureReference? TryCreateCaptureReference(
        CaptureScope scope,
        CaptureResolvedTarget target,
        RasterizedCapture capture,
        DateTimeOffset capturedAtUtc)
    {
        if (scope != CaptureScope.Window
            || target.CaptureReferenceEligibility != CaptureReferenceEligibility.Eligible)
        {
            return null;
        }

        try
        {
            return CaptureReferencePublisher.TryCreate(
                scope,
                target,
                capture.PixelWidth,
                capture.PixelHeight,
                capturedAtUtc);
        }
        catch (InvalidOperationException exception)
        {
            throw new CaptureOperationException("Runtime не смог материализовать input-compatible captureReference для window capture.", exception);
        }
    }

    private CaptureResolvedTarget? TryResolveTarget(CaptureTarget request)
    {
        try
        {
            return ResolveTarget(request);
        }
        catch (CaptureOperationException)
        {
            return null;
        }
    }

    private string WriteArtifact(byte[] pngBytes, CaptureResolvedTarget target)
    {
        try
        {
            string capturesDirectory = Path.Combine(auditLogOptions.RunDirectory, "captures");
            Directory.CreateDirectory(capturesDirectory);

            string handle = target.Window?.Hwnd.ToString(CultureInfo.InvariantCulture)
                ?? target.Monitor?.CaptureHandle.ToString(CultureInfo.InvariantCulture)
                ?? "primary";
            string fileName = CaptureArtifactNameBuilder.Create(
                target.Scope.ToContractValue(),
                target.TargetKind,
                handle,
                DateTime.UtcNow);
            string path = Path.Combine(capturesDirectory, fileName);
            File.WriteAllBytes(path, pngBytes);
            return path;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CaptureOperationException("Runtime не смог записать capture artifact на диск.", exception);
        }
        catch (IOException exception)
        {
            throw new CaptureOperationException("Runtime не смог записать capture artifact на диск.", exception);
        }
    }

    private CaptureResolvedTarget ResolveTarget(CaptureTarget target) =>
        target.Scope switch
        {
            CaptureScope.Window => ResolveWindowTarget(target.Window),
            CaptureScope.Desktop => ResolveDesktopTarget(target.Window, target.MonitorId),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.Scope, null),
        };

    private CaptureResolvedTarget ResolveWindowTarget(WindowDescriptor? window)
    {
        if (window is null)
        {
            throw new CaptureOperationException("Для window capture нужно передать hwnd или сначала прикрепить окно.");
        }

        IntPtr hwnd = new(window.Hwnd);
        Bounds frameBounds = TryGetWindowBounds(hwnd, out Bounds currentBounds) ? currentBounds : window.Bounds;
        Bounds rasterBounds = TryGetRasterOriginBounds(hwnd, frameBounds, out Bounds visibleBounds) ? visibleBounds : frameBounds;
        ValidateWindowCaptureTarget(hwnd, frameBounds);
        if (!WindowDpiReader.TryGetEffectiveDpi(hwnd, out int effectiveDpi))
        {
            throw new CaptureOperationException("Не удалось определить DPI выбранного окна для window capture.");
        }

        double dpiScale = effectiveDpi / 96.0;
        DisplayTopologySnapshot topology = monitorManager.GetTopologySnapshot();
        MonitorInfo? monitor = monitorManager.FindMonitorForWindow(window.Hwnd, topology);
        WindowDescriptor? liveWindow = windowManager
            .ListWindows(includeInvisible: true)
            .FirstOrDefault(candidate => candidate.Hwnd == window.Hwnd);
        WindowDescriptor refreshedWindow = CaptureWindowSnapshotPolicy.BuildRefreshedWindowSnapshot(
            window,
            liveWindow,
            frameBounds,
            effectiveDpi,
            dpiScale,
            monitor);

        return new CaptureResolvedTarget(
            CaptureScope.Window,
            "window",
            refreshedWindow,
            rasterBounds,
            CaptureCoordinateSpaceValues.PhysicalPixels,
            effectiveDpi,
            dpiScale,
            monitor,
            frameBounds);
    }

    private CaptureResolvedTarget ResolveDesktopTarget(WindowDescriptor? window, string? explicitMonitorId)
    {
        DisplayTopologySnapshot topology = monitorManager.GetTopologySnapshot();
        MonitorInfo? monitor = DesktopCaptureMonitorResolver.Resolve(window, explicitMonitorId, monitorManager, topology);
        if (monitor is null)
        {
            throw new CaptureOperationException(
                !string.IsNullOrWhiteSpace(explicitMonitorId)
                    ? "Выбранный monitorId не найден среди активных мониторов."
                    : "Не удалось определить monitor для desktop capture.");
        }

        return new CaptureResolvedTarget(
            CaptureScope.Desktop,
            "monitor",
            window,
            monitor.Descriptor.Bounds,
            CaptureCoordinateSpaceValues.PhysicalPixels,
            null,
            null,
            monitor);
    }

    private static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        IntPtr factoryPtr = GetActivationFactory(GraphicsCaptureItemInteropId);
        object factory = Marshal.GetTypedObjectForIUnknown(factoryPtr, typeof(IGraphicsCaptureItemInterop));

        try
        {
            IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop)factory;
            Guid itemId = GraphicsCaptureItemInterfaceId;
            Marshal.ThrowExceptionForHR(interop.CreateForWindow(hwnd, ref itemId, out IntPtr itemPtr));

            try
            {
                return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                MarshalInspectable<GraphicsCaptureItem>.DisposeAbi(itemPtr);
            }
        }
        finally
        {
            Marshal.Release(factoryPtr);
            if (Marshal.IsComObject(factory))
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    private static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor)
    {
        IntPtr factoryPtr = GetActivationFactory(GraphicsCaptureItemInteropId);
        object factory = Marshal.GetTypedObjectForIUnknown(factoryPtr, typeof(IGraphicsCaptureItemInterop));

        try
        {
            IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop)factory;
            Guid itemId = GraphicsCaptureItemInterfaceId;
            Marshal.ThrowExceptionForHR(interop.CreateForMonitor(monitor, ref itemId, out IntPtr itemPtr));

            try
            {
                return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                MarshalInspectable<GraphicsCaptureItem>.DisposeAbi(itemPtr);
            }
        }
        finally
        {
            Marshal.Release(factoryPtr);
            if (Marshal.IsComObject(factory))
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    private static IntPtr GetActivationFactory(Guid iid)
    {
        IntPtr hString = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;

        try
        {
            Marshal.ThrowExceptionForHR(
                WindowsCreateString(
                    "Windows.Graphics.Capture.GraphicsCaptureItem",
                    "Windows.Graphics.Capture.GraphicsCaptureItem".Length,
                    out hString));
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hString, iid, out factory));
            return factory;
        }
        finally
        {
            if (hString != IntPtr.Zero)
            {
                _ = WindowsDeleteString(hString);
            }
        }
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        IntPtr device = IntPtr.Zero;
        IntPtr deviceContext = IntPtr.Zero;

        try
        {
            int hr = D3D11CreateDevice(
                pAdapter: IntPtr.Zero,
                driverType: D3DDriverType.Hardware,
                software: IntPtr.Zero,
                flags: D3D11CreateDeviceBgraSupport,
                featureLevels: IntPtr.Zero,
                featureLevelsCount: 0,
                sdkVersion: D3D11SdkVersion,
                device: out device,
                featureLevel: out _,
                immediateContext: out deviceContext);

            if (hr < 0)
            {
                hr = D3D11CreateDevice(
                    pAdapter: IntPtr.Zero,
                    driverType: D3DDriverType.Warp,
                    software: IntPtr.Zero,
                    flags: D3D11CreateDeviceBgraSupport,
                    featureLevels: IntPtr.Zero,
                    featureLevelsCount: 0,
                    sdkVersion: D3D11SdkVersion,
                    device: out device,
                    featureLevel: out _,
                    immediateContext: out deviceContext);
            }

            Marshal.ThrowExceptionForHR(hr);

            Guid dxgiDeviceId = DxgiDeviceId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, ref dxgiDeviceId, out IntPtr dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDxgiDevice(dxgiDevice, out IntPtr inspectableDevice));
                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);
                }
                finally
                {
                    MarshalInterface<IDirect3DDevice>.DisposeAbi(inspectableDevice);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            if (deviceContext != IntPtr.Zero)
            {
                Marshal.Release(deviceContext);
            }

            if (device != IntPtr.Zero)
            {
                Marshal.Release(device);
            }
        }
    }

    private static bool TryGetWindowBounds(IntPtr hwnd, out Bounds bounds)
    {
        if (GetWindowRect(hwnd, out RECT rect))
        {
            bounds = new Bounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return true;
        }

        bounds = new Bounds(0, 0, 0, 0);
        return false;
    }

    private static bool TryGetRasterOriginBounds(IntPtr hwnd, Bounds frameBounds, out Bounds bounds)
    {
        if (TryGetDwmExtendedFrameBounds(hwnd, out Bounds visibleBounds)
            && ContainsBounds(frameBounds, visibleBounds))
        {
            bounds = visibleBounds;
            return true;
        }

        bounds = default!;
        return false;
    }

    private static bool TryGetDwmExtendedFrameBounds(IntPtr hwnd, out Bounds bounds)
    {
        try
        {
            int hresult = DwmGetWindowAttribute(
                hwnd,
                DwmwaExtendedFrameBounds,
                out RECT rect,
                Marshal.SizeOf<RECT>());
            if (hresult == 0)
            {
                Bounds candidate = new(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (candidate.Width > 0 && candidate.Height > 0)
                {
                    bounds = candidate;
                    return true;
                }
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        bounds = default!;
        return false;
    }

    private static bool ContainsBounds(Bounds outer, Bounds inner) =>
        inner.Left >= outer.Left
        && inner.Top >= outer.Top
        && inner.Right <= outer.Right
        && inner.Bottom <= outer.Bottom;

    private static void ValidateWindowCaptureTarget(IntPtr hwnd, Bounds bounds)
    {
        if (!IsWindow(hwnd))
        {
            throw new CaptureOperationException("Окно для capture больше не найдено.");
        }

        if (IsIconic(hwnd))
        {
            throw new CaptureOperationException("Свернутое окно нельзя использовать для window capture. Сначала восстанови окно.");
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new CaptureOperationException("Окно для capture имеет недопустимые bounds.");
        }
    }

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, Guid iid, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hString);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hString);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        D3DDriverType driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out IntPtr device,
        out D3DFeatureLevel featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true, EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    private static extern int CreateDirect3D11DeviceFromDxgiDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out RECT pvAttribute,
        int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum D3DDriverType : uint
    {
        Hardware = 1,
        Warp = 5,
    }

    private enum D3DFeatureLevel : uint
    {
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }

    [ComImport]
    [Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiDevice
    {
    }

    private sealed record CaptureExecutionResult(
        CaptureResolvedTarget Target,
        RasterizedCapture Capture);

    private sealed record RasterizedCapture(
        byte[] PngBytes,
        int PixelWidth,
        int PixelHeight);

    private sealed class SoftwareBitmapWaitVisualEvidenceFrame(
        WindowDescriptor window,
        SoftwareBitmap bitmap) : WaitVisualEvidenceFrame(window, bitmap.PixelWidth, bitmap.PixelHeight)
    {
        private SoftwareBitmap? _bitmap = bitmap;

        public SoftwareBitmap GetBitmap() =>
            _bitmap ?? throw new ObjectDisposedException(nameof(SoftwareBitmapWaitVisualEvidenceFrame));

        protected override void DisposeCore()
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}
