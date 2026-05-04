// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage.Streams;

namespace WinBridge.Runtime.Tests;

public sealed class WaitVisualComparisonPolicyTests
{
    [Fact]
    public void MemoryBufferByteAccessUsesOfficialWinRtGuid()
    {
        Assert.Equal(
            new Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"),
            typeof(IMemoryBufferByteAccess).GUID);
    }

    [Fact]
    public void CreateFromSoftwareBitmapBuildsComparisonGridFromDirectPixelBuffer()
    {
        byte[] pixelBytes = CreatePixelBytes(
            pixelWidth: 16,
            pixelHeight: 16,
            fill: (x, y) =>
            {
                byte value = (byte)((x + y) % 256);
                return (value, value, value);
            });
        using SoftwareBitmap softwareBitmap = CreateSoftwareBitmap(16, 16, pixelBytes);

        WaitVisualComparisonData comparisonData = WaitVisualComparisonDataBuilder.CreateFromSoftwareBitmap(
            softwareBitmap,
            CancellationToken.None);
        WaitVisualComparisonData expected = WaitVisualComparisonDataBuilder.CreateFromBgra32Pixels(
            16,
            16,
            16 * 4,
            pixelBytes);

        Assert.Equal(expected.PopulatedCellCount, comparisonData.PopulatedCellCount);
        Assert.Equal(expected.Cells, comparisonData.Cells);
    }

    [Fact]
    public void CreateComparisonDataDownsamplesIntoStable16x16Grid()
    {
        byte[] pixelBytes = CreatePixelBytes(
            pixelWidth: 32,
            pixelHeight: 32,
            fill: (x, y) =>
            {
                byte value = (byte)(((x / 2) + (y / 2)) % 256);
                return (value, value, value);
            });

        WaitVisualComparisonData grid = WaitVisualComparisonDataBuilder.CreateFromBgra32Pixels(
            32,
            32,
            32 * 4,
            pixelBytes);

        Assert.Equal(WaitVisualComparisonPolicy.TotalCellCount, grid.Cells.Length);
        Assert.Equal(256, grid.PopulatedCellCount);
        Assert.Equal(0, grid.Cells[0]);
        Assert.Equal(15, grid.Cells[15]);
        Assert.Equal(1, grid.Cells[WaitVisualComparisonPolicy.GridWidth]);
    }

    [Fact]
    public void CompareSuppressesNoiseBelowChangedCellThreshold()
    {
        WaitVisualSample baseline = CreateFrame(16, 16, (_, _) => ((byte)50, (byte)50, (byte)50));
        WaitVisualSample current = CreateFrame(
            16,
            16,
            (x, y) => x < 15 && y == 0 ? ((byte)65, (byte)65, (byte)65) : ((byte)50, (byte)50, (byte)50));

        WaitVisualComparisonResult result = WaitVisualComparisonPolicy.Compare(
            baseline.ComparisonData,
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
        WaitVisualSample baseline = CreateFrame(16, 16, (_, _) => ((byte)50, (byte)50, (byte)50));
        WaitVisualSample current = CreateFrame(
            16,
            16,
            (x, y) => x < 16 && y == 0 ? ((byte)65, (byte)65, (byte)65) : ((byte)50, (byte)50, (byte)50));

        WaitVisualComparisonResult result = WaitVisualComparisonPolicy.Compare(
            baseline.ComparisonData,
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
        WaitVisualSample baseline = CreateFrame(16, 16, (_, _) => ((byte)50, (byte)50, (byte)50));
        WaitVisualSample current = CreateFrame(17, 16, (_, _) => ((byte)50, (byte)50, (byte)50));

        WaitVisualComparisonResult result = WaitVisualComparisonPolicy.Compare(
            baseline.ComparisonData,
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
        WaitVisualSample baseline = CreateFrame(1, 10, (_, _) => ((byte)50, (byte)50, (byte)50));
        WaitVisualSample current = CreateFrame(1, 10, (_, _) => ((byte)65, (byte)65, (byte)65));

        WaitVisualComparisonResult result = WaitVisualComparisonPolicy.Compare(
            baseline.ComparisonData,
            baseline.PixelWidth,
            baseline.PixelHeight,
            current);

        Assert.True(result.IsCandidate);
        Assert.Equal(1.0, result.DifferenceRatio);
        Assert.Equal(1, result.EffectiveThresholdCount);
        Assert.Equal(0.1, result.EffectiveThresholdRatio);
    }

    private static WaitVisualSample CreateFrame(
        int pixelWidth,
        int pixelHeight,
        Func<int, int, (byte r, byte g, byte b)> fill)
    {
        byte[] pixelBytes = CreatePixelBytes(pixelWidth, pixelHeight, fill);
        WaitVisualComparisonData comparisonData = WaitVisualComparisonDataBuilder.CreateFromBgra32Pixels(
            pixelWidth,
            pixelHeight,
            pixelWidth * 4,
            pixelBytes);

        return new WaitVisualSample(
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
            comparisonData,
            EvidenceFrame: null);
    }

    private static byte[] CreatePixelBytes(
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

        return pixelBytes;
    }

    private static SoftwareBitmap CreateSoftwareBitmap(int pixelWidth, int pixelHeight, byte[] pixelBytes)
    {
        IBuffer buffer = new global::Windows.Storage.Streams.Buffer((uint)pixelBytes.Length);
        using DataWriter writer = new();
        writer.WriteBytes(pixelBytes);
        buffer = writer.DetachBuffer();

        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            pixelWidth,
            pixelHeight,
            BitmapAlphaMode.Premultiplied);
    }
}
