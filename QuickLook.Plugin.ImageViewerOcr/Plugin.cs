using System.Windows;
using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using QuickLook.Common.Plugin.MoreMenu;
using QuickLook.Plugin.ImageViewerOcr.Ocr;

namespace QuickLook.Plugin.ImageViewerOcr;

public sealed class Plugin : IViewer, IMoreMenu
{
    private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff",
    };

    private ImageViewerControl? _viewer;
    private CancellationTokenSource? _recognition;

    public int Priority => 10;

    public IEnumerable<IMenuItem> MenuItems { get; } = new IMenuItem[]
    {
        new MoreMenuItem
        {
            Header = "Настройки распознавания текста",
            Command = new QuickLook.Common.Commands.RelayCommand(OpenSettings),
        },
    };

    public void Init()
    {
        Task.Run(() => PaddleOcrEngine.Shared.Warmup());
    }

    public bool CanHandle(string path)
    {
        if (string.IsNullOrEmpty(path) || Directory.Exists(path))
            return false;

        return Extensions.Contains(Path.GetExtension(path));
    }

    public void Prepare(string path, ContextObject context)
    {
        Size size = ImageProbe.TryGetSize(path);
        if (size.Width > 0 && size.Height > 0)
            context.SetPreferredSizeFit(size, 0.8);
        else
            context.PreferredSize = new Size(800, 600);

        context.Theme = (Themes)SettingHelper.Get("LastTheme", 1, "QuickLook.Plugin.ImageViewer");
        context.FullWindowDragging = false;
        context.CanResize = true;
    }

    public void View(string path, ContextObject context)
    {
        CancelRecognition();
        _viewer?.Release();
        var viewer = new ImageViewerControl();
        _viewer = viewer;
        viewer.AttachHostKeys(context.Source);
        context.ViewerContent = viewer;
        Size size = ImageProbe.TryGetSize(path);
        context.Title = size.Width > 0
            ? (int)size.Width + "×" + (int)size.Height + ": " + Path.GetFileName(path)
            : Path.GetFileName(path);
        context.IsBusy = false;
        viewer.ShowFile(path);

        PluginSettings settings = PluginSettings.Load();
        if (!settings.IsEnabled)
            return;

        _recognition = new CancellationTokenSource();
        CancellationToken token = _recognition.Token;
        _ = viewer.RecognizeAsync(path, settings, token);
    }

    public void Cleanup()
    {
        CancelRecognition();
        _viewer?.Release();
        _viewer = null;
    }

    private void CancelRecognition()
    {
        if (_recognition == null)
            return;

        _recognition.Cancel();
        _recognition.Dispose();
        _recognition = null;
    }

    private static void OpenSettings()
    {
        PluginSettings settings = PluginSettings.Load();
        var window = new SettingsWindow(settings);
        window.ShowDialog();
    }
}
