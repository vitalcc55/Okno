namespace WinBridge.Runtime.Contracts;

public sealed record WindowSelector(long? Hwnd, string? TitlePattern, string? ProcessName)
{
    public void Validate()
    {
        if (Hwnd is null
            && string.IsNullOrWhiteSpace(TitlePattern)
            && string.IsNullOrWhiteSpace(ProcessName))
        {
            throw new InvalidOperationException(
                "Нужно указать хотя бы один селектор: hwnd, titlePattern или processName.");
        }
    }

    public string MatchStrategy =>
        Hwnd is not null
            ? "hwnd"
            : !string.IsNullOrWhiteSpace(ProcessName)
                ? "process_name"
                : "title_pattern";
}
