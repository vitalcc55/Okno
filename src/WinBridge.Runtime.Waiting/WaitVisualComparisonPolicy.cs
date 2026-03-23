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

    public static WaitVisualComparisonResult Compare(
        WaitVisualComparisonData baselineGrid,
        int baselinePixelWidth,
        int baselinePixelHeight,
        WaitVisualSample currentSample)
    {
        ArgumentNullException.ThrowIfNull(baselineGrid);
        ArgumentNullException.ThrowIfNull(currentSample);

        if (baselinePixelWidth != currentSample.PixelWidth
            || baselinePixelHeight != currentSample.PixelHeight)
        {
            return new(
                IsCandidate: true,
                ChangedCellCount: TotalCellCount,
                EffectiveThresholdCount: ChangedCellThreshold,
                DifferenceRatio: 1.0,
                EffectiveThresholdRatio: DifferenceRatioThreshold,
                PixelSizeChanged: true,
                Detail: "Размер window capture изменился относительно baseline.");
        }

        WaitVisualComparisonData currentGrid = currentSample.ComparisonData;
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
            EffectiveThresholdCount: changedCellThreshold,
            DifferenceRatio: differenceRatio,
            EffectiveThresholdRatio: populatedCellCount == 0 ? DifferenceRatioThreshold : (double)changedCellThreshold / populatedCellCount,
            PixelSizeChanged: false,
            Detail: detail);
    }
}

internal sealed record WaitVisualComparisonResult(
    bool IsCandidate,
    int ChangedCellCount,
    int EffectiveThresholdCount,
    double DifferenceRatio,
    double EffectiveThresholdRatio,
    bool PixelSizeChanged,
    string Detail);
