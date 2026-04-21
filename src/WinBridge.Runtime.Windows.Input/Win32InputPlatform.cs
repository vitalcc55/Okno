using System.Diagnostics;
using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class Win32InputPlatform : IInputPlatform
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int ErrorInsufficientBuffer = 122;
    private const int SecurityMandatoryMediumRid = 0x2000;
    private const int SecurityMandatoryHighRid = 0x3000;
    private const int SecurityMandatorySystemRid = 0x4000;
    private const int SmSwapButton = 23;

    public InputProcessSecurityContext ProbeCurrentProcessSecurity()
    {
        using Process process = Process.GetCurrentProcess();
        bool sessionResolved = ProcessIdToSessionId((uint)process.Id, out uint sessionId);

        if (!OpenProcessToken(process.Handle, TokenQuery, out IntPtr tokenHandle))
        {
            return new(
                SessionId: sessionResolved ? checked((int)sessionId) : null,
                SessionResolved: sessionResolved,
                IntegrityLevel: null,
                IntegrityResolved: false,
                HasUiAccess: false,
                UiAccessResolved: false,
                Reason: "Runtime не смог открыть token текущего процесса для input preflight.");
        }

        try
        {
            bool integrityResolved = TryQueryIntegrity(tokenHandle, out InputIntegrityLevel? integrityLevel);
            bool uiAccessResolved = TryQueryUInt32(tokenHandle, TokenInformationClass.TokenUIAccess, out uint tokenUiAccess);

            return new(
                SessionId: sessionResolved ? checked((int)sessionId) : null,
                SessionResolved: sessionResolved,
                IntegrityLevel: integrityLevel,
                IntegrityResolved: integrityResolved,
                HasUiAccess: uiAccessResolved && tokenUiAccess != 0,
                UiAccessResolved: uiAccessResolved,
                Reason: BuildCurrentProcessProbeReason(sessionResolved, integrityResolved, uiAccessResolved));
        }
        finally
        {
            _ = CloseHandle(tokenHandle);
        }
    }

    public InputTargetSecurityInfo ProbeTargetSecurity(long hwnd, int? processIdHint)
    {
        int? processId = processIdHint;
        if (processId is null or <= 0)
        {
            _ = GetWindowThreadProcessId(new IntPtr(hwnd), out uint liveProcessId);
            if (liveProcessId > 0)
            {
                processId = checked((int)liveProcessId);
            }
        }

        if (processId is null or <= 0)
        {
            return new(
                ProcessId: null,
                SessionId: null,
                SessionResolved: false,
                IntegrityLevel: null,
                IntegrityResolved: false,
                Reason: "Runtime не смог определить process id окна-цели для input preflight.");
        }

        bool sessionResolved = ProcessIdToSessionId((uint)processId.Value, out uint sessionId);
        IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId.Value);
        if (processHandle == IntPtr.Zero)
        {
            return new(
                ProcessId: processId,
                SessionId: sessionResolved ? checked((int)sessionId) : null,
                SessionResolved: sessionResolved,
                IntegrityLevel: null,
                IntegrityResolved: false,
                Reason: "Runtime не смог открыть target process для input preflight.");
        }

        try
        {
            if (!OpenProcessToken(processHandle, TokenQuery, out IntPtr tokenHandle))
            {
                return new(
                    ProcessId: processId,
                    SessionId: sessionResolved ? checked((int)sessionId) : null,
                    SessionResolved: sessionResolved,
                    IntegrityLevel: null,
                    IntegrityResolved: false,
                    Reason: "Runtime не смог открыть target token для input preflight.");
            }

            try
            {
                bool integrityResolved = TryQueryIntegrity(tokenHandle, out InputIntegrityLevel? integrityLevel);
                return new(
                    ProcessId: processId,
                    SessionId: sessionResolved ? checked((int)sessionId) : null,
                    SessionResolved: sessionResolved,
                    IntegrityLevel: integrityLevel,
                    IntegrityResolved: integrityResolved,
                    Reason: integrityResolved
                        ? null
                        : "Runtime не смог определить integrity окна-цели для input preflight.");
            }
            finally
            {
                _ = CloseHandle(tokenHandle);
            }
        }
        finally
        {
            _ = CloseHandle(processHandle);
        }
    }

    public bool TrySetCursorPosition(InputPoint screenPoint) =>
        SetCursorPos(screenPoint.X, screenPoint.Y);

    public bool TryGetCursorPosition(out InputPoint screenPoint)
    {
        if (GetCursorPos(out POINT point))
        {
            screenPoint = new InputPoint(point.X, point.Y);
            return true;
        }

        screenPoint = new InputPoint(0, 0);
        return false;
    }

    public InputPointerSideEffectBoundaryResult ValidatePointerSideEffectBoundary(WindowDescriptor admittedTargetWindow)
    {
        ArgumentNullException.ThrowIfNull(admittedTargetWindow);
        return ValidatePointerSideEffectBoundaryCore(admittedTargetWindow);
    }

    public InputClickDispatchResult DispatchClick(InputClickDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InputClickDispatchResult environmentResult = ValidateDispatchEnvironment(context, out bool mouseButtonsSwapped);
        if (!environmentResult.Success)
        {
            return environmentResult;
        }

        (uint downFlag, uint upFlag) = InputMouseButtonSemantics.GetDispatchFlags(context.LogicalButton, mouseButtonsSwapped);

        INPUT[] inputs =
        [
            CreateMouseInput(downFlag),
            CreateMouseInput(upFlag),
        ];

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == inputs.Length)
        {
            return new(Success: true);
        }

        uint compensationSent = 0;
        if (sent > 0)
        {
            INPUT[] compensationInputs =
            [
                CreateMouseInput(upFlag),
            ];
            compensationSent = SendInput((uint)compensationInputs.Length, compensationInputs, Marshal.SizeOf<INPUT>());
        }

        return InputClickDispatchOutcomePolicy.FromSendInputCounts(
            logicalButton: context.LogicalButton,
            insertedEvents: sent,
            expectedEvents: (uint)inputs.Length,
            compensationInsertedEvents: compensationSent,
            compensationExpectedEvents: sent > 0 ? 1u : 0u);
    }

    public InputDispatchResult DispatchText(InputTextDispatchContext context) =>
        new(
            Success: false,
            CommittedSideEffects: false,
            FailureCode: InputFailureCodeValues.UnsupportedActionType,
            Reason: "Runtime touchpoint для type_text подготовлен, но shipped Win32 text input path ещё не реализован.");

    public InputDispatchResult DispatchKeypress(InputKeypressDispatchContext context) =>
        new(
            Success: false,
            CommittedSideEffects: false,
            FailureCode: InputFailureCodeValues.UnsupportedActionType,
            Reason: "Runtime touchpoint для press_key подготовлен, но shipped Win32 keypress path ещё не реализован.");

    public InputDispatchResult DispatchScroll(InputScrollDispatchContext context) =>
        new(
            Success: false,
            CommittedSideEffects: false,
            FailureCode: InputFailureCodeValues.UnsupportedActionType,
            Reason: "Runtime touchpoint для scroll подготовлен, но shipped Win32 scroll path ещё не реализован.");

    public InputDispatchResult DispatchDrag(InputDragDispatchContext context) =>
        new(
            Success: false,
            CommittedSideEffects: false,
            FailureCode: InputFailureCodeValues.UnsupportedActionType,
            Reason: "Runtime touchpoint для drag подготовлен, но shipped Win32 drag path ещё не реализован.");

    private static InputClickDispatchResult ValidateDispatchEnvironment(InputClickDispatchContext context, out bool mouseButtonsSwapped)
    {
        mouseButtonsSwapped = false;
        if (!GetCursorPos(out POINT point))
        {
            return new(
                Success: false,
                FailureCode: InputFailureCodeValues.CursorMoveFailed,
                Reason: "Runtime не смог подтвердить cursor position непосредственно перед click dispatch.");
        }

        InputPoint currentCursorPoint = new(point.X, point.Y);
        if (!Equals(currentCursorPoint, context.ExpectedScreenPoint))
        {
            return new(
                Success: false,
                FailureCode: InputFailureCodeValues.CursorMoveFailed,
                Reason: $"Cursor position drifted before click dispatch: фактическая точка ({currentCursorPoint.X},{currentCursorPoint.Y}) не совпадает с ожидаемой ({context.ExpectedScreenPoint.X},{context.ExpectedScreenPoint.Y}).");
        }

        InputPointerSideEffectBoundaryResult boundaryResult = ValidatePointerSideEffectBoundaryCore(context.AdmittedTargetWindow);
        if (!boundaryResult.Success)
        {
            return new(
                Success: false,
                FailureCode: boundaryResult.FailureCode,
                Reason: boundaryResult.Reason);
        }

        mouseButtonsSwapped = boundaryResult.MouseButtonsSwapped;
        return new(Success: true);
    }

    private static InputPointerSideEffectBoundaryResult ValidatePointerSideEffectBoundaryCore(WindowDescriptor admittedTargetWindow)
    {
        GetForegroundWindowSnapshot(out long? foregroundHwnd, out ActivatedWindowVerificationSnapshot foregroundSnapshot);
        if (!InputForegroundTargetBoundaryPolicy.TryValidate(
                foregroundHwnd,
                foregroundSnapshot,
                admittedTargetWindow,
                out int? validatedForegroundOwnerProcessId,
                out string? foregroundFailureCode,
                out string? foregroundReason))
        {
            return new(
                Success: false,
                FailureCode: foregroundFailureCode,
                Reason: foregroundReason);
        }

        InputAmbientInputProbeContext probeContext = CreateAmbientInputProbeContext(validatedForegroundOwnerProcessId);
        InputAmbientInputProbeResult ambientInput = InputAmbientInputPolicy.Probe(probeContext, GetAsyncKeyState);
        if (ambientInput.Status != InputAmbientInputProofStatus.Neutral)
        {
            return new(
                Success: false,
                FailureCode: ambientInput.FailureCode,
                Reason: ambientInput.Reason);
        }

        return new(
            Success: true,
            MouseButtonsSwapped: probeContext.MouseButtonsSwapped);
    }

    private static InputAmbientInputProbeContext CreateAmbientInputProbeContext(int? foregroundOwnerProcessId)
    {
        InputAsyncStateReadabilityProbeResult readability = InputAsyncStateReadabilityEvaluator.ProbeForForegroundOwner(
            foregroundOwnerProcessId,
            Environment.ProcessId,
            InputAsyncStateReadabilityProbe.ProbeForCurrentThread);
        return readability.Status == InputAsyncStateReadabilityStatus.Readable
            ? new(
                CanReadAsyncState: true,
                MouseButtonsSwapped: GetSystemMetrics(SmSwapButton) != 0,
                UnknownReason: null)
            : new(
                CanReadAsyncState: false,
                MouseButtonsSwapped: false,
                UnknownReason: readability.Reason);
    }

    private static void GetForegroundWindowSnapshot(
        out long? foregroundHwnd,
        out ActivatedWindowVerificationSnapshot foregroundSnapshot)
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            foregroundHwnd = null;
            foregroundSnapshot = new(
                Exists: false,
                ProcessId: null,
                ThreadId: null,
                ClassName: null,
                IsForeground: false,
                IsMinimized: false);
            return;
        }

        foregroundHwnd = foregroundWindow.ToInt64();
        foregroundSnapshot = ProbeForegroundWindowSnapshot(foregroundWindow);
    }

    private static ActivatedWindowVerificationSnapshot ProbeForegroundWindowSnapshot(IntPtr foregroundWindow)
    {
        uint threadId = GetWindowThreadProcessId(foregroundWindow, out uint processId);
        return new(
            Exists: true,
            ProcessId: threadId == 0 ? null : checked((int)processId),
            ThreadId: threadId == 0 ? null : checked((int)threadId),
            ClassName: TryGetWindowClassName(foregroundWindow),
            IsForeground: true,
            IsMinimized: false);
    }

    private static string? TryGetWindowClassName(IntPtr hwnd)
    {
        char[] buffer = new char[256];
        int length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    private static string? BuildCurrentProcessProbeReason(bool sessionResolved, bool integrityResolved, bool uiAccessResolved)
    {
        if (!sessionResolved)
        {
            return "Runtime не смог определить session текущего процесса для input preflight.";
        }

        if (!integrityResolved)
        {
            return "Runtime не смог определить integrity текущего процесса для input preflight.";
        }

        if (!uiAccessResolved)
        {
            return "Runtime не смог определить uiAccess flag текущего процесса для input preflight.";
        }

        return null;
    }

    private static INPUT CreateMouseInput(uint flags) =>
        new()
        {
            type = InputMouse,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };

    private static bool TryQueryIntegrity(IntPtr tokenHandle, out InputIntegrityLevel? integrityLevel)
    {
        integrityLevel = null;
        if (!TryQueryBuffer(tokenHandle, TokenInformationClass.TokenIntegrityLevel, out IntPtr buffer))
        {
            return false;
        }

        try
        {
            TOKEN_MANDATORY_LABEL label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
            if (label.Label.Sid == IntPtr.Zero || !IsValidSid(label.Label.Sid))
            {
                return false;
            }

            IntPtr subAuthorityCountPointer = GetSidSubAuthorityCount(label.Label.Sid);
            if (subAuthorityCountPointer == IntPtr.Zero)
            {
                return false;
            }

            byte subAuthorityCount = Marshal.ReadByte(subAuthorityCountPointer);
            if (subAuthorityCount == 0)
            {
                return false;
            }

            IntPtr ridPointer = GetSidSubAuthority(label.Label.Sid, (uint)(subAuthorityCount - 1));
            if (ridPointer == IntPtr.Zero)
            {
                return false;
            }

            int rid = Marshal.ReadInt32(ridPointer);
            integrityLevel = MapIntegrityLevel(rid);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static InputIntegrityLevel MapIntegrityLevel(int rid)
    {
        if (rid < SecurityMandatoryMediumRid)
        {
            return InputIntegrityLevel.Low;
        }

        if (rid < SecurityMandatoryHighRid)
        {
            return InputIntegrityLevel.Medium;
        }

        if (rid < SecurityMandatorySystemRid)
        {
            return InputIntegrityLevel.High;
        }

        return InputIntegrityLevel.SystemOrAbove;
    }

    private static bool TryQueryUInt32(
        IntPtr tokenHandle,
        TokenInformationClass informationClass,
        out uint value)
    {
        value = 0;
        if (!TryQueryBuffer(tokenHandle, informationClass, out IntPtr buffer))
        {
            return false;
        }

        try
        {
            value = unchecked((uint)Marshal.ReadInt32(buffer));
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryQueryBuffer(
        IntPtr tokenHandle,
        TokenInformationClass informationClass,
        out IntPtr buffer)
    {
        buffer = IntPtr.Zero;
        _ = GetTokenInformation(tokenHandle, informationClass, IntPtr.Zero, 0, out int requiredLength);
        if (requiredLength <= 0 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
        {
            return false;
        }

        buffer = Marshal.AllocHGlobal(requiredLength);
        if (!GetTokenInformation(tokenHandle, informationClass, buffer, requiredLength, out _))
        {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            return false;
        }

        return true;
    }

    private const uint InputMouse = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_MANDATORY_LABEL
    {
        public SID_AND_ATTRIBUTES Label;
    }

    private enum TokenInformationClass
    {
        TokenIntegrityLevel = 25,
        TokenUIAccess = 26,
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool IsValidSid(IntPtr sid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
