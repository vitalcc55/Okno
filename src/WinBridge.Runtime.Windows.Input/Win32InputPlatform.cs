using System.Diagnostics;
using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class Win32InputPlatform : IInputPlatform
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfExtendedkey = 0x0001;
    private const uint KeyeventfUnicode = 0x0004;
    private const uint MouseeventfWheel = 0x0800;
    private const uint MouseeventfHwheel = 0x01000;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLwin = 0x5B;
    private const int ErrorInsufficientBuffer = 122;
    private const int SecurityMandatoryMediumRid = 0x2000;
    private const int SecurityMandatoryHighRid = 0x3000;
    private const int SecurityMandatorySystemRid = 0x4000;
    private const int SmSwapButton = 23;
    private static readonly Dictionary<string, ushort> NamedKeyVirtualKeys =
        new(StringComparer.Ordinal)
        {
            ["tab"] = 0x09,
            ["enter"] = 0x0D,
            ["escape"] = 0x1B,
            ["delete"] = 0x2E,
            ["backspace"] = 0x08,
            ["space"] = 0x20,
            ["up"] = 0x26,
            ["down"] = 0x28,
            ["left"] = 0x25,
            ["right"] = 0x27,
            ["home"] = 0x24,
            ["end"] = 0x23,
            ["page_up"] = 0x21,
            ["page_down"] = 0x22,
            ["insert"] = 0x2D,
            ["f1"] = 0x70,
            ["f2"] = 0x71,
            ["f3"] = 0x72,
            ["f4"] = 0x73,
            ["f5"] = 0x74,
            ["f6"] = 0x75,
            ["f7"] = 0x76,
            ["f8"] = 0x77,
            ["f9"] = 0x78,
            ["f10"] = 0x79,
            ["f11"] = 0x7A,
            ["f12"] = 0x7B,
        };
    private static readonly HashSet<ushort> ExtendedVirtualKeys =
        [0x25, 0x26, 0x27, 0x28, 0x21, 0x22, 0x23, 0x24, 0x2D, 0x2E, VkLwin];

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

    public InputDispatchResult DispatchText(InputTextDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InputPointerSideEffectBoundaryResult boundaryResult = ValidatePointerSideEffectBoundaryCore(context.AdmittedTargetWindow);
        if (!boundaryResult.Success)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: boundaryResult.FailureCode,
                Reason: boundaryResult.Reason);
        }

        if (!TryBuildTextInputs(context.Text, out INPUT[]? inputs, out string? failureCode, out string? reason))
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: failureCode ?? InputFailureCodeValues.InvalidRequest,
                Reason: reason ?? "Runtime не смог подготовить text dispatch.");
        }

        uint sent = SendInput((uint)inputs!.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            return new(
                Success: false,
                CommittedSideEffects: sent > 0,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "SendInput не подтвердил полный text dispatch.");
        }

        return new(
            Success: true,
            CommittedSideEffects: true);
    }

    public InputDispatchResult DispatchKeypress(InputKeypressDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InputPointerSideEffectBoundaryResult boundaryResult = ValidatePointerSideEffectBoundaryCore(context.AdmittedTargetWindow);
        if (!boundaryResult.Success)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: boundaryResult.FailureCode,
                Reason: boundaryResult.Reason);
        }

        if (!TryBuildKeypressInputs(context.Key, context.Repeat, out INPUT[]? inputs, out string? failureCode, out string? reason))
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: failureCode ?? InputFailureCodeValues.UnsupportedKey,
                Reason: reason ?? "Runtime не смог нормализовать key literal для keypress dispatch.");
        }

        uint sent = SendInput((uint)inputs!.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            if (sent == 0u)
            {
                return new(
                    Success: false,
                    CommittedSideEffects: false,
                    FailureCode: InputFailureCodeValues.InputDispatchFailed,
                    Reason: "SendInput не подтвердил полный keypress dispatch.");
            }

            INPUT[] compensationInputs = CreateKeypressCompensationInputs(inputs, sent);
            bool compensationSucceeded = compensationInputs.Length == 0;
            if (!compensationSucceeded)
            {
                uint released = SendInput((uint)compensationInputs.Length, compensationInputs, Marshal.SizeOf<INPUT>());
                compensationSucceeded = released == compensationInputs.Length;
            }

            return new(
                Success: false,
                CommittedSideEffects: true,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: compensationSucceeded
                    ? "SendInput не подтвердил полный keypress dispatch; key-up compensation succeeded."
                    : "SendInput не подтвердил полный keypress dispatch; key-up compensation failed.",
                FailureStageHint: compensationSucceeded
                    ? InputFailureStageValues.KeypressDispatchPartialCompensated
                    : InputFailureStageValues.KeypressDispatchPartialUncompensated);
        }

        return new(
            Success: true,
            CommittedSideEffects: true);
    }

    public InputDispatchResult DispatchScroll(InputScrollDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InputClickDispatchResult environmentResult = ValidateDispatchEnvironment(
            new InputClickDispatchContext(
                context.ExpectedScreenPoint,
                InputButtonValues.Left,
                context.AdmittedTargetWindow),
            out _);
        if (!environmentResult.Success)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: environmentResult.FailureCode,
                Reason: environmentResult.Reason);
        }

        if (!TryCreateScrollInput(context.Direction, context.Delta, out INPUT scrollInput, out string? failureCode, out string? reason))
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: failureCode ?? InputFailureCodeValues.InvalidRequest,
                Reason: reason ?? "Runtime не смог подготовить scroll dispatch.");
        }

        uint sent = SendInput(1u, [scrollInput], Marshal.SizeOf<INPUT>());
        if (sent != 1u)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "SendInput не подтвердил scroll dispatch.");
        }

        return new(
            Success: true,
            CommittedSideEffects: true);
    }

    public InputDispatchResult DispatchDrag(InputDragDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ScreenPath.Count < 2)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: InputFailureCodeValues.InvalidRequest,
                Reason: "Drag dispatch требует screen path минимум из двух точек.");
        }

        InputPoint startPoint = context.ScreenPath[0];
        InputClickDispatchResult environmentResult = ValidateDispatchEnvironment(
            new InputClickDispatchContext(
                startPoint,
                InputButtonValues.Left,
                context.AdmittedTargetWindow),
            out bool mouseButtonsSwapped);
        if (!environmentResult.Success)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: environmentResult.FailureCode,
                Reason: environmentResult.Reason);
        }

        (uint downFlag, uint upFlag) = InputMouseButtonSemantics.GetDispatchFlags(InputButtonValues.Left, mouseButtonsSwapped);

        uint downSent = SendInput(1u, [CreateMouseInput(downFlag)], Marshal.SizeOf<INPUT>());
        if (downSent != 1u)
        {
            return new(
                Success: false,
                CommittedSideEffects: false,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "SendInput не подтвердил drag button-down dispatch.");
        }

        for (int index = 1; index < context.ScreenPath.Count; index++)
        {
            InputPoint dragPoint = context.ScreenPath[index];
            if (!TrySetCursorPosition(dragPoint))
            {
                return CreateDragPostDownFailure(
                    upFlag,
                    InputFailureCodeValues.CursorMoveFailed,
                    $"SetCursorPos вернул failure для drag path point {index}.");
            }

            if (!TryGetCursorPosition(out InputPoint observedPoint))
            {
                return CreateDragPostDownFailure(
                    upFlag,
                    InputFailureCodeValues.CursorMoveFailed,
                    $"Runtime не смог подтвердить cursor position после drag path point {index}.");
            }

            if (!Equals(observedPoint, dragPoint))
            {
                return CreateDragPostDownFailure(
                    upFlag,
                    InputFailureCodeValues.CursorMoveFailed,
                    $"Cursor position drifted during drag on path point {index}: фактическая точка ({observedPoint.X},{observedPoint.Y}) не совпадает с ожидаемой ({dragPoint.X},{dragPoint.Y}).");
            }

            InputPointerSideEffectBoundaryResult boundaryResult = ValidateForegroundBoundaryDuringDrag(context.AdmittedTargetWindow);
            if (!boundaryResult.Success)
            {
                return CreateDragPostDownFailure(
                    upFlag,
                    boundaryResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                    boundaryResult.Reason ?? "Runtime потерял foreground boundary во время drag dispatch.");
            }
        }

        uint upSent = SendInput(1u, [CreateMouseInput(upFlag)], Marshal.SizeOf<INPUT>());
        if (upSent != 1u)
        {
            return new(
                Success: false,
                CommittedSideEffects: true,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: "SendInput не подтвердил drag button-up dispatch.",
                FailureStageHint: InputFailureStageValues.DragDispatchPartialUncompensated);
        }

        return new(
            Success: true,
            CommittedSideEffects: true);
    }

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

    private static InputPointerSideEffectBoundaryResult ValidateForegroundBoundaryDuringDrag(WindowDescriptor admittedTargetWindow)
    {
        GetForegroundWindowSnapshot(out long? foregroundHwnd, out ActivatedWindowVerificationSnapshot foregroundSnapshot);
        if (!InputForegroundTargetBoundaryPolicy.TryValidate(
                foregroundHwnd,
                foregroundSnapshot,
                admittedTargetWindow,
                out _,
                out string? foregroundFailureCode,
                out string? foregroundReason))
        {
            return new(
                Success: false,
                FailureCode: foregroundFailureCode,
                Reason: foregroundReason);
        }

        return new(Success: true);
    }

    private static InputDispatchResult CreateDragPostDownFailure(
        uint upFlag,
        string failureCode,
        string reason)
    {
        uint releaseSent = SendInput(1u, [CreateMouseInput(upFlag)], Marshal.SizeOf<INPUT>());
        bool releaseSucceeded = releaseSent == 1u;
        return new(
            Success: false,
            CommittedSideEffects: true,
            FailureCode: failureCode,
            Reason: releaseSucceeded
                ? $"{reason} Best-effort drag button-up compensation succeeded."
                : $"{reason} Best-effort drag button-up compensation also failed.",
            FailureStageHint: releaseSucceeded
                ? InputFailureStageValues.DragDispatchPartialCompensated
                : InputFailureStageValues.DragDispatchPartialUncompensated);
    }

    private static bool TryBuildKeypressInputs(
        string keyLiteral,
        int repeat,
        out INPUT[]? inputs,
        out string? failureCode,
        out string? reason)
    {
        inputs = null;
        failureCode = null;
        reason = null;

        if (repeat < InputActionScalarConstraints.MinimumRepeat
            || repeat > InputActionScalarConstraints.MaximumKeypressRepeat)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Runtime не поддерживает repeat вне диапазона {InputActionScalarConstraints.MinimumRepeat}..{InputActionScalarConstraints.MaximumKeypressRepeat} для keypress dispatch.";
            return false;
        }

        string[] rawTokens = keyLiteral
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeKeypressToken)
            .ToArray();
        if (rawTokens.Length == 0)
        {
            failureCode = InputFailureCodeValues.UnsupportedKey;
            reason = "Runtime не смог разобрать key literal для keypress dispatch.";
            return false;
        }

        string baseToken = rawTokens[^1];
        string[] modifiers = rawTokens[..^1];
        if (modifiers.Any(token => ResolveModifierVirtualKey(token) is null))
        {
            failureCode = InputFailureCodeValues.UnsupportedKey;
            reason = $"Runtime не поддерживает modifier combo '{keyLiteral}' для keypress dispatch.";
            return false;
        }

        if (!TryResolveBaseVirtualKey(baseToken, out ushort baseVirtualKey, out bool baseIsExtended, out failureCode, out reason))
        {
            return false;
        }

        List<INPUT> sequence = [];
        foreach (string modifier in modifiers)
        {
            sequence.Add(CreateKeyInput(ResolveModifierVirtualKey(modifier)!.Value, keyUp: false, isExtended: modifier == "win"));
        }

        for (int index = 0; index < repeat; index++)
        {
            sequence.Add(CreateKeyInput(baseVirtualKey, keyUp: false, isExtended: baseIsExtended));
            sequence.Add(CreateKeyInput(baseVirtualKey, keyUp: true, isExtended: baseIsExtended));
        }

        for (int index = modifiers.Length - 1; index >= 0; index--)
        {
            string modifier = modifiers[index];
            sequence.Add(CreateKeyInput(ResolveModifierVirtualKey(modifier)!.Value, keyUp: true, isExtended: modifier == "win"));
        }

        inputs = [.. sequence];
        return true;
    }

    private static bool TryBuildTextInputs(
        string text,
        out INPUT[]? inputs,
        out string? failureCode,
        out string? reason)
    {
        inputs = null;
        failureCode = null;
        reason = null;

        if (text.Length == 0)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Runtime не поддерживает empty string для text dispatch.";
            return false;
        }

        List<INPUT> sequence = new(text.Length * 2);
        foreach (char codeUnit in text)
        {
            sequence.Add(CreateUnicodeKeyInput(codeUnit, keyUp: false));
            sequence.Add(CreateUnicodeKeyInput(codeUnit, keyUp: true));
        }

        inputs = [.. sequence];
        return true;
    }

    private static INPUT[] CreateKeypressCompensationInputs(INPUT[] attemptedInputs, uint sentInputs)
    {
        ArgumentNullException.ThrowIfNull(attemptedInputs);

        List<KeypressCompensationKey> pressedKeys = [];
        int observedCount = Math.Min(attemptedInputs.Length, checked((int)sentInputs));
        for (int index = 0; index < observedCount; index++)
        {
            INPUT input = attemptedInputs[index];
            if (input.type != InputKeyboard)
            {
                continue;
            }

            KEYBDINPUT keyInput = input.union.ki;
            if ((keyInput.dwFlags & KeyeventfUnicode) != 0u)
            {
                continue;
            }

            KeypressCompensationKey key = new(
                keyInput.wVk,
                (keyInput.dwFlags & KeyeventfExtendedkey) != 0u);
            bool keyUp = (keyInput.dwFlags & KeyeventfKeyup) != 0u;
            if (!keyUp)
            {
                pressedKeys.Add(key);
                continue;
            }

            for (int pressedIndex = pressedKeys.Count - 1; pressedIndex >= 0; pressedIndex--)
            {
                if (pressedKeys[pressedIndex].Equals(key))
                {
                    pressedKeys.RemoveAt(pressedIndex);
                    break;
                }
            }
        }

        if (pressedKeys.Count == 0)
        {
            return [];
        }

        INPUT[] compensationInputs = new INPUT[pressedKeys.Count];
        for (int index = 0; index < pressedKeys.Count; index++)
        {
            KeypressCompensationKey key = pressedKeys[^(index + 1)];
            compensationInputs[index] = CreateKeyInput(key.VirtualKey, keyUp: true, key.IsExtended);
        }

        return compensationInputs;
    }

    private static bool TryResolveBaseVirtualKey(
        string baseToken,
        out ushort virtualKey,
        out bool isExtended,
        out string? failureCode,
        out string? reason)
    {
        if (NamedKeyVirtualKeys.TryGetValue(baseToken, out virtualKey))
        {
            isExtended = ExtendedVirtualKeys.Contains(virtualKey);
            failureCode = null;
            reason = null;
            return true;
        }

        if (baseToken.Length == 1 && char.IsLetterOrDigit(baseToken[0]))
        {
            char normalizedBaseKey = char.ToUpperInvariant(baseToken[0]);
            if (char.IsAsciiLetter(normalizedBaseKey) || char.IsAsciiDigit(normalizedBaseKey))
            {
                virtualKey = normalizedBaseKey;
                isExtended = false;
                failureCode = null;
                reason = null;
                return true;
            }
        }

        isExtended = false;
        virtualKey = 0;
        failureCode = InputFailureCodeValues.UnsupportedKey;
        reason = $"Runtime не поддерживает key literal '{baseToken}' для keypress dispatch.";
        return false;
    }

    private static ushort? ResolveModifierVirtualKey(string token) =>
        token switch
        {
            "ctrl" => VkControl,
            "alt" => VkMenu,
            "shift" => VkShift,
            "win" => VkLwin,
            _ => null,
        };

    private static string NormalizeKeypressToken(string token)
    {
        string normalized = token.Trim().ToLowerInvariant();
        return normalized switch
        {
            "control" => "ctrl",
            "esc" => "escape",
            "return" => "enter",
            "arrow_up" => "up",
            "arrow_down" => "down",
            "arrow_left" => "left",
            "arrow_right" => "right",
            "pageup" => "page_up",
            "pagedown" => "page_down",
            _ => normalized,
        };
    }

    private static INPUT CreateKeyInput(ushort virtualKey, bool keyUp, bool isExtended)
    {
        uint flags = keyUp ? KeyeventfKeyup : 0u;
        if (isExtended)
        {
            flags |= KeyeventfExtendedkey;
        }

        return new INPUT
        {
            type = InputKeyboard,
            union = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = flags,
                    dwExtraInfo = IntPtr.Zero,
                    time = 0,
                },
            },
        };
    }

    private static INPUT CreateUnicodeKeyInput(char codeUnit, bool keyUp)
    {
        uint flags = KeyeventfUnicode;
        if (keyUp)
        {
            flags |= KeyeventfKeyup;
        }

        return new INPUT
        {
            type = InputKeyboard,
            union = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = codeUnit,
                    dwFlags = flags,
                    dwExtraInfo = IntPtr.Zero,
                    time = 0,
                },
            },
        };
    }

    private static bool TryCreateScrollInput(
        string direction,
        int delta,
        out INPUT input,
        out string? failureCode,
        out string? reason)
    {
        if (delta == 0)
        {
            input = default;
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Runtime не поддерживает zero delta для scroll dispatch.";
            return false;
        }

        uint flags = direction switch
        {
            "up" or "down" => MouseeventfWheel,
            "left" or "right" => MouseeventfHwheel,
            _ => 0u,
        };
        if (flags == 0u)
        {
            input = default;
            failureCode = InputFailureCodeValues.UnsupportedActionType;
            reason = $"Runtime не поддерживает direction '{direction}' для scroll dispatch.";
            return false;
        }

        input = new INPUT
        {
            type = InputMouse,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = unchecked((uint)delta),
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
        failureCode = null;
        reason = null;
        return true;
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

    private readonly record struct KeypressCompensationKey(ushort VirtualKey, bool IsExtended);

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
    private const uint InputKeyboard = 1;

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

        [FieldOffset(0)]
        public KEYBDINPUT ki;
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
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
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
