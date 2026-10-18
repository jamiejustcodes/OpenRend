using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.App;

/// <summary>
/// Vegas Pro 1:1 Text-Based Editing window.
/// Provides speech transcription arrangement, word-level timeline navigation,
/// synchronized playback cursor highlighting, middle/head/tail range cutting with ripple,
/// word boundary splitting, pause detection and deletion, and find & replace.
/// </summary>
public sealed class TextBasedEditingWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Action? onPreviewRequest;
    private Guid? targetClipId;

    // View state
    private bool showSelectedOnly;
    private bool autoRipple = true;
    private bool showPauses = true;
    private bool showTimecodes = true;
    private bool showSpeakerNames = true;
    private double minPauseSeconds = 0.5;

    // Selection across words
    private Guid? selectedClipId;
    private readonly List<int> selectedWordIndices = [];

    // Controls
    private readonly ComboBox cbMode = new() { Width = 110 };
    private readonly ToggleButton btnShowSelected = new() { Width = 32, Height = 26, ToolTip = "Show Only Selected Events" };
    private readonly ToggleButton btnAutoRipple = new() { Width = 32, Height = 26, IsChecked = true, ToolTip = "Auto Ripple (Affected Tracks)" };
    private readonly CheckBox chkShowPauses = new() { Content = "Pauses", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
    private readonly Slider sliderMinPause = new() { Minimum = 0.2, Maximum = 2.0, Value = 0.5, Width = 80, TickFrequency = 0.1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock lblMinPause = new() { Text = "0.5s", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0), Foreground = Brushes.Silver, Width = 30 };
    private readonly Button btnDeletePauses = new() { Content = "Delete Pauses", Height = 26, Padding = new Thickness(8, 0, 8, 0), ToolTip = "Remove all detected pauses on the timeline" };
    private readonly CheckBox chkShowTimecodes = new() { Content = "Timecodes", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
    private readonly CheckBox chkShowSpeakers = new() { Content = "Speakers", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
    private readonly Button btnTranscribe = new() { Content = "Transcribe Audio…", Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(4, 0, 4, 0) };

    // Find and Replace
    private readonly TextBox txtFind = new() { Width = 110, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox txtReplace = new() { Width = 110, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button btnFindNext = new() { Content = "Find", Height = 24, Padding = new Thickness(6, 0, 6, 0) };
    private readonly Button btnReplace = new() { Content = "Replace", Height = 24, Padding = new Thickness(6, 0, 6, 0) };
    private readonly Button btnReplaceAll = new() { Content = "All", Height = 24, Padding = new Thickness(6, 0, 6, 0) };

    // Action panel
    private readonly Border selectionActionBar = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(32, 38, 46)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(45, 110, 190)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10, 6, 10, 6),
        Margin = new Thickness(12, 0, 12, 10),
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock lblSelectionInfo = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button btnCutSelected = new() { Content = "Cut (Ctrl+X)", Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button btnDeleteSelected = new() { Content = "Delete (Del)", Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button btnSplitAtWord = new() { Content = "Split Here (S)", Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button btnCopySelected = new() { Content = "Copy (Ctrl+C)", Height = 26, Padding = new Thickness(8, 0, 8, 0) };

    // Word display container
    private readonly StackPanel eventsContainer = new() { Margin = new Thickness(12) };
    private readonly ScrollViewer scrollViewer = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    // Active word highlight tracking
    private readonly List<(Guid ClipId, int WordIndex, Border TokenBorder, WordTiming Timing)> activeWordTokens = [];

    public TextBasedEditingWindow(EditorViewModel viewModel, Guid? initialClipId = null, Action? onPreviewRequest = null)
    {
        vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        targetClipId = initialClipId;
        this.onPreviewRequest = onPreviewRequest;
        showSelectedOnly = targetClipId.HasValue;

        Style = (Style)FindResource(typeof(Window));
        Title = "Text-Based Editing — Vegas Pro Parity";
        Width = 980;
        Height = 680;
        MinWidth = 800;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildLayout();

        // Wire events
        vm.PropertyChanged += Vm_PropertyChanged;
        vm.Changed += Vm_Changed;
        Closed += (_, _) =>
        {
            vm.PropertyChanged -= Vm_PropertyChanged;
            vm.Changed -= Vm_Changed;
        };

        KeyDown += OnWindowKeyDown;

        PopulateEvents();
    }

    private UIElement BuildLayout()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Find & Replace
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Transcript
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Action bar

        // 1. Toolbar
        var toolbar = new ToolBarTray { Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)) };
        var tb = new ToolBar { Background = Brushes.Transparent };

        // Mode combobox
        cbMode.Items.Add("Editing");
        cbMode.Items.Add("Script");
        cbMode.Items.Add("Subtitles");
        cbMode.SelectedIndex = 0;
        cbMode.SelectionChanged += (_, _) => PopulateEvents();
        tb.Items.Add(new TextBlock { Text = "Mode: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 2, 0) });
        tb.Items.Add(cbMode);
        tb.Items.Add(new Separator());

        // Show selected toggle
        btnShowSelected.IsChecked = showSelectedOnly;
        btnShowSelected.Content = "🎯";
        btnShowSelected.Click += (_, _) =>
        {
            showSelectedOnly = btnShowSelected.IsChecked == true;
            PopulateEvents();
        };
        tb.Items.Add(btnShowSelected);

        // Auto ripple toggle
        btnAutoRipple.Content = "⇄";
        btnAutoRipple.Click += (_, _) => autoRipple = btnAutoRipple.IsChecked == true;
        tb.Items.Add(btnAutoRipple);
        tb.Items.Add(new Separator());

        // Pauses
        chkShowPauses.Checked += (_, _) => { showPauses = true; PopulateEvents(); };
        chkShowPauses.Unchecked += (_, _) => { showPauses = false; PopulateEvents(); };
        sliderMinPause.ValueChanged += (_, e) =>
        {
            minPauseSeconds = Math.Round(e.NewValue, 1);
            lblMinPause.Text = $"{minPauseSeconds:0.0}s";
            PopulateEvents();
        };
        btnDeletePauses.Click += OnDeleteAllPausesClick;
        tb.Items.Add(chkShowPauses);
        tb.Items.Add(sliderMinPause);
        tb.Items.Add(lblMinPause);
        tb.Items.Add(btnDeletePauses);
        tb.Items.Add(new Separator());

        // Display options
        chkShowTimecodes.Checked += (_, _) => { showTimecodes = true; PopulateEvents(); };
        chkShowTimecodes.Unchecked += (_, _) => { showTimecodes = false; PopulateEvents(); };
        chkShowSpeakers.Checked += (_, _) => { showSpeakerNames = true; PopulateEvents(); };
        chkShowSpeakers.Unchecked += (_, _) => { showSpeakerNames = false; PopulateEvents(); };
        tb.Items.Add(chkShowTimecodes);
        tb.Items.Add(chkShowSpeakers);
        tb.Items.Add(new Separator());

        // Transcribe
        btnTranscribe.Click += OnTranscribeClick;
        tb.Items.Add(btnTranscribe);

        toolbar.ToolBars.Add(tb);
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        // 2. Find and Replace bar
        var findBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 4, 10, 4)
        };
        var findPanel = new StackPanel { Orientation = Orientation.Horizontal };
        findPanel.Children.Add(new TextBlock { Text = "Find:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        findPanel.Children.Add(txtFind);
        btnFindNext.Margin = new Thickness(6, 0, 12, 0);
        btnFindNext.Click += OnFindNextClick;
        findPanel.Children.Add(btnFindNext);

        findPanel.Children.Add(new TextBlock { Text = "Replace:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        findPanel.Children.Add(txtReplace);
        btnReplace.Margin = new Thickness(6, 0, 4, 0);
        btnReplace.Click += OnReplaceClick;
        findPanel.Children.Add(btnReplace);
        btnReplaceAll.Click += OnReplaceAllClick;
        findPanel.Children.Add(btnReplaceAll);

        findBar.Child = findPanel;
        Grid.SetRow(findBar, 1);
        root.Children.Add(findBar);

        // 3. Scroll area
        scrollViewer.Content = eventsContainer;
        Grid.SetRow(scrollViewer, 2);
        root.Children.Add(scrollViewer);

        // 4. Selection action bar
        var actionPanel = new StackPanel { Orientation = Orientation.Horizontal };
        actionPanel.Children.Add(lblSelectionInfo);
        btnCutSelected.Click += (_, _) => ExecuteCutSelected();
        btnDeleteSelected.Click += (_, _) => ExecuteDeleteSelected();
        btnSplitAtWord.Click += (_, _) => ExecuteSplitAtSelectedWord();
        btnCopySelected.Click += (_, _) => ExecuteCopySelected();

        actionPanel.Children.Add(btnCutSelected);
        actionPanel.Children.Add(btnDeleteSelected);
        actionPanel.Children.Add(btnSplitAtWord);
        actionPanel.Children.Add(btnCopySelected);
        selectionActionBar.Child = actionPanel;

        Grid.SetRow(selectionActionBar, 3);
        root.Children.Add(selectionActionBar);

        return root;
    }

    private void PopulateEvents()
    {
        eventsContainer.Children.Clear();
        activeWordTokens.Clear();
        ClearSelection();

        var clips = new List<(TimelineTrack Track, TimelineClip Clip)>();
        foreach (var track in vm.Project.Tracks)
        {
            foreach (var clip in track.Clips.OrderBy(c => c.StartFrame))
            {
                if (showSelectedOnly && targetClipId.HasValue && clip.Id != targetClipId.Value && !vm.Selection.Contains(clip.Id))
                    continue;

                clips.Add((track, clip));
            }
        }

        if (clips.Count == 0)
        {
            eventsContainer.Children.Add(new TextBlock
            {
                Text = showSelectedOnly ? "No selected event has transcript data." : "No clips found on timeline. Import media and transcribe dialogue to edit text.",
                Foreground = Brushes.Gray,
                FontSize = 14,
                Margin = new Thickness(24)
            });
            return;
        }

        var rate = vm.Project.FrameRate;
        var minPauseFrames = rate.Frames(minPauseSeconds);

        foreach (var (track, clip) in clips)
        {
            var eventCard = CreateEventBlock(track, clip, minPauseFrames);
            eventsContainer.Children.Add(eventCard);
        }

        HighlightActiveWord();
    }

    private Border CreateEventBlock(TimelineTrack track, TimelineClip clip, long minPauseFrames)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(12)
        };

        var panel = new StackPanel();

        // Header
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Track badge
        var trackColor = track.Kind == TrackKind.Audio ? Color.FromRgb(46, 125, 50) : Color.FromRgb(25, 118, 210);
        var badge = new Border
        {
            Background = new SolidColorBrush(trackColor),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 8, 0)
        };
        badge.Child = new TextBlock
        {
            Text = $"{track.Name} [{track.Kind}]",
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.Bold
        };
        Grid.SetColumn(badge, 0);
        header.Children.Add(badge);

        // Clip name
        var title = new TextBlock
        {
            Text = clip.Name,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        // Timecode range
        if (showTimecodes)
        {
            var tcText = new TextBlock
            {
                Text = $"{FormatTimecode(clip.StartFrame, vm.Project.FrameRate)}  -->  {FormatTimecode(clip.EndFrame, vm.Project.FrameRate)}",
                Foreground = Brushes.DarkGray,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tcText, 2);
            header.Children.Add(tcText);
        }

        // Action menu / word count
        var words = clip.Transcript?.Words ?? [];
        var countLabel = new TextBlock
        {
            Text = $"{words.Count(w => w.Kind == TranscriptWordKind.Word)} words",
            Foreground = Brushes.Silver,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countLabel, 3);
        header.Children.Add(countLabel);

        panel.Children.Add(header);

        // Body: Flowing word wrap panel
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

        if (words.Count == 0)
        {
            var emptyNotice = new TextBlock
            {
                Text = "No transcript attached. Click 'Transcribe Audio' above to transcribe this event.",
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(4)
            };
            wrap.Children.Add(emptyNotice);
        }
        else
        {
            // Build tokens, inserting pauses if requested
            for (var i = 0; i < words.Count; i++)
            {
                var word = words[i];

                // Detect pause before word
                if (showPauses)
                {
                    long gapFrames = 0;
                    long gapStart = 0;
                    if (i == 0 && word.StartFrame >= minPauseFrames)
                    {
                        gapFrames = word.StartFrame;
                        gapStart = 0;
                    }
                    else if (i > 0)
                    {
                        var prevEnd = words[i - 1].EndFrame;
                        if (word.StartFrame - prevEnd >= minPauseFrames)
                        {
                            gapFrames = word.StartFrame - prevEnd;
                            gapStart = prevEnd;
                        }
                    }

                    if (gapFrames > 0)
                    {
                        var pauseSec = vm.Project.FrameRate.Seconds(gapFrames);
                        var pauseToken = CreatePauseToken(clip.Id, gapStart, gapFrames, pauseSec);
                        wrap.Children.Add(pauseToken);
                    }
                }

                // Word token
                var wordToken = CreateWordToken(clip, i, word);
                wrap.Children.Add(wordToken);
            }

            // Final pause after last word
            if (showPauses && words.Count > 0)
            {
                var lastEnd = words[^1].EndFrame;
                if (clip.DurationFrames - lastEnd >= minPauseFrames)
                {
                    var gapFrames = clip.DurationFrames - lastEnd;
                    var pauseSec = vm.Project.FrameRate.Seconds(gapFrames);
                    var pauseToken = CreatePauseToken(clip.Id, lastEnd, gapFrames, pauseSec);
                    wrap.Children.Add(pauseToken);
                }
            }
        }

        panel.Children.Add(wrap);
        card.Child = panel;
        return card;
    }

    private Border CreateWordToken(TimelineClip clip, int wordIndex, WordTiming word)
    {
        var isSelected = selectedClipId == clip.Id && selectedWordIndices.Contains(wordIndex);

        var border = new Border
        {
            Background = isSelected
                ? new SolidColorBrush(Color.FromRgb(25, 118, 210))
                : new SolidColorBrush(Color.FromRgb(36, 36, 36)),
            BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(66, 165, 245))
                : new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(2),
            Cursor = Cursors.Hand
        };

        var textBlock = new TextBlock
        {
            Text = word.Word,
            Foreground = isSelected ? Brushes.White : Brushes.WhiteSmoke,
            FontSize = 13
        };

        if (showSpeakerNames && !string.IsNullOrEmpty(word.Speaker))
        {
            var speakerPrefix = new TextBlock
            {
                Text = $"[{word.Speaker}] ",
                Foreground = Brushes.SkyBlue,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(speakerPrefix);
            sp.Children.Add(textBlock);
            border.Child = sp;
        }
        else
        {
            border.Child = textBlock;
        }

        var absStart = clip.StartFrame + word.StartFrame;
        var absEnd = clip.StartFrame + word.EndFrame;
        border.ToolTip = $"Word: \"{word.Word}\"\nClip Offset: {word.StartFrame}f ({word.DurationFrames}f)\nTimeline: {FormatTimecode(absStart, vm.Project.FrameRate)} --> {FormatTimecode(absEnd, vm.Project.FrameRate)}\nConfidence: {word.Confidence:P0}";

        border.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            OnWordTokenClicked(clip.Id, wordIndex, word, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        };

        border.MouseRightButtonDown += (s, e) =>
        {
            e.Handled = true;
            OnWordTokenRightClicked(clip.Id, wordIndex, word);
        };

        activeWordTokens.Add((clip.Id, wordIndex, border, word));
        return border;
    }

    private Border CreatePauseToken(Guid clipId, long offsetStart, long durationFrames, double seconds)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(38, 30, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 60, 40)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(2),
            Cursor = Cursors.Hand
        };

        var text = new TextBlock
        {
            Text = $"[... {seconds:0.0}s]",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0)),
            FontSize = 11,
            FontWeight = FontWeights.Medium
        };
        border.Child = text;
        border.ToolTip = $"Pause gap: {durationFrames} frames ({seconds:0.00}s)\nClick to jump playhead.\nRight-click to delete pause.";

        border.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            if (clip is not null)
            {
                vm.Position = clip.StartFrame + offsetStart;
                onPreviewRequest?.Invoke();
            }
        };

        border.MouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            vm.Editor.DeleteTranscriptRange(clipId, offsetStart, offsetStart + durationFrames, autoRipple: autoRipple);
            PopulateEvents();
        };

        return border;
    }

    private void OnWordTokenClicked(Guid clipId, int wordIndex, WordTiming word, bool isShift)
    {
        var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null) return;

        // Timeline playhead jump
        vm.Position = clip.StartFrame + word.StartFrame;
        onPreviewRequest?.Invoke();

        // Selection update
        if (isShift && selectedClipId == clipId && selectedWordIndices.Count > 0)
        {
            var first = selectedWordIndices[0];
            var min = Math.Min(first, wordIndex);
            var max = Math.Max(first, wordIndex);
            selectedWordIndices.Clear();
            for (var i = min; i <= max; i++)
                selectedWordIndices.Add(i);
        }
        else
        {
            selectedClipId = clipId;
            selectedWordIndices.Clear();
            selectedWordIndices.Add(wordIndex);
        }

        UpdateSelectionActionBar();
        RefreshTokenVisuals();
    }

    private void OnWordTokenRightClicked(Guid clipId, int wordIndex, WordTiming word)
    {
        selectedClipId = clipId;
        if (!selectedWordIndices.Contains(wordIndex))
        {
            selectedWordIndices.Clear();
            selectedWordIndices.Add(wordIndex);
        }
        UpdateSelectionActionBar();
        RefreshTokenVisuals();

        var cm = new ContextMenu();
        var miCut = new MenuItem { Header = "Cut Range", InputGestureText = "Ctrl+X" };
        miCut.Click += (_, _) => ExecuteCutSelected();
        cm.Items.Add(miCut);

        var miDelete = new MenuItem { Header = "Delete Range", InputGestureText = "Del" };
        miDelete.Click += (_, _) => ExecuteDeleteSelected();
        cm.Items.Add(miDelete);

        var miSplit = new MenuItem { Header = "Split Event at Word", InputGestureText = "S" };
        miSplit.Click += (_, _) => ExecuteSplitAtSelectedWord();
        cm.Items.Add(miSplit);

        cm.Items.Add(new Separator());

        var miEdit = new MenuItem { Header = "Edit Word Text…" };
        miEdit.Click += (_, _) => PromptEditWord(clipId, wordIndex, word);
        cm.Items.Add(miEdit);

        cm.IsOpen = true;
    }

    private void UpdateSelectionActionBar()
    {
        if (selectedClipId.HasValue && selectedWordIndices.Count > 0)
        {
            var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == selectedClipId.Value);
            if (clip?.Transcript is not null)
            {
                var minIdx = selectedWordIndices.Min();
                var maxIdx = selectedWordIndices.Max();
                var first = clip.Transcript.Words[minIdx];
                var last = clip.Transcript.Words[maxIdx];
                var durFrames = last.EndFrame - first.StartFrame;
                var sec = vm.Project.FrameRate.Seconds(durFrames);
                lblSelectionInfo.Text = $"Selected: {selectedWordIndices.Count} words  ({sec:0.00}s)";
                selectionActionBar.Visibility = Visibility.Visible;
                return;
            }
        }

        selectionActionBar.Visibility = Visibility.Collapsed;
    }

    private void ClearSelection()
    {
        selectedClipId = null;
        selectedWordIndices.Clear();
        selectionActionBar.Visibility = Visibility.Collapsed;
        RefreshTokenVisuals();
    }

    private void RefreshTokenVisuals()
    {
        foreach (var (cId, wIdx, border, _) in activeWordTokens)
        {
            var isSel = selectedClipId == cId && selectedWordIndices.Contains(wIdx);
            border.Background = isSel
                ? new SolidColorBrush(Color.FromRgb(25, 118, 210))
                : new SolidColorBrush(Color.FromRgb(36, 36, 36));
            border.BorderBrush = isSel
                ? new SolidColorBrush(Color.FromRgb(66, 165, 245))
                : new SolidColorBrush(Color.FromRgb(50, 50, 50));
            if (border.Child is TextBlock tb)
                tb.Foreground = isSel ? Brushes.White : Brushes.WhiteSmoke;
        }

        HighlightActiveWord();
    }

    private void HighlightActiveWord()
    {
        var playhead = vm.Position;

        foreach (var (cId, wIdx, border, timing) in activeWordTokens)
        {
            var isSel = selectedClipId == cId && selectedWordIndices.Contains(wIdx);
            if (isSel) continue; // Keep selection highlight

            var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == cId);
            if (clip is null) continue;

            var absStart = clip.StartFrame + timing.StartFrame;
            var absEnd = clip.StartFrame + timing.EndFrame;

            if (playhead >= absStart && playhead < absEnd)
            {
                // Active playing word highlight: Vegas Pro Gold
                border.Background = new SolidColorBrush(Color.FromRgb(255, 179, 0));
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 213, 79));
                if (border.Child is TextBlock tb)
                    tb.Foreground = Brushes.Black;
            }
            else
            {
                border.Background = new SolidColorBrush(Color.FromRgb(36, 36, 36));
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50));
                if (border.Child is TextBlock tb)
                    tb.Foreground = Brushes.WhiteSmoke;
            }
        }
    }

    private void ExecuteDeleteSelected()
    {
        if (!selectedClipId.HasValue || selectedWordIndices.Count == 0) return;
        var clipId = selectedClipId.Value;
        var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip?.Transcript is null) return;

        var minIdx = selectedWordIndices.Min();
        var maxIdx = selectedWordIndices.Max();
        var startOffset = clip.Transcript.Words[minIdx].StartFrame;
        var endOffset = clip.Transcript.Words[maxIdx].EndFrame;

        vm.Editor.DeleteTranscriptRange(clipId, startOffset, endOffset, autoRipple: autoRipple, ignoreGroups: vm.IgnoreGroups);
        ClearSelection();
        PopulateEvents();
        onPreviewRequest?.Invoke();
    }

    private void ExecuteCutSelected()
    {
        if (!selectedClipId.HasValue || selectedWordIndices.Count == 0) return;
        var clipId = selectedClipId.Value;
        var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip?.Transcript is null) return;

        var minIdx = selectedWordIndices.Min();
        var maxIdx = selectedWordIndices.Max();
        var selectedWords = selectedWordIndices.Select(i => clip.Transcript.Words[i].Word);
        var text = string.Join(" ", selectedWords);
        Clipboard.SetText(text);

        var startOffset = clip.Transcript.Words[minIdx].StartFrame;
        var endOffset = clip.Transcript.Words[maxIdx].EndFrame;

        vm.Editor.DeleteTranscriptRange(clipId, startOffset, endOffset, autoRipple: autoRipple, ignoreGroups: vm.IgnoreGroups);
        ClearSelection();
        PopulateEvents();
        onPreviewRequest?.Invoke();
    }

    private void ExecuteSplitAtSelectedWord()
    {
        if (!selectedClipId.HasValue || selectedWordIndices.Count == 0) return;
        var clipId = selectedClipId.Value;
        var minIdx = selectedWordIndices.Min();

        vm.Editor.SplitClipAtWord(clipId, minIdx, ignoreGroups: vm.IgnoreGroups);
        ClearSelection();
        PopulateEvents();
        onPreviewRequest?.Invoke();
    }

    private void ExecuteCopySelected()
    {
        if (!selectedClipId.HasValue || selectedWordIndices.Count == 0) return;
        var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == selectedClipId.Value);
        if (clip?.Transcript is null) return;

        var words = selectedWordIndices.OrderBy(i => i).Select(i => clip.Transcript.Words[i].Word);
        Clipboard.SetText(string.Join(" ", words));
    }

    private void OnDeleteAllPausesClick(object sender, RoutedEventArgs e)
    {
        var clipsWithTranscripts = vm.Project.Tracks
            .SelectMany(t => t.Clips)
            .Where(c => c.Transcript is not null && c.Transcript.Words.Count > 0)
            .ToList();

        if (clipsWithTranscripts.Count == 0)
        {
            MessageBox.Show(this, "No clips with transcripts found on the timeline.", "Delete Pauses", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var minPauseFrames = vm.Project.FrameRate.Frames(minPauseSeconds);
        var totalRemoved = 0;

        foreach (var clip in clipsWithTranscripts)
        {
            totalRemoved += vm.Editor.DeletePauses(clip.Id, minPauseFrames, autoRipple: autoRipple, ignoreGroups: vm.IgnoreGroups);
        }

        PopulateEvents();
        onPreviewRequest?.Invoke();
        MessageBox.Show(this, $"Successfully removed {totalRemoved} pauses across the timeline with ripple.", "Delete Pauses", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PromptEditWord(Guid clipId, int wordIndex, WordTiming word)
    {
        var win = new Window
        {
            Title = "Edit Word (Vegas Pro In-Place)",
            Width = 340,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Style = (Style)FindResource(typeof(Window))
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        var tb = new TextBox { Text = word.Word, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 75, Height = 26, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 75, Height = 26, IsCancel = true };
        ok.Click += (_, _) =>
        {
            var newText = tb.Text.Trim();
            if (!string.IsNullOrEmpty(newText))
            {
                var clip = vm.Project.Tracks.SelectMany(t => t.Clips).First(c => c.Id == clipId);
                var transcript = clip.Transcript?.DeepClone() ?? new ClipTranscript();
                if (wordIndex < transcript.Words.Count)
                {
                    transcript.Words[wordIndex].Word = newText;
                    vm.Editor.SetClipTranscript(clipId, transcript);
                    PopulateEvents();
                }
            }
            win.DialogResult = true;
        };
        bp.Children.Add(ok);
        bp.Children.Add(cancel);
        sp.Children.Add(tb);
        sp.Children.Add(bp);
        win.Content = sp;
        win.ShowDialog();
    }

    private void OnFindNextClick(object sender, RoutedEventArgs e)
    {
        var query = txtFind.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        foreach (var (cId, wIdx, border, timing) in activeWordTokens)
        {
            if (timing.Word.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                selectedClipId = cId;
                selectedWordIndices.Clear();
                selectedWordIndices.Add(wIdx);
                UpdateSelectionActionBar();
                RefreshTokenVisuals();
                border.BringIntoView();
                return;
            }
        }
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        var query = txtFind.Text.Trim();
        var repl = txtReplace.Text;
        if (string.IsNullOrEmpty(query)) return;

        if (selectedClipId.HasValue && selectedWordIndices.Count > 0)
        {
            var clipId = selectedClipId.Value;
            var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            if (clip?.Transcript is not null)
            {
                var transcript = clip.Transcript.DeepClone();
                var idx = selectedWordIndices[0];
                if (idx < transcript.Words.Count && transcript.Words[idx].Word.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    transcript.Words[idx].Word = transcript.Words[idx].Word.Replace(query, repl, StringComparison.OrdinalIgnoreCase);
                    vm.Editor.SetClipTranscript(clipId, transcript);
                    PopulateEvents();
                    OnFindNextClick(sender, e);
                    return;
                }
            }
        }

        OnFindNextClick(sender, e);
    }

    private void OnReplaceAllClick(object sender, RoutedEventArgs e)
    {
        var query = txtFind.Text.Trim();
        var repl = txtReplace.Text;
        if (string.IsNullOrEmpty(query)) return;

        var count = 0;
        foreach (var track in vm.Project.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.Transcript is null) continue;
                var transcript = clip.Transcript.DeepClone();
                var modified = false;
                foreach (var w in transcript.Words)
                {
                    if (w.Word.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        w.Word = w.Word.Replace(query, repl, StringComparison.OrdinalIgnoreCase);
                        modified = true;
                        count++;
                    }
                }
                if (modified)
                {
                    vm.Editor.SetClipTranscript(clip.Id, transcript);
                }
            }
        }

        PopulateEvents();
        MessageBox.Show(this, $"Replaced {count} occurrences of \"{query}\".", "Replace All", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnTranscribeClick(object sender, RoutedEventArgs e)
    {
        var sttWin = new SpeechToTextWindow(vm) { Owner = this };
        sttWin.ShowDialog();
        PopulateEvents();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            ExecuteDeleteSelected();
        }
        else if (e.Key == Key.X && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ExecuteCutSelected();
        }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ExecuteCopySelected();
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            ExecuteSplitAtSelectedWord();
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Position))
        {
            Dispatcher.InvokeAsync(HighlightActiveWord);
        }
    }

    private void Vm_Changed()
    {
        Dispatcher.InvokeAsync(PopulateEvents);
    }

    private static string FormatTimecode(long frames, Rational rate)
    {
        var sec = rate.Seconds(frames);
        var ts = TimeSpan.FromSeconds(sec);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
