using System.Windows;

namespace QuickLook.Plugin.ImageViewerOcr;

public partial class SettingsWindow : Window
{
    private readonly PluginSettings _settings;

    public SettingsWindow(PluginSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        EngineBox.ItemsSource = new[]
        {
            new EngineChoice("Auto", "Авто"),
            new EngineChoice("Paddle", "PaddleOCR"),
            new EngineChoice("WindowsAi", "Windows AI"),
            new EngineChoice("WindowsOcr", "Windows OCR"),
        };
        EngineBox.DisplayMemberPath = nameof(EngineChoice.Label);
        EngineBox.SelectedValuePath = nameof(EngineChoice.Id);
        EnabledBox.IsChecked = settings.IsEnabled;
        RussianBox.IsChecked = settings.UseRussian;
        EnglishBox.IsChecked = settings.UseEnglish;
        EngineBox.SelectedValue = settings.Engine;
        if (EngineBox.SelectedValue == null)
            EngineBox.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsEnabled = EnabledBox.IsChecked == true;
        _settings.UseRussian = RussianBox.IsChecked == true;
        _settings.UseEnglish = EnglishBox.IsChecked == true;
        _settings.Engine = EngineBox.SelectedValue as string ?? "Auto";
        _settings.Save();
        DialogResult = true;
    }

    private sealed class EngineChoice
    {
        public EngineChoice(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }

        public string Label { get; }
    }
}
