using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

public sealed class SceneDetectionWindow : TimelineEditDialog
{
    private sealed record Candidate(TimelineClip Clip, MediaAsset Media, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record CutChoice(SceneCut Cut, CheckBox Include);

    private readonly EditorViewModel vm;
    private readonly ComboBox events = new() { DisplayMemberPath = nameof(Candidate.Label) };
    private readonly ComboBox action = new() { Items = { "Split selected event", "Add scene regions", "Create subclips in Project Media" }, SelectedIndex = 0 };
    private readonly Slider sensitivity = new() { Minimum = 10, Maximum = 95, Value = 65, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBox minimumLength = TimeField("500");
    private readonly CheckBox grouped = new() { Content = "Split linked and grouped events at each cut", IsChecked = true };
    private readonly Button analyze = new() { Content = "Analyze", MinWidth = 80 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ProgressBar progress = new() { Height = 8, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel inputs = new();
    private readonly StackPanel review = new();
    private readonly StackPanel actionField;
    private readonly List<CutChoice> choices = [];
    private CancellationTokenSource? scan;
    private bool closed;

    public SceneDetectionWindow(EditorViewModel viewModel, TimelineClip? initialClip = null) : base("Scene Detection", 510)
    {
        vm = viewModel;
        var candidates = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips.Select(clip => (Track: track, Clip: clip)))
            .Join(vm.Project.Media, pair => pair.Clip.MediaId, media => media.Id, (pair, media) => (pair.Track, pair.Clip, Media: media))
            .Where(pair => pair.Media.HasVideo && !pair.Media.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase))
            .Select(pair => new Candidate(pair.Clip, pair.Media, $"{pair.Clip.Name} · {pair.Track.Name}")).ToArray();
        events.ItemsSource = candidates;
        events.SelectedItem = candidates.FirstOrDefault(candidate => initialClip?.Id == candidate.Clip.Id)
            ?? candidates.FirstOrDefault(candidate => vm.Selection.Contains(candidate.Clip.Id))
            ?? candidates.FirstOrDefault();

        AutomationProperties.SetAutomationId(events, "SceneDetectionEvent");
        AutomationProperties.SetAutomationId(action, "SceneDetectionAction");
        AutomationProperties.SetAutomationId(minimumLength, "MinimumSceneLength");
        AutomationProperties.SetAutomationId(analyze, "AnalyzeScenes");
        AutomationProperties.SetAutomationId(AcceptButton, "ApplySceneCuts");
        AutomationProperties.SetAutomationId(status, "SceneDetectionStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        actionField = Field("A_ction", action);

        inputs.Children.Add(Field("_Video event", events));
        var sensitivityRow = new DockPanel();
        var value = new TextBlock { Width = 42, Text = "65%", TextAlignment = TextAlignment.Right };
        DockPanel.SetDock(value, Dock.Right);
        sensitivityRow.Children.Add(value);
        sensitivityRow.Children.Add(sensitivity);
        inputs.Children.Add(Field("_Sensitivity", sensitivity, sensitivityRow));
        inputs.Children.Add(Field("_Minimum scene length (ms)", minimumLength));
        Form.Children.Add(inputs);

        var scanBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(analyze, Dock.Right);
        scanBar.Children.Add(analyze);
        scanBar.Children.Add(status);
        Form.Children.Add(scanBar);
        Form.Children.Add(progress);
        Form.Children.Add(review);

        action.SelectionChanged += (_, _) => grouped.Visibility = action.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        events.SelectionChanged += (_, _) => ResetResults();
        minimumLength.TextChanged += (_, _) => ResetResults();
        sensitivity.ValueChanged += (_, _) =>
        {
            value.Text = $"{sensitivity.Value:0}%";
            ResetResults();
        };
        analyze.Click += async (_, _) =>
        {
            if (scan is { } current) current.Cancel();
            else await AnalyzeAsync();
        };
        Closed += (_, _) =>
        {
            closed = true;
            scan?.Cancel();
        };
        AcceptButton.Content = "_Apply";
        ResetResults();
    }

    private bool CanAnalyze() => events.SelectedItem is Candidate target && !target.Clip.IsReversed
        && target.Clip.PlaybackRate == 1 && target.Clip.UnderSampleRate == 1
        && target.Clip.Envelopes?.Any(envelope => envelope.Type == EnvelopeType.Velocity && envelope.IsEnabled) != true
        && target.Clip.SourceInFrame >= 0 && target.Clip.SourceInFrame + target.Clip.DurationFrames <= vm.Project.FrameRate.Frames(target.Media.DurationSeconds);

    private void ResetResults()
    {
        ClearValidation();
        choices.Clear();
        review.Children.Clear();
        UpdateApplyState();
        analyze.IsEnabled = CanAnalyze();
        ShowStatus(analyze.IsEnabled ? "Analyze the event for hard cuts."
            : events.SelectedItem is null ? "No video events are available."
            : "Analyze requires a forward, unretimed event within its source range.");
    }

    private async Task AnalyzeAsync()
    {
        if (!CanAnalyze() || events.SelectedItem is not Candidate target) return;
        if (!double.TryParse(minimumLength.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var duration)
            || !double.IsFinite(duration) || duration is < 50 or > 60000)
        {
            Reject(minimumLength, "Enter a minimum scene length from 50 to 60,000 ms.");
            return;
        }

        ResetResults();
        using var cancellation = new CancellationTokenSource();
        scan = cancellation;
        inputs.IsEnabled = false;
        analyze.Content = "Cancel scan";
        analyze.IsDefault = false;
        progress.Value = 0;
        progress.Visibility = Visibility.Visible;
        ShowStatus("Analyzing video frames…");
        try
        {
            var updates = new Progress<double>(value =>
            {
                if (!closed && scan == cancellation) progress.Value = value;
            });
            var options = new SceneDetectionOptions { Sensitivity = sensitivity.Value / 100, MinSceneLengthMs = duration };
            var detected = await new SceneDetectionService(vm.Media).DetectScenesAsync(
                target.Media, target.Clip.SourceInFrame, target.Clip.DurationFrames, vm.Project.FrameRate, options, target.Clip.StartFrame,
                updates, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!closed) ShowCuts(detected);
        }
        catch (OperationCanceledException)
        {
            if (!closed) ShowStatus("Analysis cancelled.");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
            if (!closed) ShowStatus("Analysis failed. " + error.Message);
        }
        finally
        {
            scan = null;
            if (!closed)
            {
                inputs.IsEnabled = true;
                analyze.Content = "Analyze";
                progress.Visibility = Visibility.Collapsed;
                UpdateApplyState();
            }
        }
    }

    private void ShowCuts(IReadOnlyList<SceneCut> cuts)
    {
        if (cuts.Count == 0)
        {
            ShowStatus("No cuts found with these settings.");
            return;
        }

        ShowStatus(cuts.Count == 1 ? "Review 1 detected cut." : $"Review {cuts.Count} detected cuts.");
        var rows = new StackPanel();
        var project = vm.Project;
        foreach (var cut in cuts)
        {
            var position = TimecodeFormatter.Format(cut.AbsoluteFrame, project.FrameRate, project.RulerFormat,
                project.AudioSampleRate, project.BeatsPerMinute, project.BeatsPerMeasure, project.RulerStartTimeFrames);
            var include = new CheckBox { Content = $"Cut at {position}", IsChecked = true };
            AutomationProperties.SetAutomationId(include, $"SceneCut_{cut.AbsoluteFrame}");
            include.Checked += (_, _) => UpdateApplyState();
            include.Unchecked += (_, _) => UpdateApplyState();
            choices.Add(new(cut, include));
            rows.Children.Add(include);
        }

        var scroll = new ScrollViewer { Content = rows, MaxHeight = 145, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        review.Children.Add(new TextBlock { Text = "Detected cuts", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var list = new Border { Child = scroll, BorderThickness = new Thickness(1), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8) };
        list.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        list.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        review.Children.Add(list);
        review.Children.Add(actionField);
        review.Children.Add(grouped);
        UpdateApplyState();
    }

    private void UpdateApplyState()
    {
        var canApply = scan is null && choices.Any(choice => choice.Include.IsChecked == true);
        AcceptButton.IsEnabled = AcceptButton.IsDefault = canApply;
        analyze.IsDefault = scan is null && !canApply;
    }

    protected override bool TryAccept()
    {
        if (events.SelectedItem is not Candidate target || scan is not null) return false;
        var cuts = choices.Where(choice => choice.Include.IsChecked == true).Select(choice => choice.Cut).ToArray();
        if (cuts.Length == 0) return false;

        var mode = (SceneDetectionMode)action.SelectedIndex;
        try
        {
            var result = vm.Editor.ApplySceneDetection(target.Clip.Id, cuts, mode,
                new() { IncludeGroupedTracks = grouped.IsChecked == true }, target.Clip.Name);
            vm.Status = mode switch
            {
                SceneDetectionMode.SplitOnTimeline => $"Split the event into {result.SceneCount} scenes.",
                SceneDetectionMode.AddRegionMarkers => $"Added {result.ResultRegions.Count} scene regions.",
                _ => $"Created {result.ResultSubclips.Count} scene subclips in Project Media."
            };
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or OverflowException)
        {
            ShowStatus("Could not apply the cuts. " + error.Message);
            return false;
        }
    }

    private void ShowStatus(string message)
    {
        status.Text = message;
        UIElementAutomationPeer.CreatePeerForElement(status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
