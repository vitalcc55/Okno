// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinDispatchClassValues
{
    public const string Semantic = "semantic";
    public const string ExpectedPhysical = "expected_physical";
    public const string FallbackPhysical = "fallback_physical";
}

internal static class ComputerUseWinTargetProofValues
{
    public const string None = "none";
    public const string UiaRevalidated = "uia_revalidated";
    public const string CapturePoint = "capture_point";
}

internal static class ComputerUseWinWindowContinuityValues
{
    public const string Accepted = "accepted";
    public const string Failed = "failed";
    public const string BestEffort = "best_effort";
    public const string Unknown = "unknown";
}

internal static class ComputerUseWinForegroundIntegrityValues
{
    public const string Accepted = "accepted";
    public const string Blocked = "blocked";
    public const string Unknown = "unknown";
}

internal sealed record ComputerUseWinExecutionFactsInputs(
    string Executor,
    bool ConfirmationRequired,
    bool Confirmed,
    bool FallbackUsed,
    string TargetProof,
    bool StateTokenPresent,
    bool CaptureReferencePresent,
    string WindowContinuity,
    string ForegroundIntegrity,
    bool ObserveAfterRequested,
    bool SuccessorStateAvailable);

internal sealed record ComputerUseWinExecutionFacts(
    string DispatchClass,
    string Executor,
    bool ConfirmationRequired,
    bool ConfirmationSatisfied,
    bool FallbackUsed,
    string TargetProof,
    bool StateTokenPresent,
    bool CaptureReferencePresent,
    string WindowContinuity,
    string ForegroundIntegrity,
    bool PhysicalPointerUsed,
    bool PhysicalKeyboardUsed,
    bool SystemCursorMoved,
    bool ObserveAfterRequested,
    bool SuccessorStateAvailable);

internal static class ComputerUseWinExecutionFactsBuilder
{
    public static ComputerUseWinExecutionFacts Build(ComputerUseWinExecutionFactsInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ValidateKnownValue(nameof(inputs.TargetProof), inputs.TargetProof, KnownTargetProofValues);
        ValidateKnownValue(nameof(inputs.WindowContinuity), inputs.WindowContinuity, KnownWindowContinuityValues);
        ValidateKnownValue(nameof(inputs.ForegroundIntegrity), inputs.ForegroundIntegrity, KnownForegroundIntegrityValues);

        string dispatchClass = ComputerUseWinPhysicalExecutionPolicy.DetermineDispatchClass(inputs.Executor, inputs.FallbackUsed);
        bool physicalPointerUsed = ComputerUseWinPhysicalExecutionPolicy.UsesPhysicalPointer(inputs.Executor);
        bool physicalKeyboardUsed = ComputerUseWinPhysicalExecutionPolicy.UsesPhysicalKeyboard(inputs.Executor);
        bool systemCursorMoved = ComputerUseWinPhysicalExecutionPolicy.MovesSystemCursor(inputs.Executor);

        return new(
            DispatchClass: dispatchClass,
            Executor: inputs.Executor,
            ConfirmationRequired: inputs.ConfirmationRequired,
            ConfirmationSatisfied: !inputs.ConfirmationRequired || inputs.Confirmed,
            FallbackUsed: inputs.FallbackUsed,
            TargetProof: inputs.TargetProof,
            StateTokenPresent: inputs.StateTokenPresent,
            CaptureReferencePresent: inputs.CaptureReferencePresent,
            WindowContinuity: inputs.WindowContinuity,
            ForegroundIntegrity: inputs.ForegroundIntegrity,
            PhysicalPointerUsed: physicalPointerUsed,
            PhysicalKeyboardUsed: physicalKeyboardUsed,
            SystemCursorMoved: systemCursorMoved,
            ObserveAfterRequested: inputs.ObserveAfterRequested,
            SuccessorStateAvailable: inputs.SuccessorStateAvailable);
    }

    private static readonly string[] KnownTargetProofValues =
    [
        ComputerUseWinTargetProofValues.None,
        ComputerUseWinTargetProofValues.UiaRevalidated,
        ComputerUseWinTargetProofValues.CapturePoint,
    ];

    private static readonly string[] KnownWindowContinuityValues =
    [
        ComputerUseWinWindowContinuityValues.Accepted,
        ComputerUseWinWindowContinuityValues.Failed,
        ComputerUseWinWindowContinuityValues.BestEffort,
        ComputerUseWinWindowContinuityValues.Unknown,
    ];

    private static readonly string[] KnownForegroundIntegrityValues =
    [
        ComputerUseWinForegroundIntegrityValues.Accepted,
        ComputerUseWinForegroundIntegrityValues.Blocked,
        ComputerUseWinForegroundIntegrityValues.Unknown,
    ];

    private static void ValidateKnownValue(string parameterName, string value, IReadOnlyList<string> knownValues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Execution facts input '{parameterName}' must be non-blank.");
        }

