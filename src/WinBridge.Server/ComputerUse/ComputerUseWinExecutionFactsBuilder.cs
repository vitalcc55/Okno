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
        ComputerUseWinExecutionExecutorDescriptor descriptor = GetDescriptor(executor);
        if (fallbackUsed)
        {
            return ComputerUseWinDispatchClassValues.FallbackPhysical;
        }

        return descriptor.DefaultDispatchClass;
    }

    public static bool UsesPhysicalPointer(string executor) =>
        GetDescriptor(executor).UsesPhysicalPointer;

    public static bool UsesPhysicalKeyboard(string executor) =>
        GetDescriptor(executor).UsesPhysicalKeyboard;

    public static bool MovesSystemCursor(string executor) =>
        GetDescriptor(executor).MovesSystemCursor;

    private static ComputerUseWinExecutionExecutorDescriptor GetDescriptor(string executor)
    {
        if (string.IsNullOrWhiteSpace(executor))
        {
            throw new InvalidOperationException("Execution facts executor must be non-blank.");
        }

        return ComputerUseWinExecutionExecutorValues.TryGetDescriptor(executor, out ComputerUseWinExecutionExecutorDescriptor? descriptor)
            ? descriptor
            : throw UnknownExecutor(executor);
    }

    private static InvalidOperationException UnknownExecutor(string executor) =>
        new($"Execution facts builder does not know executor '{executor}'. Extend the shared policy before publishing new execution semantics.");
}
