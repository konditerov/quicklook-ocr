using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using QuickLook.Plugin.ImageViewerOcr.Ocr;

namespace QuickLook.Plugin.ImageViewerOcr;

public partial class ImageViewerControl : UserControl
{
    private readonly List<(OcrWord Word, Polygon Shape)> _shapes = new List<(OcrWord, Polygon)>();
    private readonly List<Rectangle> _bands = new List<Rectangle>();
    private OcrPage? _page;
    private bool _hasCaret;
    private TextCaret _anchor;
    private TextCaret _focus;
    private string _path = "";
    private int _imageWidth;
    private int _imageHeight;
    private bool _userAdjusted;
    private bool _selecting;
    private bool _panning;
    private Point _lastScreen;
    private Window? _host;
    private KeyEventHandler? _hostKeys;

    public ImageViewerControl()
    {
        InitializeComponent();
    }

    public void ShowFile(string path)
    {
        _path = path;
        _page = null;
        ClearPage();
        try
        {
            Size size = ImageProbe.TryGetSize(path);
            if (size.Width <= 0 || size.Height <= 0)
                throw new InvalidOperationException("Image size is empty.");

            _imageWidth = (int)size.Width;
            _imageHeight = (int)size.Height;
            Photo.Source = ImageProbe.LoadDisplay(path, _imageWidth, _imageHeight);
            Photo.Width = _imageWidth;
            Photo.Height = _imageHeight;
            Words.Width = _imageWidth;
            Words.Height = _imageHeight;
            ErrorText.Visibility = Visibility.Collapsed;
            _userAdjusted = false;
            Fit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            Photo.Source = null;
            ErrorText.Text = "Не удалось открыть изображение.";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    public async Task RecognizeAsync(string path, PluginSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            OcrPage? page = await OcrRunner.RunAsync(path, settings, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested || page == null || path != _path)
                return;

            ShowPage(page);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    public void AttachHostKeys(object? source)
    {
        DetachHostKeys();
        if (source is not Window window)
            return;

        _host = window;
        _hostKeys = OnPreviewKeyDown;
        window.PreviewKeyDown += _hostKeys;
    }

    public void Release()
    {
        DetachHostKeys();
        Photo.Source = null;
        ClearPage();
        _path = "";
    }

    private void ShowPage(OcrPage page)
    {
        _page = page;
        ClearPage();
        foreach (OcrLine line in page.Lines)
        {
            foreach (OcrWord word in line.Words)
            {
                var shape = new Polygon
                {
                    Points = new PointCollection(word.Quad.Select(point => new Point(point.X, point.Y))),
                    Fill = IdleBrush(),
                    StrokeThickness = 0,
                    IsHitTestVisible = false,
                };
                Words.Children.Add(shape);
                _shapes.Add((word, shape));
            }
        }
    }

    private void ClearPage()
    {
        Words.Children.Clear();
        _shapes.Clear();
        _bands.Clear();
        _hasCaret = false;
        Band.Visibility = Visibility.Collapsed;
    }

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_userAdjusted)
            Fit();
    }

    private void Fit()
    {
        if (_imageWidth <= 0 || _imageHeight <= 0 || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
            return;

        double scale = Math.Min(Viewport.ActualWidth / _imageWidth, Viewport.ActualHeight / _imageHeight);
        scale = Math.Max(0.1, Math.Min(16, scale));
        Scale.ScaleX = scale;
        Scale.ScaleY = scale;
        Translate.X = (Viewport.ActualWidth - _imageWidth * scale) / 2d;
        Translate.Y = (Viewport.ActualHeight - _imageHeight * scale) / 2d;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_imageWidth <= 0)
            return;

        double factor = e.Delta > 0 ? 1.1 : 1d / 1.1;
        double next = Math.Max(0.1, Math.Min(16, Scale.ScaleX * factor));
        Point mouse = e.GetPosition(Viewport);
        OcrPoint world = ToImage(mouse);
        Scale.ScaleX = next;
        Scale.ScaleY = next;
        Translate.X = mouse.X - world.X * next;
        Translate.Y = mouse.Y - world.Y * next;
        _userAdjusted = true;
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_imageWidth <= 0)
            return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            OnMiddleDown(sender, e);
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        Focus();
        Point screen = e.GetPosition(Viewport);
        OcrPoint image = ToImage(screen);
        if (_page == null)
            return;

        if (e.ClickCount == 2)
        {
            SelectWord(image);
            _selecting = false;
            _panning = false;
            Band.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        if (!TextSelection.TryHit(_page, image, out TextCaret caret))
        {
            ClearSelection();
            return;
        }

        _anchor = caret;
        _focus = caret;
        _hasCaret = true;
        _selecting = true;
        _panning = false;
        PaintSelection();
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMiddleDown(object sender, MouseButtonEventArgs e)
    {
        if (_imageWidth <= 0)
            return;

        _selecting = false;
        _panning = true;
        _lastScreen = e.GetPosition(Viewport);
        Band.Visibility = Visibility.Collapsed;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point screen = e.GetPosition(Viewport);
        if (!IsMouseCaptured)
        {
            UpdateCursor(screen);
            return;
        }

        if (_panning)
        {
            Translate.X += screen.X - _lastScreen.X;
            Translate.Y += screen.Y - _lastScreen.Y;
            _lastScreen = screen;
            _userAdjusted = true;
            return;
        }

        if (!_selecting)
            return;

        if (_page != null && TextSelection.TryHit(_page, ToImage(screen), out TextCaret caret))
        {
            _focus = caret;
            PaintSelection();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            OnMiddleUp(sender, e);
            return;
        }

        if (e.ChangedButton != MouseButton.Left || !IsMouseCaptured)
            return;

        ReleaseMouseCapture();
        _panning = false;
        _selecting = false;
        Band.Visibility = Visibility.Collapsed;
        UpdateCursor(e.GetPosition(Viewport));
    }

    private void OnMiddleUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning)
            return;

        ReleaseMouseCapture();
        _panning = false;
        e.Handled = true;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured)
            Cursor = Cursors.Arrow;
    }

    private void UpdateCursor(Point screen)
    {
        OcrPoint image = ToImage(screen);
        Cursor = _page != null && TextSelection.TryHit(_page, image, out _) ? Cursors.IBeam : Cursors.Arrow;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control)
            return;

        Copy(HasSelection());
        e.Handled = true;
    }

    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        CopyItem.IsEnabled = HasSelection();
    }

    private void CopySelection_Click(object sender, RoutedEventArgs e)
    {
        Copy(selectedOnly: true);
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        Copy(selectedOnly: false);
    }

    private void Copy(bool selectedOnly)
    {
        if (_page == null)
            return;

        string text = selectedOnly && _hasCaret
            ? TextSelection.Copy(_page, _anchor, _focus)
            : TextLayout.JoinAll(_page);
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void SelectWord(OcrPoint point)
    {
        if (_page == null || !TextSelection.TryHit(_page, point, out TextCaret caret))
        {
            ClearSelection();
            return;
        }

        _anchor = TextSelection.WordStart(caret);
        _focus = TextSelection.WordEnd(_page, caret);
        _hasCaret = true;
        PaintSelection();
    }

    private void ClearSelection()
    {
        _hasCaret = false;
        PaintSelection();
    }

    private void PaintSelection()
    {
        foreach (Rectangle band in _bands)
            Words.Children.Remove(band);
        _bands.Clear();
        foreach ((OcrWord _, Polygon shape) in _shapes)
            shape.Fill = IdleBrush();

        if (!_hasCaret || _page == null)
            return;

        foreach (SelectionBand band in TextSelection.Bands(_page, _anchor, _focus))
        {
            var rect = new Rectangle
            {
                Width = Math.Max(1, band.Width),
                Height = Math.Max(1, band.Height),
                Fill = SelectedBrush(),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, band.X);
            Canvas.SetTop(rect, band.Y);
            Words.Children.Add(rect);
            _bands.Add(rect);
        }
    }

    private bool HasSelection()
    {
        return _hasCaret;
    }

    private OcrPoint ToImage(Point screen)
    {
        double scale = Scale.ScaleX == 0 ? 1 : Scale.ScaleX;
        return new OcrPoint((screen.X - Translate.X) / scale, (screen.Y - Translate.Y) / scale);
    }

    private Point ToScreen(OcrPoint image)
    {
        return new Point(image.X * Scale.ScaleX + Translate.X, image.Y * Scale.ScaleY + Translate.Y);
    }

    private void DetachHostKeys()
    {
        if (_host != null && _hostKeys != null)
            _host.PreviewKeyDown -= _hostKeys;
        _host = null;
        _hostKeys = null;
    }

    private static SolidColorBrush IdleBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush SelectedBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(96, 80, 160, 255));
        brush.Freeze();
        return brush;
    }
}