        if (!knownValues.Contains(value, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Execution facts input '{parameterName}' contains unsupported value '{value}'.");
        }
    }
}

internal static class ComputerUseWinPhysicalExecutionPolicy
{
    public static string DetermineDispatchClass(string executor, bool fallbackUsed)
    {
        string normalizedExecutor = NormalizeExecutor(executor);
        if (fallbackUsed)
        {
            return ComputerUseWinDispatchClassValues.FallbackPhysical;
        }

        return normalizedExecutor switch
        {
            "uia_toggle" => ComputerUseWinDispatchClassValues.Semantic,
            "uia_toggle_pattern" => ComputerUseWinDispatchClassValues.Semantic,
            "uia_invoke" => ComputerUseWinDispatchClassValues.Semantic,
            "uia_invoke_pattern" => ComputerUseWinDispatchClassValues.Semantic,
            "uia_scroll_pattern" => ComputerUseWinDispatchClassValues.Semantic,
            "uia_value_pattern" => ComputerUseWinDispatchClassValues.Semantic,
            "uia_range_value_pattern" => ComputerUseWinDispatchClassValues.Semantic,
            "uia_semantic_set" => ComputerUseWinDispatchClassValues.Semantic,
            "win32_pointer_click" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "fresh_uia_revalidation_to_input" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "fresh_uia_revalidation_to_input_drag" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "capture_pixels_input" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "screen_input" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "win32_sendinput_keypress" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "win32_sendinput_drag" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "capture_pixels_drag_input" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "screen_drag_input" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "win32_sendinput_wheel" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "win32_sendinput_unicode" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            "capture_pixels_text_input" => ComputerUseWinDispatchClassValues.ExpectedPhysical,
            _ => throw UnknownExecutor(executor),
        };
    }

    public static bool UsesPhysicalPointer(string executor) =>
        NormalizeExecutor(executor) switch
        {
            "uia_toggle" or
            "uia_toggle_pattern" or
            "uia_invoke" or
            "uia_invoke_pattern" or
            "uia_scroll_pattern" or
            "uia_value_pattern" or
            "uia_range_value_pattern" or
            "uia_semantic_set" => false,
            "win32_sendinput_keypress" or
            "win32_sendinput_unicode" => false,
            "win32_pointer_click" or
            "fresh_uia_revalidation_to_input" or
            "fresh_uia_revalidation_to_input_drag" or
            "capture_pixels_input" or
            "screen_input" or
            "win32_sendinput_drag" or
            "capture_pixels_drag_input" or
            "screen_drag_input" or
            "win32_sendinput_wheel" or
            "capture_pixels_text_input" => true,
            _ => throw UnknownExecutor(executor),
        };

    public static bool UsesPhysicalKeyboard(string executor) =>
        NormalizeExecutor(executor) switch
        {
            "win32_sendinput_keypress" or
            "win32_sendinput_unicode" or
            "capture_pixels_text_input" => true,
            "uia_toggle" or
            "uia_toggle_pattern" or
            "uia_invoke" or
            "uia_invoke_pattern" or
            "uia_scroll_pattern" or
            "uia_value_pattern" or
            "uia_range_value_pattern" or
            "uia_semantic_set" or
            "win32_pointer_click" or
            "fresh_uia_revalidation_to_input" or
            "fresh_uia_revalidation_to_input_drag" or
            "capture_pixels_input" or
            "screen_input" or
            "win32_sendinput_drag" or
            "capture_pixels_drag_input" or
            "screen_drag_input" or
            "win32_sendinput_wheel" => false,
            _ => throw UnknownExecutor(executor),
        };

    public static bool MovesSystemCursor(string executor) =>
        NormalizeExecutor(executor) switch
        {
            "win32_pointer_click" or
            "fresh_uia_revalidation_to_input" or
            "fresh_uia_revalidation_to_input_drag" or
            "capture_pixels_input" or
            "screen_input" or
            "win32_sendinput_drag" or
            "capture_pixels_drag_input" or
            "screen_drag_input" or
            "capture_pixels_text_input" => true,
            "uia_toggle" or
            "uia_toggle_pattern" or
            "uia_invoke" or
            "uia_invoke_pattern" or
            "uia_scroll_pattern" or
            "uia_value_pattern" or
            "uia_range_value_pattern" or
            "uia_semantic_set" or
            "win32_sendinput_keypress" or
            "win32_sendinput_unicode" or
            "win32_sendinput_wheel" => false,
            _ => throw UnknownExecutor(executor),
        };

    private static string NormalizeExecutor(string executor)
    {
        if (string.IsNullOrWhiteSpace(executor))
        {
            throw new InvalidOperationException("Execution facts executor must be non-blank.");
        }

        return executor;
    }

    private static InvalidOperationException UnknownExecutor(string executor) =>
        new($"Execution facts builder does not know executor '{executor}'. Extend the shared policy before publishing new execution semantics.");
}
