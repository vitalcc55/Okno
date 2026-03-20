using WinBridge.Runtime.Windows.Capture;

namespace WinBridge.Runtime.Waiting;

internal static class WaitVisualComparisonPolicy
{
    public const int GridWidth = 16;
    public const int GridHeight = 16;
    public const int TotalCellCount = GridWidth * GridHeight;
    public const int CellDeltaThreshold = 12;
    public const int ChangedCellThreshold = 16;
    public const double DifferenceRatioThreshold = (double)ChangedCellThreshold / TotalCellCount;

    public static WaitVisualGrid CreateLumaGrid(WaitVisualFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        long[] sums = new long[TotalCellCount];
        int[] counts = new int[TotalCellCount];

        for (int y = 0; y < frame.PixelHeight; y++)
        {
            int rowOffset = y * frame.RowStride;
            int cellY = y * GridHeight / frame.PixelHeight;
            for (int x = 0; x < frame.PixelWidth; x++)
            {
                int pixelOffset = rowOffset + (x * 4);
                byte blue = frame.PixelBytes[pixelOffset];
                byte green = frame.PixelBytes[pixelOffset + 1];
                byte red = frame.PixelBytes[pixelOffset + 2];
                int luma = ((red * 77) + (green * 150) + (blue * 29) + 128) >> 8;
                int cellX = x * GridWidth / frame.PixelWidth;
                int cellIndex = (cellY * GridWidth) + cellX;
                sums[cellIndex] += luma;
                counts[cellIndex]++;
            }
        }

        byte[] grid = new byte[TotalCellCount];
        int populatedCellCount = 0;
        for (int index = 0; index < TotalCellCount; index++)
        {
            if (counts[index] == 0)
            {
                grid[index] = 0;
                continue;
            }

            populatedCellCount++;
            grid[index] = (byte)(sums[index] / counts[index]);
        }

        return new WaitVisualGrid(grid, populatedCellCount);
    }

    public static WaitVisualComparisonResult Compare(
        WaitVisualGrid baselineGrid,
        int baselinePixelWidth,
        int baselinePixelHeight,
        WaitVisualFrame currentFrame)
    {
        ArgumentNullException.ThrowIfNull(baselineGrid);
        ArgumentNullException.ThrowIfNull(currentFrame);

        if (baselinePixelWidth != currentFrame.PixelWidth
            || baselinePixelHeight != currentFrame.PixelHeight)
        {
            return new(
                IsCandidate: true,
                ChangedCellCount: TotalCellCount,
                DifferenceRatio: 1.0,
                PixelSizeChanged: true,
                Detail: "Размер window capture изменился относительно baseline.");
        }

        WaitVisualGrid currentGrid = CreateLumaGrid(currentFrame);
        int populatedCellCount = Math.Max(baselineGrid.PopulatedCellCount, currentGrid.PopulatedCellCount);
        int changedCellThreshold = Math.Max(
            1,
            (int)Math.Ceiling(populatedCellCount * DifferenceRatioThreshold));
        int changedCellCount = 0;
        for (int index = 0; index < TotalCellCount; index++)
        {
            if (Math.Abs(currentGrid.Cells[index] - baselineGrid.Cells[index]) >= CellDeltaThreshold)
            {
                changedCellCount++;
            }
        }

        double differenceRatio = populatedCellCount == 0 ? 0.0 : (double)changedCellCount / populatedCellCount;
        bool isCandidate = changedCellCount >= changedCellThreshold;
        string detail = isCandidate
            ? $"Визуальное изменение превысило порог {changedCellThreshold}/{populatedCellCount} populated ячеек."
            : "Визуальное изменение осталось ниже порога noise suppression.";
        return new(
            IsCandidate: isCandidate,
            ChangedCellCount: changedCellCount,
            DifferenceRatio: differenceRatio,
            PixelSizeChanged: false,
            Detail: detail);
    }
}

internal sealed record WaitVisualComparisonResult(
    bool IsCandidate,
    int ChangedCellCount,
    double DifferenceRatio,
    bool PixelSizeChanged,
    string Detail);

internal sealed record WaitVisualGrid(
    byte[] Cells,
    int PopulatedCellCount);
