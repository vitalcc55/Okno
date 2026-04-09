namespace WinBridge.Runtime.Contracts;

internal static class OpenTargetDocumentSafetyPolicy
{
    // V1 document admission stays fail-closed against known launcher/script targets
    // that ShellExecute can hand off to an interpreter or launcher instead of a passive document handler.
    private static readonly HashSet<string> BlockedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".application",
        ".appref-ms",
        ".bat",
        ".cmd",
        ".com",
        ".cpl",
        ".exe",
        ".hta",
        ".js",
        ".jse",
        ".lnk",
        ".msc",
        ".msi",
        ".ps1",
        ".psd1",
        ".psm1",
        ".py",
        ".pyc",
        ".pyw",
        ".reg",
        ".scr",
        ".url",
        ".vb",
        ".vbe",
        ".vbs",
        ".ws",
        ".wsf",
        ".wsh",
    };

    internal static bool IsBlockedDocumentTarget(string target)
    {
        string extension = Path.GetExtension(target);
        return !string.IsNullOrWhiteSpace(extension) && BlockedDocumentExtensions.Contains(extension);
    }
}
