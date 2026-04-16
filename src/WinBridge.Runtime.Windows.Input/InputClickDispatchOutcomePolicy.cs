using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputClickDispatchOutcomePolicy
{
    public static InputClickDispatchResult FromSendInputCounts(
        string logicalButton,
        uint insertedEvents,
        uint expectedEvents,
        uint compensationInsertedEvents,
        uint compensationExpectedEvents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalButton);

        if (insertedEvents == expectedEvents)
        {
            return new(Success: true);
        }

        if (insertedEvents == 0)
        {
            return new(
                Success: false,
                OutcomeKind: InputClickDispatchOutcomeKind.CleanFailure,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: $"Button dispatch для '{logicalButton}' не был подтверждён платформой.");
        }

        if (compensationExpectedEvents > 0 && compensationInsertedEvents == compensationExpectedEvents)
        {
            return new(
                Success: false,
                OutcomeKind: InputClickDispatchOutcomeKind.PartialDispatchCompensated,
                FailureCode: InputFailureCodeValues.InputDispatchFailed,
                Reason: $"Button dispatch для '{logicalButton}' был выполнен только частично; runtime зафиксировал partial side effect и подтвердил best-effort button-up compensation.");
        }

        return new(
            Success: false,
            OutcomeKind: InputClickDispatchOutcomeKind.PartialDispatchUncompensated,
            FailureCode: InputFailureCodeValues.InputDispatchFailed,
            Reason: $"Button dispatch для '{logicalButton}' был выполнен только частично, а компенсация button-up не подтверждена; input side effects уже могли выйти в систему.");
    }
}
