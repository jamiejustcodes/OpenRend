using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.Core;
using OpenRend.Media;
using PixelPoint = System.Drawing.Point;
using PixelRect = System.Drawing.Rectangle;

namespace OpenRend.App;

internal sealed class WhiteBalanceSampleDialog : TimelineEditDialog
{
    private readonly ProjectDocument project;
    private readonly VideoEffect grade;
    private readonly MediaService media;
    private readonly long frame;
    private readonly int width;
    private readonly int height;
    private readonly Grid viewport;
    private readonly Image image = new() { Stretch = Stretch.Fill };
    private readonly Border outline = new() { BorderThickness = new Thickness(1), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly CancellationTokenSource cancellation = new();
    private byte[]? pixels;
    private PixelPoint? dragStart;
    private PixelRect? selection;
    private bool solving;

    public WhiteBalanceSampler.Adjustment Adjustment { get; private set; }

    public WhiteBalanceSampleDialog(ProjectDocument project, VideoEffect grade, MediaService media, long frame)
        : base("Sample Neutral Tone", 688)
    {
        this.project = project;
        this.grade = grade;
        this.media = media;
        this.frame = frame;
        var scale = Math.Min(1, Math.Min(640d / project.Width, 400d / project.Height));
        width = Math.Max(1, (int)Math.Round(project.Width * scale));
        height = Math.Max(1, (int)Math.Round(project.Height * scale));

        Form.Children.Add(new TextBlock
        {
            Text = "Click a white or gray area, or drag to average a region.",
            Margin = new Thickness(0, 0, 0, 10)
        });

        viewport = new Grid
        {
            Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Cross, Focusable = true, ClipToBounds = true
        };
        viewport.SetResourceReference(Panel.BackgroundProperty, "PanelBrush");
        AutomationProperties.SetAutomationId(image, "NeutralTonePreview");
        AutomationProperties.SetName(image, "Frame at the timeline cursor");
        AutomationProperties.SetHelpText(viewport, "Press Space to sample the center, or use the arrow keys to move the sample.");
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        outline.SetResourceReference(Border.BorderBrushProperty, "FocusBrush");
        viewport.Children.Add(image);
        viewport.Children.Add(new Canvas { IsHitTestVisible = false, Children = { outline } });
        viewport.MouseLeftButtonDown += StartSelection;
        viewport.MouseMove += (_, args) =>
        {
            if (dragStart is { } start && args.LeftButton == MouseButtonState.Pressed)
                SelectArea(start, PixelAt(args.GetPosition(viewport)));
        };
        viewport.MouseLeftButtonUp += FinishSelection;
        viewport.LostMouseCapture += (_, _) => dragStart = null;
        viewport.KeyDown += MoveSample;
        Form.Children.Add(viewport);

        Form.Children.Add(status);
        AutomationProperties.SetAutomationId(status, "NeutralToneStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(AcceptButton, "ApplyNeutralTone");
        AcceptButton.Content = "_Apply";
        AcceptButton.IsEnabled = false;
        ShowStatus("Rendering preview…");
        Loaded += async (_, _) => await LoadFrameAsync();
        Closed += (_, _) =>
        {
            cancellation.Cancel();
            cancellation.Dispose();
        };
    }

    private async Task LoadFrameAsync()
    {
        var token = cancellation.Token;
        try
        {
            var displayed = await Task.Run(() =>
            {
                using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
                return renderer.RenderFrame(project, frame, width, height);
            }, token);
            if (token.IsCancellationRequested) return;
            pixels = displayed;
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, displayed, width * 4);
            bitmap.Freeze();
            image.Source = bitmap;
            ShowStatus(null);
            viewport.Focus();
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) ShowStatus($"The preview could not be rendered. {error.Message}");
        }
    }

    private void StartSelection(object sender, MouseButtonEventArgs args)
    {
        if (pixels is null || solving) return;
        viewport.Focus();
        var point = PixelAt(args.GetPosition(viewport));
        if (viewport.CaptureMouse()) dragStart = point;
        SelectArea(point, point);
        args.Handled = true;
    }

    private void FinishSelection(object sender, MouseButtonEventArgs args)
    {
        if (dragStart is not { } start) return;
        SelectArea(start, PixelAt(args.GetPosition(viewport)));
        viewport.ReleaseMouseCapture();
        args.Handled = true;
    }

    private void MoveSample(object sender, KeyEventArgs args)
    {
        if (pixels is null || solving) return;
        var current = selection?.Location ?? new PixelPoint(width / 2, height / 2);
        var delta = args.Key switch
        {
            Key.Left => new PixelPoint(-1, 0),
            Key.Right => new PixelPoint(1, 0),
            Key.Up => new PixelPoint(0, -1),
            Key.Down => new PixelPoint(0, 1),
            Key.Space => PixelPoint.Empty,
            _ => (PixelPoint?)null
        };
        if (delta is not { } move) return;
        var point = PixelAt(new Point(current.X + move.X, current.Y + move.Y));
        SelectArea(point, point);
        args.Handled = true;
    }

    private PixelPoint PixelAt(Point point) =>
        new(Math.Clamp((int)Math.Floor(point.X), 0, width - 1), Math.Clamp((int)Math.Floor(point.Y), 0, height - 1));

    private void SelectArea(PixelPoint a, PixelPoint b)
    {
        var area = PixelRect.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
        selection = area;
        outline.Width = Math.Max(4, area.Width);
        outline.Height = Math.Max(4, area.Height);
        Canvas.SetLeft(outline, area.X);
        Canvas.SetTop(outline, area.Y);
        outline.Visibility = Visibility.Visible;
        AcceptButton.IsEnabled = true;
        ShowStatus(null);
    }

    protected override bool TryAccept()
    {
        if (!solving && pixels is { } displayed && selection is { } area) _ = SolveAsync(displayed, area);
        return false;
    }

    private async Task SolveAsync(byte[] displayed, PixelRect area)
    {
        solving = true;
        AcceptButton.IsEnabled = false;
        ShowStatus("Checking white balance…");
        var token = cancellation.Token;
        try
        {
            var result = await Task.Run(() =>
            {
                using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
                var current = WhiteBalanceSampler.Adjustment.FromGrade(grade);
                try
                {
                    return WhiteBalanceSampler.Solve(current, displayed, width, height, area, adjustment =>
                    {
                        adjustment.ApplyTo(grade);
                        return renderer.RenderFrame(project, frame, width, height);
                    }, token);
                }
                finally { current.ApplyTo(grade); }
            }, token);
            if (token.IsCancellationRequested) return;
            if (result.Status == WhiteBalanceSampler.SampleStatus.Adjusted)
            {
                Adjustment = result.Adjustment;
                DialogResult = true;
                return;
            }
            ShowStatus(result.Status switch
            {
                WhiteBalanceSampler.SampleStatus.AlreadyNeutral => "This sample is already neutral.",
                WhiteBalanceSampler.SampleStatus.TooDark => "Choose a brighter white or gray area.",
                _ => "The selected area did not respond to this grade. Choose a visible white or gray area."
            });
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) ShowStatus($"White balance could not be sampled. {error.Message}");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                solving = false;
                AcceptButton.IsEnabled = true;
            }
        }
    }

    private void ShowStatus(string? message)
    {
        status.Text = message ?? "";
        status.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
