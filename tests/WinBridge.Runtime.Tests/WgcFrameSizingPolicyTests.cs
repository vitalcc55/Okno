using WinBridge.Runtime.Windows.Capture;

namespace WinBridge.Runtime.Tests;

public sealed class WgcFrameSizingPolicyTests
{
    [Fact]
    public void EvaluateReturnsAcceptWhenContentSizeMatchesExpectedSize()
    {
        WgcFrameSizingDecision decision = WgcFrameSizingPolicy.Evaluate(
            new WgcFrameSize(800, 600),
            new WgcFrameSize(800, 600),
            recreateAttempted: false);

        Assert.Equal(WgcFrameSizingDecision.Accept, decision);
    }

    [Fact]
    public void EvaluateReturnsRecreateAndRetryOnFirstMismatch()
    {
        WgcFrameSizingDecision decision = WgcFrameSizingPolicy.Evaluate(
            new WgcFrameSize(800, 600),
            new WgcFrameSize(1024, 768),
            recreateAttempted: false);

        Assert.Equal(WgcFrameSizingDecision.RecreateAndRetry, decision);
    }

    [Fact]
    public void EvaluateReturnsFailWhenMismatchPersistsAfterRecreate()
    {
        WgcFrameSizingDecision decision = WgcFrameSizingPolicy.Evaluate(
            new WgcFrameSize(1024, 768),
            new WgcFrameSize(1280, 720),
            recreateAttempted: true);

        Assert.Equal(WgcFrameSizingDecision.Fail, decision);
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, 0)]
    [InlineData(-1, 600)]
    [InlineData(800, -1)]
    public void EvaluateReturnsFailForInvalidContentSize(int width, int height)
    {
        WgcFrameSizingDecision decision = WgcFrameSizingPolicy.Evaluate(
            new WgcFrameSize(800, 600),
            new WgcFrameSize(width, height),
            recreateAttempted: false);

        Assert.Equal(WgcFrameSizingDecision.Fail, decision);
    }
}
