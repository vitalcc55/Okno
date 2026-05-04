// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class UiAutomationWaitMatchAccumulatorTests
{
    [Fact]
    public void TextAppearsKeepsOnlyTextQualifiedMatches()
    {
        UiAutomationWaitMatchAccumulator accumulator = new(WaitConditionValues.TextAppears, "Ready");

        accumulator.AddSelectorHit(
            CreateElement("rid:1", "Status"),
            valueText: "Idle",
            textPatternText: null);
        accumulator.AddSelectorHit(
            CreateElement("rid:2", "Status"),
            valueText: "Ready to submit",
            textPatternText: null);

        UiAutomationWaitProbeResult result = accumulator.Build(CreateObservedWindow());

        UiaElementSnapshot match = Assert.Single(result.Matches);
        Assert.Equal("rid:2", match.ElementId);
        Assert.Equal("Ready to submit", result.MatchedText);
        Assert.Equal("value_pattern", result.MatchedTextSource);
    }

    [Fact]
    public void TextAppearsMarksMultipleTextQualifiedMatchesAsAmbiguousCandidates()
    {
        UiAutomationWaitMatchAccumulator accumulator = new(WaitConditionValues.TextAppears, "Ready");

        accumulator.AddSelectorHit(
            CreateElement("rid:1", "Status"),
            valueText: "Ready to submit",
            textPatternText: null);
        accumulator.AddSelectorHit(
            CreateElement("rid:2", "Status"),
            valueText: null,
            textPatternText: "Ready again");

        UiAutomationWaitProbeResult result = accumulator.Build(CreateObservedWindow());

        Assert.Equal(2, result.Matches.Count);
        Assert.Null(result.MatchedText);
        Assert.Null(result.MatchedTextSource);
    }

    private static UiaElementSnapshot CreateElement(string elementId, string name) =>
        new()
        {
            ElementId = elementId,
            Name = name,
            AutomationId = "StatusLabel",
            ControlType = "text",
            ControlTypeId = 50020,
            IsControlElement = true,
            IsContentElement = true,
            IsEnabled = true,
            Children = [],
        };

    private static ObservedWindowDescriptor CreateObservedWindow() =>
        new(
            Hwnd: 42,
            Title: "Window",
            ProcessName: "okno-tests",
            ProcessId: 1,
            ThreadId: 2,
            ClassName: "Window");
}
