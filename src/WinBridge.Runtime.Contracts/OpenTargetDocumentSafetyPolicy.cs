namespace WinBridge.Runtime.Contracts;

internal static class OpenTargetDocumentSafetyPolicy
{
    // Текущая document admission policy остаётся fail-closed против launcher/script targets
    // that ShellExecute can hand off to an interpreter or launcher instead of a passive document handler.
    private static readonly HashSet<string> BlockedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // ClickOnce / shell bootstrap families
        ".application",
        ".appref-ms",
        ".lnk",
        ".url",

        // Native executable / installer / control entrypoints
        ".bat",
        ".cmd",
        ".com",
        ".cpl",
        ".exe",
        ".hta",
        ".js",
        ".jse",
        ".msc",
        ".msi",
        ".ps1",
        ".psd1",
        ".psm1",

        // Python-associated executable families on Windows, including zipapp packaging.
        ".py",
        ".pyc",
        ".pyw",
        ".pyz",
        ".pyzw",

        // Java executable archives on Windows can be shell-associated to javaw -jar.
        ".jar",

        ".reg",
        ".scr",
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
