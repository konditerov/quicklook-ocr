using QuickLook.Common.Helpers;

namespace QuickLook.Plugin.ImageViewerOcr;

public sealed class PluginSettings
{
    public const string Domain = "QuickLook.Plugin.ImageViewerOcr";

    public bool IsEnabled { get; set; } = true;

    public string Engine { get; set; } = "Auto";

    public bool UseRussian { get; set; } = true;

    public bool UseEnglish { get; set; } = true;

    public static PluginSettings Load()
    {
        return new PluginSettings
        {
            IsEnabled = SettingHelper.Get("OcrEnabled", true, Domain),
            Engine = SettingHelper.Get("OcrEngine", "Auto", Domain) ?? "Auto",
            UseRussian = SettingHelper.Get("OcrLanguageRu", true, Domain),
            UseEnglish = SettingHelper.Get("OcrLanguageEn", true, Domain),
        };
    }

    public void Save()
    {
        SettingHelper.Set("OcrEnabled", IsEnabled, Domain);
        SettingHelper.Set("OcrEngine", Engine, Domain);
        SettingHelper.Set("OcrLanguageRu", UseRussian, Domain);
        SettingHelper.Set("OcrLanguageEn", UseEnglish, Domain);
    }
}
