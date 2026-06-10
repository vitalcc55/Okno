// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public static class UiaSemanticLookupViewValues
{
    public const string Raw = "raw";
}

public static class UiaSemanticLookupStatusValues
{
    public const string UniqueMatch = "unique_match";
    public const string ZeroMatches = "zero_matches";
    public const string AmbiguousMatches = "ambiguous_matches";
    public const string BudgetExceeded = "budget_exceeded";
    public const string Timeout = "timeout";
    public const string Failed = "failed";
}

public static class UiaSemanticLookupFailureKindValues
{
    public const string InvalidRequest = "invalid_request";
    public const string ProviderFailure = "provider_failure";
    public const string RootUnavailable = "root_unavailable";
}

public static class UiaSemanticLookupDefaults
{
    public const int MaxDepth = 12;
    public const int MaxNodes = 1024;
    public const int TimeoutMs = 3000;
    public const int MaxDepthCeiling = 64;
    public const int MaxNodesCeiling = 4096;
    public const int TimeoutMsCeiling = 10000;
    public const string View = UiaSemanticLookupViewValues.Raw;
}

public sealed record UiaSemanticLookupRequest
{
    public WaitElementSelector? Selector { get; init; }

    public int MaxDepth { get; init; } = UiaSemanticLookupDefaults.MaxDepth;

    public int MaxNodes { get; init; } = UiaSemanticLookupDefaults.MaxNodes;

    public int TimeoutMs { get; init; } = UiaSemanticLookupDefaults.TimeoutMs;
}

public sealed record UiaSemanticLookupResult(
    string Status,
    ObservedWindowDescriptor? Window = null,
    UiaElementSnapshot? Element = null,
    int VisitedNodeCount = 0,
    int MatchCount = 0,
    string MatchCardinality = ElementSelectorMatchCardinalityValues.None,
    bool NodeBudgetExceeded = false,
    bool DepthBudgetExceeded = false,
    int MaxDepth = UiaSemanticLookupDefaults.MaxDepth,
    int MaxNodes = UiaSemanticLookupDefaults.MaxNodes,
    string View = UiaSemanticLookupDefaults.View,
    string? FailureKind = null,
    string? Reason = null)
{
    public bool IsUniqueMatch => string.Equals(Status, UiaSemanticLookupStatusValues.UniqueMatch, StringComparison.Ordinal);
}

public interface IUiAutomationSemanticLookupService
{
    Task<UiaSemanticLookupResult> LookupAsync(
        WindowDescriptor targetWindow,
        UiaSemanticLookupRequest request,
        CancellationToken cancellationToken);
}

internal static class UiaSemanticLookupRequestValidator
{
    public static bool TryValidate(UiaSemanticLookupRequest request, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ElementSelectorPolicy.HasCriteria(request.Selector))
        {
            reason = "Для semantic lookup нужен selector хотя бы с одним из полей: name, automationId или controlType.";
            return false;
        }

        if (request.MaxDepth < 0 || request.MaxDepth > UiaSemanticLookupDefaults.MaxDepthCeiling)
        {
            reason = $"Параметр maxDepth для semantic lookup должен быть в диапазоне 0..{UiaSemanticLookupDefaults.MaxDepthCeiling}.";
            return false;
        }

        if (request.MaxNodes <= 0 || request.MaxNodes > UiaSemanticLookupDefaults.MaxNodesCeiling)
        {
            reason = $"Параметр maxNodes для semantic lookup должен быть в диапазоне 1..{UiaSemanticLookupDefaults.MaxNodesCeiling}.";
            return false;
        }

        if (request.TimeoutMs <= 0 || request.TimeoutMs > UiaSemanticLookupDefaults.TimeoutMsCeiling)
        {
            reason = $"Параметр timeoutMs для semantic lookup должен быть в диапазоне 1..{UiaSemanticLookupDefaults.TimeoutMsCeiling}.";
            return false;
        }

        reason = null;
        return true;
    }
}
