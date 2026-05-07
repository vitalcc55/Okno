using Microsoft.Windows.ApplicationModel.Resources;

namespace WinBridge.Setup.App.Services;

public sealed class SetupLocalizationService
{
    private readonly ResourceManager resourceManager = new();
    private readonly ResourceMap resourceMap;
    private string currentLanguageTag = "en-US";

    public SetupLocalizationService()
    {
        try
        {
            resourceMap = resourceManager.MainResourceMap.GetSubtree("Resources");
        }
        catch
        {
            resourceMap = resourceManager.MainResourceMap;
        }
    }

    public static IReadOnlyList<SetupLanguageOption> SupportedLanguages { get; } =
    [
        new("en-US", "English"),
        new("ru-RU", "Русский"),
        new("zh-CN", "简体中文"),
    ];

    public string CurrentLanguageTag => currentLanguageTag;

    public void SetLanguage(string languageTag)
    {
        if (SupportedLanguages.All(option => !string.Equals(option.Tag, languageTag, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Unsupported setup language '{languageTag}'.");
        }

        currentLanguageTag = languageTag;
    }

    public string GetString(string resourceKey)
    {
        string? localized = TryGetString(resourceKey, currentLanguageTag);
        if (!string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        string? englishFallback = TryGetString(resourceKey, "en-US");
        if (!string.IsNullOrWhiteSpace(englishFallback))
        {
            return englishFallback;
        }

        throw new InvalidOperationException($"Localized setup resource '{resourceKey}' is missing.");
    }

    private string? TryGetString(string resourceKey, string languageTag)
    {
        ResourceContext context = resourceManager.CreateResourceContext();
        context.QualifierValues["Language"] = languageTag;
        return resourceMap.GetValue(resourceKey, context).ValueAsString;
    }
}

public sealed record SetupLanguageOption(string Tag, string DisplayName);
