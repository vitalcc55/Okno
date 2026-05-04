// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class UiAutomationWaitMatchAccumulator(string condition, string? expectedText)
{
    private readonly List<UiaElementSnapshot> _matches = [];
    private string? _matchedText;
    private string? _matchedTextSource;

    public bool ShouldContinueTraversal => _matches.Count < 2;

    public void AddSelectorHit(
        UiaElementSnapshot snapshot,
        string? valueText,
        string? textPatternText)
    {
        if (string.Equals(condition, WaitConditionValues.TextAppears, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(expectedText))
            {
                return;
            }

            WaitTextCandidateMatch? textMatch = UiAutomationWaitTextCandidateResolver.Match(
                expectedText,
                valueText,
                textPatternText,
                snapshot.Name);
            if (textMatch is null)
            {
                return;
            }

            _matches.Add(snapshot);
            if (_matches.Count == 1)
            {
                _matchedText = textMatch.Text;
                _matchedTextSource = textMatch.Source;
            }
            else
            {
                _matchedText = null;
                _matchedTextSource = null;
            }

            return;
        }

        _matches.Add(snapshot);
    }

    public UiAutomationWaitProbeResult Build(ObservedWindowDescriptor observedWindow) =>
        new()
        {
            Window = observedWindow,
            Matches = _matches.ToArray(),
            MatchedText = _matches.Count == 1 ? _matchedText : null,
            MatchedTextSource = _matches.Count == 1 ? _matchedTextSource : null,
        };
}
