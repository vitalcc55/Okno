// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinSemanticTargetResolver(
    IUiAutomationService uiAutomationService,
    IUiAutomationSemanticLookupService semanticLookupService)
{
    public static bool TryClassifyBeforeActivation(
        ComputerUseWinStoredState state,
        int? elementIndex,
        WaitElementSelector? selector,
        ComputerUseWinSemanticTargetPolicy policy,
        out ComputerUseWinStoredElement? storedElement,
        out ComputerUseWinFailureDetails? failure)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(policy);

        storedElement = null;
        failure = null;
        if (selector is not null)
        {
            return false;
        }

        if (elementIndex is not int index)
        {
            failure = ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.InvalidRequest,
                "Semantic action target не был передан после contract validation.");
            return true;
        }

        if (!state.Elements.TryGetValue(index, out storedElement) || storedElement is null)
        {
            failure = ComputerUseWinFailureDetails.Expected(
                policy.MissingTargetFailureCode,
                string.Format(CultureInfo.InvariantCulture, policy.MissingTargetReason, index));
            return true;
        }

        if (!policy.IsActionable(storedElement))
        {
            failure = ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.UnsupportedAction,
                string.Format(CultureInfo.InvariantCulture, policy.PreviewUnsupportedReason, index));
            return true;
        }

        return false;
    }

    public Task<ComputerUseWinSemanticTargetResolution> ResolveAsync(
        ComputerUseWinStoredState state,
        int? elementIndex,
        WaitElementSelector? selector,
        ComputerUseWinSemanticTargetPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(policy);

        return selector is not null
            ? ResolveSelectorTargetAsync(state, selector, policy, cancellationToken)
            : ResolveElementIndexTargetAsync(state, elementIndex, policy, cancellationToken);
    }

    private async Task<ComputerUseWinSemanticTargetResolution> ResolveElementIndexTargetAsync(
        ComputerUseWinStoredState state,
        int? elementIndex,
        ComputerUseWinSemanticTargetPolicy policy,
        CancellationToken cancellationToken)
    {
        if (TryClassifyBeforeActivation(state, elementIndex, selector: null, policy, out ComputerUseWinStoredElement? storedElement, out ComputerUseWinFailureDetails? failure))
        {
            return ComputerUseWinSemanticTargetResolution.Failure(failure!);
        }

        try
        {
            UiaSnapshotResult snapshot = await uiAutomationService.SnapshotAsync(
                state.Window,
                new UiaSnapshotRequest
                {
                    Depth = state.Observation.RequestedDepth,
                    MaxNodes = state.Observation.RequestedMaxNodes,
                },
                cancellationToken).ConfigureAwait(false);

            if (!string.Equals(snapshot.Status, UiaSnapshotStatusValues.Done, StringComparison.Ordinal)
                || snapshot.Root is null)
            {
                return ComputerUseWinSemanticTargetResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.ObservationFailed,
                        snapshot.Reason ?? policy.FreshObservationFailureReason));
            }

            IReadOnlyDictionary<int, ComputerUseWinStoredElement> freshElements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
            if (!ComputerUseWinFreshElementResolver.TryResolve(freshElements, storedElement!, out ComputerUseWinStoredElement? freshElement)
                || freshElement is null)
            {
                return ComputerUseWinSemanticTargetResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.StaleState,
                        policy.FreshStaleReason));
            }

            if (!policy.IsActionable(freshElement))
            {
                return ComputerUseWinSemanticTargetResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.UnsupportedAction,
                        policy.FreshUnsupportedReason));
            }

            return ComputerUseWinSemanticTargetResolution.Success(freshElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ComputerUseWinFailureDetails failureDetails = ComputerUseWinObservationFailureTranslator.Translate(
                exception,
                policy.FreshObservationFailureReason);
            return ComputerUseWinSemanticTargetResolution.Failure(failureDetails);
        }
    }

    private async Task<ComputerUseWinSemanticTargetResolution> ResolveSelectorTargetAsync(
        ComputerUseWinStoredState state,
        WaitElementSelector selector,
        ComputerUseWinSemanticTargetPolicy policy,
        CancellationToken cancellationToken)
    {
        try
        {
            UiaSemanticLookupResult lookup = await semanticLookupService.LookupAsync(
                state.Window,
                new UiaSemanticLookupRequest
                {
                    Selector = selector,
                    MaxDepth = UiaSemanticLookupDefaults.MaxDepth,
                    MaxNodes = Math.Max(state.Observation.RequestedMaxNodes, UiaSemanticLookupDefaults.MaxNodes),
                    TimeoutMs = UiaSemanticLookupDefaults.TimeoutMs,
                },
                cancellationToken).ConfigureAwait(false);

            if (!lookup.IsUniqueMatch || lookup.Element is null)
            {
                return ComputerUseWinSemanticTargetResolution.Failure(MapLookupFailure(lookup, policy));
            }

            ComputerUseWinStoredElement effectiveElement = ProjectLookupElement(lookup.Element);
            if (!policy.IsActionable(effectiveElement))
            {
                return ComputerUseWinSemanticTargetResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.UnsupportedAction,
                        policy.SelectorUnsupportedReason));
            }

            return ComputerUseWinSemanticTargetResolution.Success(effectiveElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ComputerUseWinFailureDetails failureDetails = ComputerUseWinObservationFailureTranslator.Translate(
                exception,
                policy.SelectorObservationFailureReason);
            return ComputerUseWinSemanticTargetResolution.Failure(failureDetails);
        }
    }

    private static ComputerUseWinFailureDetails MapLookupFailure(
        UiaSemanticLookupResult lookup,
        ComputerUseWinSemanticTargetPolicy policy)
    {
        string reason = lookup.Status switch
        {
            UiaSemanticLookupStatusValues.ZeroMatches => policy.SelectorZeroMatchesReason,
            UiaSemanticLookupStatusValues.AmbiguousMatches => policy.SelectorAmbiguousReason,
            UiaSemanticLookupStatusValues.BudgetExceeded => policy.SelectorBudgetExceededReason,
            UiaSemanticLookupStatusValues.Timeout => policy.SelectorTimeoutReason,
            UiaSemanticLookupStatusValues.Failed when string.Equals(lookup.FailureKind, UiaSemanticLookupFailureKindValues.InvalidRequest, StringComparison.Ordinal) =>
                lookup.Reason ?? "Selector request не прошёл validation.",
            _ => policy.SelectorObservationFailureReason,
        };

        string failureCode = lookup.Status switch
        {
            UiaSemanticLookupStatusValues.ZeroMatches => ComputerUseWinFailureCodeValues.StaleState,
            UiaSemanticLookupStatusValues.AmbiguousMatches => ComputerUseWinFailureCodeValues.AmbiguousTarget,
            UiaSemanticLookupStatusValues.Failed when string.Equals(lookup.FailureKind, UiaSemanticLookupFailureKindValues.InvalidRequest, StringComparison.Ordinal) =>
                ComputerUseWinFailureCodeValues.InvalidRequest,
            _ => ComputerUseWinFailureCodeValues.ObservationFailed,
        };

        return ComputerUseWinFailureDetails.Expected(failureCode, reason);
    }

    private static ComputerUseWinStoredElement ProjectLookupElement(UiaElementSnapshot element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return new(
            Index: 0,
            ElementId: element.ElementId,
            Name: element.Name,
            AutomationId: element.AutomationId,
            ControlType: element.ControlType,
            Bounds: element.BoundingRectangle,
            HasKeyboardFocus: element.HasKeyboardFocus,
            Actions: ComputerUseWinAffordanceResolver.Resolve(element),
            Patterns: element.Patterns);
    }
}

internal sealed record ComputerUseWinSemanticTargetPolicy(
    Func<ComputerUseWinStoredElement, bool> IsActionable,
    string MissingTargetFailureCode,
    string MissingTargetReason,
    string PreviewUnsupportedReason,
    string FreshObservationFailureReason,
    string FreshStaleReason,
    string FreshUnsupportedReason,
    string SelectorZeroMatchesReason,
    string SelectorAmbiguousReason,
    string SelectorBudgetExceededReason,
    string SelectorTimeoutReason,
    string SelectorObservationFailureReason,
    string SelectorUnsupportedReason);

internal sealed record ComputerUseWinSemanticTargetResolution(
    bool IsSuccess,
    ComputerUseWinStoredElement? EffectiveElement,
    ComputerUseWinFailureDetails? FailureDetails)
{
    public static ComputerUseWinSemanticTargetResolution Success(ComputerUseWinStoredElement effectiveElement) =>
        new(true, effectiveElement, null);

    public static ComputerUseWinSemanticTargetResolution Failure(ComputerUseWinFailureDetails failure) =>
        new(false, null, failure);
}
