namespace WinBridge.Runtime.Contracts;

public static class WaitRequestValidator
{
    public static bool TryValidate(WaitRequest request, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Condition))
        {
            reason = "Параметр condition для wait не должен быть пустым.";
            return false;
        }

        if (request.TimeoutMs <= 0)
        {
            reason = "Параметр timeoutMs для wait должен быть > 0.";
            return false;
        }

        if (RequiresSelector(request.Condition) && !HasSelector(request.Selector))
        {
            reason = "Для этого wait condition нужен selector хотя бы с одним из полей: name, automationId или controlType.";
            return false;
        }

        if (string.Equals(request.Condition, WaitConditionValues.TextAppears, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(request.ExpectedText))
        {
            reason = "Для condition 'text_appears' нужно передать expectedText.";
            return false;
        }

        if (!string.Equals(request.Condition, WaitConditionValues.TextAppears, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(request.ExpectedText))
        {
            reason = "Параметр expectedText поддерживается только для condition 'text_appears'.";
            return false;
        }

        if (string.Equals(request.Condition, WaitConditionValues.ActiveWindowMatches, StringComparison.Ordinal))
        {
            reason = null;
            return true;
        }

        if (string.Equals(request.Condition, WaitConditionValues.ElementExists, StringComparison.Ordinal)
            || string.Equals(request.Condition, WaitConditionValues.ElementGone, StringComparison.Ordinal)
            || string.Equals(request.Condition, WaitConditionValues.TextAppears, StringComparison.Ordinal)
            || string.Equals(request.Condition, WaitConditionValues.FocusIs, StringComparison.Ordinal)
            || string.Equals(request.Condition, WaitConditionValues.VisualChanged, StringComparison.Ordinal))
        {
            reason = null;
            return true;
        }

        reason = $"Условие wait '{request.Condition}' не поддерживается.";
        return false;
    }

    private static bool HasSelector(WaitElementSelector? selector) =>
        selector is not null
        && (!string.IsNullOrWhiteSpace(selector.Name)
            || !string.IsNullOrWhiteSpace(selector.AutomationId)
            || !string.IsNullOrWhiteSpace(selector.ControlType));

    private static bool RequiresSelector(string condition) =>
        string.Equals(condition, WaitConditionValues.ElementExists, StringComparison.Ordinal)
        || string.Equals(condition, WaitConditionValues.ElementGone, StringComparison.Ordinal)
        || string.Equals(condition, WaitConditionValues.TextAppears, StringComparison.Ordinal)
        || string.Equals(condition, WaitConditionValues.FocusIs, StringComparison.Ordinal);
}
