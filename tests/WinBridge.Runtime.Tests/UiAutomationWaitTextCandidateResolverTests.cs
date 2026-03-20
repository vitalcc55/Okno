using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class UiAutomationWaitTextCandidateResolverTests
{
    [Fact]
    public void EnumerateDistinctCandidatesPreservesValueTextAndNameOrder()
    {
        IReadOnlyList<WaitTextCandidateMatch> candidates = UiAutomationWaitTextCandidateResolver.EnumerateDistinctCandidates(
            valueText: "value",
            textPatternText: "text",
            name: "name");

        Assert.Collection(
            candidates,
            first =>
            {
                Assert.Equal("value_pattern", first.Source);
                Assert.Equal("value", first.Text);
            },
            second =>
            {
                Assert.Equal("text_pattern", second.Source);
                Assert.Equal("text", second.Text);
            },
            third =>
            {
                Assert.Equal("name", third.Source);
                Assert.Equal("name", third.Text);
            });
    }

    [Fact]
    public void MatchReturnsFirstOrdinalSubstringCandidate()
    {
        WaitTextCandidateMatch? match = UiAutomationWaitTextCandidateResolver.Match(
            expectedText: "Ready",
            valueText: "Ready to submit",
            textPatternText: "Ready",
            name: "Submit");

        Assert.NotNull(match);
        Assert.Equal("value_pattern", match!.Source);
        Assert.Equal("Ready to submit", match.Text);
    }
}
