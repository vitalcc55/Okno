using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Launch;

internal sealed class ShellExecuteOpenTargetPlatform : IOpenTargetPlatform
{
    private const uint SeeMaskNoCloseProcess = 0x00000040;
    private const uint SeeMaskNoAsync = 0x00000100;
    private const uint SeeMaskFlagNoUi = 0x00000400;
    private const int SwShownormal = 1;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int SeErrAssocIncomplete = 27;
    private const int SeErrNoAssoc = 31;

    private readonly IShellExecuteNativeAdapter _nativeAdapter;
    private readonly IShellExecuteStaExecutor _staExecutor;

    internal const uint ExpectedMask = SeeMaskNoCloseProcess | SeeMaskNoAsync | SeeMaskFlagNoUi;

    public ShellExecuteOpenTargetPlatform()
        : this(new ShellExecuteNativeAdapter(), new DedicatedStaShellExecuteExecutor())
    {
    }

    internal ShellExecuteOpenTargetPlatform(
        IShellExecuteNativeAdapter nativeAdapter,
        IShellExecuteStaExecutor staExecutor)
    {
        _nativeAdapter = nativeAdapter;
        _staExecutor = staExecutor;
    }

    public OpenTargetPlatformResult Open(OpenTargetPlatformRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Target);

        return _staExecutor.Execute(() =>
        {
            ShellExecuteInvocation invocation = CreateInvocation(request);
            ShellExecuteNativeResult nativeResult = _nativeAdapter.Execute(invocation);
            return MapNativeResult(nativeResult);
        });
    }

    private static ShellExecuteInvocation CreateInvocation(OpenTargetPlatformRequest request) =>
        new(
            Target: request.Target,
            Mask: ExpectedMask,
            Verb: null,
            Parameters: null,
            Directory: null,
            ShowCommand: SwShownormal);

    private OpenTargetPlatformResult MapNativeResult(ShellExecuteNativeResult nativeResult)
    {
        int shellCode = nativeResult.HInstApp != nint.Zero
            ? unchecked((int)nativeResult.HInstApp.ToInt64())
            : nativeResult.LastError;

        if (nativeResult.Succeeded && shellCode > 32)
        {
            int? handlerProcessId = null;
            if (nativeResult.ProcessHandle != nint.Zero)
            {
                try
                {
                    uint? processId = _nativeAdapter.TryGetProcessId(nativeResult.ProcessHandle);
                    if (processId is > 0 and <= int.MaxValue)
                    {
                        handlerProcessId = (int)processId.Value;
                    }
                }
                finally
                {
                    _nativeAdapter.CloseHandle(nativeResult.ProcessHandle);
                }
            }

            return new OpenTargetPlatformResult(
                IsAccepted: true,
                HandlerProcessId: handlerProcessId);
        }

        return shellCode switch
        {
            ErrorFileNotFound or ErrorPathNotFound => new OpenTargetPlatformResult(
                IsAccepted: false,
                FailureCode: OpenTargetFailureCodeValues.TargetNotFound,
                FailureReason: "Shell-open target не найден."),
            ErrorAccessDenied => new OpenTargetPlatformResult(
                IsAccepted: false,
                FailureCode: OpenTargetFailureCodeValues.TargetAccessDenied,
                FailureReason: "Shell-open target отклонён из-за access denied."),
            SeErrAssocIncomplete or SeErrNoAssoc => new OpenTargetPlatformResult(
                IsAccepted: false,
                FailureCode: OpenTargetFailureCodeValues.NoAssociation,
                FailureReason: "Для shell-open target не найдена готовая application association."),
            _ => new OpenTargetPlatformResult(
                IsAccepted: false,
                FailureCode: OpenTargetFailureCodeValues.ShellRejectedTarget,
                FailureReason: "Shell не принял open request для target."),
        };
    }
}

internal readonly record struct ShellExecuteInvocation(
    string Target,
    uint Mask,
    string? Verb,
    string? Parameters,
    string? Directory,
    int ShowCommand);

internal readonly record struct ShellExecuteNativeResult(
    bool Succeeded,
    nint HInstApp,
    nint ProcessHandle,
    int LastError);

internal interface IShellExecuteNativeAdapter
{
    ShellExecuteNativeResult Execute(ShellExecuteInvocation invocation);

    uint? TryGetProcessId(nint processHandle);

    void CloseHandle(nint processHandle);
}

internal interface IShellExecuteStaExecutor
{
    T Execute<T>(Func<T> callback);
}

internal sealed class DedicatedStaShellExecuteExecutor : IShellExecuteStaExecutor
{
    private const int CoInitApartmentThreaded = 0x2;
    private const int HResultOk = 0;
    private const int HResultFalse = 1;
    private const int RpcEChangedMode = unchecked((int)0x80010106);

    public T Execute<T>(Func<T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        T? result = default;
        ExceptionDispatchInfo? capturedException = null;
        using ManualResetEventSlim completed = new(false);
        Thread thread = new(() =>
        {
            bool shouldUninitialize = false;

            try
            {
                int hr = CoInitializeEx(nint.Zero, CoInitApartmentThreaded);
                if (hr is HResultOk or HResultFalse)
                {
                    shouldUninitialize = true;
                }
                else if (hr != RpcEChangedMode)
                {
                    throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException($"CoInitializeEx failed with HRESULT 0x{hr:X8}.");
                }

                result = callback();
            }
            catch (Exception exception)
            {
                capturedException = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                if (shouldUninitialize)
                {
                    CoUninitialize();
                }

                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "ShellExecuteOpenTargetPlatform.STA",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completed.Wait();
        capturedException?.Throw();
        return result!;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint pvReserved, int dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}

internal sealed class ShellExecuteNativeAdapter : IShellExecuteNativeAdapter
{
    public ShellExecuteNativeResult Execute(ShellExecuteInvocation invocation)
    {
        SHELLEXECUTEINFOW executeInfo = new()
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFOW>(),
            fMask = invocation.Mask,
            lpVerb = invocation.Verb,
            lpFile = invocation.Target,
            lpParameters = invocation.Parameters,
            lpDirectory = invocation.Directory,
            nShow = invocation.ShowCommand,
        };

        bool succeeded = ShellExecuteExW(ref executeInfo);
        int lastError = succeeded ? 0 : Marshal.GetLastWin32Error();
        return new ShellExecuteNativeResult(
            Succeeded: succeeded,
            HInstApp: executeInfo.hInstApp,
            ProcessHandle: executeInfo.hProcess,
            LastError: lastError);
    }

    public uint? TryGetProcessId(nint processHandle)
    {
        if (processHandle == nint.Zero)
        {
            return null;
        }

        uint processId = GetProcessId(processHandle);
        return processId == 0 ? null : processId;
    }

    public void CloseHandle(nint processHandle)
    {
        if (processHandle != nint.Zero)
        {
            _ = CloseHandleNative(processHandle);
        }
    }

    [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFOW lpExecInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessId(nint handle);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static extern bool CloseHandleNative(nint handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFOW
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIconOrMonitor;
        public nint hProcess;
    }
}
