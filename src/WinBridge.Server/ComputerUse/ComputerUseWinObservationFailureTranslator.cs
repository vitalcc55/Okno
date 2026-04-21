using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinObservationFailureTranslator
{
    public static ComputerUseWinObservationFailure Translate(Exception exception, string unexpectedReason)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(unexpectedReason);

        return exception switch
        {
            CaptureOperationException captureException => new(
                ComputerUseWinFailureCodeValues.ObservationFailed,
                captureException.Message),
            _ => new(
                ComputerUseWinFailureCodeValues.ObservationFailed,
                unexpectedReason),
        };
    }
}

internal sealed record ComputerUseWinObservationFailure(
    string FailureCode,
    string Reason);
