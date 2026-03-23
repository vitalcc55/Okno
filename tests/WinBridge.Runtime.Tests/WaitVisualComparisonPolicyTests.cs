using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;

namespace WinBridge.Runtime.Tests;

public sealed class WaitVisualComparisonPolicyTests
{
    [Fact]
    public void CreateLumaGridDownsamplesIntoStable16x16Grid()
    {
        WaitVisualFrame frame = CreateFrame(
            pixelWidth: 32,
            pixelHeight: 32,
            fill: (x, y) =>
            {
                byte value = (byte)(((x / 2) + (y / 2)) % 256);
                return (value, value, value);
            });

        WaitVisualGrid grid = WaitVisualComparisonPolicy.CreateLumaGrid(frame);

        Assert.Equal(WaitVisualComparisonPolicy.TotalCellCount, grid.Cells.Length);
        Assert.Equal(256, grid.PopulatedCellCount);
        Assert.Equal(0, grid.Cells[0]);
        Assert.Equal(15, grid.Cells[15]);
        Assert.Equal(1, grid.Cells[WaitVisualComparisonPolicy.GridWidth]);
    }

    [Fact]
    public void CompareSuppressesNoiseBelowChangedCellThreshold()
    {
        WaitVisualFrame baseline = CreateFrame(16, 16, (_, _) => ((byte)50, (byte)50, (byte)50));
        WaitVisualFrame current = CreateFrame(
            16,
            16,
            (x, y) => x < 15 && y == 0 ? ((byte)65, (byte)65, (byte)65) : ((byte)50, (byte)50, (byte)50));

        WaitVisualComparisonResult result = WaitVisualComparisonPolicy.Compare(
            WaitVisualComparisonPolicy.CreateLumaGrid(baseline),
            baseline.PixelWidth,
            baseline.PixelHeight,
            current);

        Assert.False(result.IsCandidate);
        Assert.Equal(15, result.ChangedCellCount);
        Assert.Equal(16, result.EffectiveThresholdCount);
        Assert.Equal(15d / WaitVisualComparisonPolicy.TotalCellCount, result.DifferenceRatio);
        Assert.Equal(WaitVisualComparisonPolicy.DifferenceRatioThreshold, result.EffectiveThresholdRatio);
    }

    [Fact]
    public void CompareReturnsCandidateWhenChangedCellThresholdIsMet()
    {
        WaitVisualFrame baseline = CreateFrame(16, 16, (_, _) => ((byte)50, (byte)50, (byte)50));
        WaitVisualFrame current = CreateFrame(
            16,
            16,
            (x, y) => x < 16 && y == 0 ? ((byte)65, (byte)65, (byte)65) : ((byte)50, (byte)50, (byte)50));

        WaitVisualComparisonResult result = WaitVisualComparisonPolicy.Compare(
            WaitVisualComparisonPolicy.CreateLumaGrid(baseline),
            baseline.PixelWidth,
            baseline.PixelHeight,
            current);

        Assert.True(result.IsCandidate);
        Assert.Equal(16, result.ChangedCellCount);
        Assert.Equal(16, result.EffectiveThresholdCount);
        Assert.Equal(WaitVisualComparisonPolicy.DifferenceRatioThreshold, result.DifferenceRatio);
        Assert.Equal(WaitVisualComparisonPolicy.DifferenceRatioThreshold, result.EffectiveThresholdRatio);
    }

    [Fact]
    public void CompareReturnsCandidateWhenPixelSizeChanges()
    {
        WaitVisualFrame baseline = CreateFrame(16, 16, (_, _) => ((byte)50, (byte)50, (byte)50));
        WaitVisualFrame current = CreateFrame(17, 16, (_, _) => ((byte)50, (byte)50, (byte)50));

        WaitVisualComparisonResult result = WaitVisualComparisonPolicy.Compare(
            WaitVisualComparisonPolicy.CreateLumaGrid(baseline),
            baseline.PixelWidth,
            baseline.PixelHeight,
            current);

        Assert.True(result.IsCandidate);
        Assert.True(result.PixelSizeChanged);
        Assert.Equal(WaitVisualComparisonPolicy.ChangedCellThreshold, result.EffectiveThresholdCount);
        Assert.Equal(1.0, result.DifferenceRatio);
        Assert.Equal(WaitVisualComparisonPolicy.DifferenceRatioThreshold, result.EffectiveThresholdRatio);
    }

    [Fact]
    public void CompareReturnsCandidateWhenAllPopulatedCellsChangeInTinyWindow()
    {
        WaitVisualFrame baseline = CreateFrame(1, 10, (_, _) => ((byte)50, (byte)50, (byte)50));
        WaitVisualFrame current = CreateFrame(1, 10, (_, _) => ((byte)65, (byte)65, (byte)65));

        WaitVisualComparisonResult result = WaitVisualComparisonPolicy.Compare(
            WaitVisualComparisonPolicy.CreateLumaGrid(baseline),
            baseline.PixelWidth,
            baseline.PixelHeight,
            current);

        Assert.True(result.IsCandidate);
        Assert.Equal(1.0, result.DifferenceRatio);
        Assert.Equal(1, result.EffectiveThresholdCount);
        Assert.Equal(0.1, result.EffectiveThresholdRatio);
    }

    private static WaitVisualFrame CreateFrame(
        int pixelWidth,
        int pixelHeight,
        Func<int, int, (byte r, byte g, byte b)> fill)
    {
        int rowStride = pixelWidth * 4;
        byte[] pixelBytes = new byte[rowStride * pixelHeight];

        for (int y = 0; y < pixelHeight; y++)
        {
            int rowOffset = y * rowStride;
            for (int x = 0; x < pixelWidth; x++)
            {
                (byte r, byte g, byte b) = fill(x, y);
                int offset = rowOffset + (x * 4);
                pixelBytes[offset] = b;
                pixelBytes[offset + 1] = g;
                pixelBytes[offset + 2] = r;
                pixelBytes[offset + 3] = 255;
            }
        }

        return new WaitVisualFrame(
            new WindowDescriptor(
                Hwnd: 101,
                Title: "Visual wait window",
                ProcessName: "okno-tests",
                ProcessId: 42,
                ThreadId: 84,
                ClassName: "OknoVisualWaitWindow",
                Bounds: new Bounds(0, 0, pixelWidth, pixelHeight),
                IsForeground: true,
                IsVisible: true),
            pixelWidth,
            pixelHeight,
            rowStride,
            pixelBytes);
    }
}
