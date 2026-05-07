// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Xml.Linq;

namespace WinBridge.Server.IntegrationTests;

public sealed partial class ComputerUseWinInstallSurfaceTests
{
    [Fact]
    public void SetupAppLocalizationResourcesStayInKeyParityAcrossSupportedLanguages()
    {
        string stringsRoot = Path.Combine(GetRepositoryRoot(), "src", "WinBridge.Setup.App", "Strings");
        HashSet<string> englishKeys = LoadReswValues(Path.Combine(stringsRoot, "en-US", "Resources.resw")).Keys.ToHashSet();

        foreach (string supportedLocalizedCulture in new[] { "ru-RU", "zh-CN" })
        {
            Dictionary<string, string> localized = LoadReswValues(Path.Combine(stringsRoot, supportedLocalizedCulture, "Resources.resw"));

            if (!englishKeys.SetEquals(localized.Keys))
            {
                Assert.Equal(englishKeys.Order(), localized.Keys.Order());
            }

            Assert.All(localized, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{supportedLocalizedCulture} value for '{entry.Key}' is empty."));
        }
    }

    private static Dictionary<string, string> LoadReswValues(string path) =>
        XDocument.Load(path).Root?.Elements("data").ToDictionary(
            static element => element.Attribute("name")?.Value
                ?? throw new InvalidOperationException("Resource key is missing."),
            static element => element.Element("value")?.Value ?? string.Empty)
        ?? throw new InvalidOperationException($"RESW file '{path}' does not contain a root element.");
}