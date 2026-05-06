// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Xml.Linq;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public void SetupAppLocalizationResourcesStayInKeyParityAcrossSupportedLanguages()
    {
        string repoRoot = GetRepositoryRoot();
        string stringsRoot = Path.Combine(repoRoot, "src", "WinBridge.Setup.App", "Strings");

        Dictionary<string, string> english = LoadReswValues(Path.Combine(stringsRoot, "en-US", "Resources.resw"));
        Dictionary<string, string> russian = LoadReswValues(Path.Combine(stringsRoot, "ru-RU", "Resources.resw"));
        Dictionary<string, string> chinese = LoadReswValues(Path.Combine(stringsRoot, "zh-CN", "Resources.resw"));

        Assert.Equal(english.Keys.OrderBy(static key => key), russian.Keys.OrderBy(static key => key));
        Assert.Equal(english.Keys.OrderBy(static key => key), chinese.Keys.OrderBy(static key => key));
        Assert.All(russian, static entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"Russian value for '{entry.Key}' is empty."));
        Assert.All(chinese, static entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"Chinese value for '{entry.Key}' is empty."));
    }

    private static Dictionary<string, string> LoadReswValues(string path)
    {
        XDocument document = XDocument.Load(path);
        return document.Root?
                   .Elements("data")
                   .ToDictionary(
                       static element => element.Attribute("name")?.Value
                           ?? throw new InvalidOperationException("Resource key is missing."),
                       static element => element.Element("value")?.Value ?? string.Empty)
               ?? throw new InvalidOperationException($"RESW file '{path}' does not contain a root element.");
    }
}
