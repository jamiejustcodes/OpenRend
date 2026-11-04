using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro Multi-Track Mixing Console (Ctrl+Alt+5).
/// Provides dedicated audio channel strips for every timeline audio track,
/// assignable audio buses (Bus A, Bus B, ...), and Master Bus console strip,
/// including volume faders (-60 dB to +6 dB), rotary pan, mute/solo, phase inversion (Ø),
/// insert FX chains, peak meters with latching clip LEDs, and master dimming/monitoring.
/// </summary>
public sealed class MixingConsoleWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Action onProjectChanged;

    // Containers and Layout
    private readonly StackPanel tracksContainer = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer tracksScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Rectangle tracksSplitter = new() { Width = 2, HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly StackPanel busesContainer = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer busesScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Rectangle busesSplitter = new() { Width = 2, HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly Border masterStrip;

    // View toggles
    private readonly ToggleButton tracksToggle = new() { Content = "Tracks", IsChecked = true, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(8, 2, 8, 2), FontSize = 10 };
    private readonly ToggleButton busesToggle = new() { Content = "Buses", IsChecked = true, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(8, 2, 8, 2), FontSize = 10 };
    private readonly ToggleButton masterToggle = new() { Content = "Master", IsChecked = true, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(8, 2, 8, 2), FontSize = 10 };

    // Master bus controls
    private readonly Slider masterFader = new() { Orientation = Orientation.Vertical, Minimum = -60, Maximum = 6, Value = 0, Height = 160, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock masterFaderText = new() { Text = "0.0 dB", FontSize = 10, Foreground = Brushes.Silver, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border masterLeftClip = CreateClipLed();
    private readonly Border masterRightClip = CreateClipLed();
    private readonly Border masterLeftMeter = new() { Background = CreateMeterBrush(), VerticalAlignment = VerticalAlignment.Bottom, Height = 0, Width = 8 };
    private readonly Border masterRightMeter = new() { Background = CreateMeterBrush(), VerticalAlignment = VerticalAlignment.Bottom, Height = 0, Width = 8 };
    private readonly TextBlock masterPeakText = new() { Text = "0.0   0.0", FontSize = 9, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button masterMuteBtn = new() { Content = "M", Width = 24, Height = 20, Margin = new Thickness(1), FontSize = 10, FontWeight = FontWeights.Bold };
    private readonly Button masterDimBtn = new() { Content = "Dim", Width = 32, Height = 20, Margin = new Thickness(1), FontSize = 10 };

    // Dynamic metering & clipping dictionaries
    private readonly Dictionary<Guid, Border> trackMeters = [];
    private readonly Dictionary<Guid, Border> trackClipLeds = [];
    private readonly Dictionary<Guid, Border> busMeters = [];
    private readonly Dictionary<Guid, Border> busClipLeds = [];

    private bool isDimmed;

    private sealed record BusComboItem(string Name, Guid? BusId)
    {
        public override string ToString() => Name;
    }

    public MixingConsoleWindow(EditorViewModel viewModel, Action projectChanged)
    {
        vm = viewModel;
        onProjectChanged = projectChanged;

        Style = (Style)FindResource(typeof(Window));
        Title = "Mixing Console";
        Width = 840;
        Height = 520;
        MinWidth = 480;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var borderBrush = (Brush)FindResource("BorderBrush");
        var headerBrush = (Brush)FindResource("HeaderBrush");
        var textBrush = (Brush)FindResource("TextBrush");

        tracksSplitter.Fill = borderBrush;
        busesSplitter.Fill = borderBrush;

        var mainGrid = new Grid { Background = panelBrush };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Top Toolbar
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Strips Area

        // 1. Build Top Toolbar
        var toolbar = BuildTopToolbar();
        Grid.SetRow(toolbar, 0);
        mainGrid.Children.Add(toolbar);

        // 2. Build Main Strips Grid
        var stripsGrid = new Grid();
        stripsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Tracks strips
        stripsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Separator
        stripsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Buses strips
        stripsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Separator
        stripsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // Master bus strip

        // 2.1 Tracks Scroll Area
        tracksScroll.Content = tracksContainer;
        Grid.SetColumn(tracksScroll, 0);
        stripsGrid.Children.Add(tracksScroll);

        // 2.2 Splitter 1
        Grid.SetColumn(tracksSplitter, 1);
        stripsGrid.Children.Add(tracksSplitter);

        // 2.3 Buses Scroll Area
        busesScroll.Content = busesContainer;
        Grid.SetColumn(busesScroll, 2);
        stripsGrid.Children.Add(busesScroll);

        // 2.4 Splitter 2
        Grid.SetColumn(busesSplitter, 3);
        stripsGrid.Children.Add(busesSplitter);

        // 2.5 Master Bus Strip
        masterStrip = BuildMasterStrip();
        Grid.SetColumn(masterStrip, 4);
        stripsGrid.Children.Add(masterStrip);

        Grid.SetRow(stripsGrid, 1);
        mainGrid.Children.Add(stripsGrid);

        Content = mainGrid;

        Loaded += (_, _) => RefreshTracks();
    }

    private FrameworkElement BuildTopToolbar()
    {
        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(36, 36, 36)),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4, 8, 4)
        };

        var dock = new DockPanel();

        // Left: Add Bus button
        var addBusBtn = new Button
        {
            Content = "+ Add Bus",
            FontSize = 10,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Create a new assignable audio bus track"
        };
        addBusBtn.Click += (_, _) =>
        {
            vm.Editor.CreateAudioBus();
            onProjectChanged();
            RefreshTracks();
        };
        DockPanel.SetDock(addBusBtn, Dock.Left);
        dock.Children.Add(addBusBtn);

        // Right: Reset Clips button
        var resetClipsBtn = new Button
        {
            Content = "Reset Clips",
            FontSize = 10,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Clear all latched overload clip indicators"
        };
        resetClipsBtn.Click += (_, _) => ResetAllClipLeds();
        DockPanel.SetDock(resetClipsBtn, Dock.Right);
        dock.Children.Add(resetClipsBtn);

        // Center: Section View Toggles
        var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        tracksToggle.Click += (_, _) =>
        {
            var show = tracksToggle.IsChecked ?? true;
            tracksScroll.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            tracksSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        };
        busesToggle.Click += (_, _) =>
        {
            var show = busesToggle.IsChecked ?? true;
            busesScroll.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            busesSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        };
        masterToggle.Click += (_, _) =>
        {
            var show = masterToggle.IsChecked ?? true;
            masterStrip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        };

        togglePanel.Children.Add(tracksToggle);
        togglePanel.Children.Add(busesToggle);
        togglePanel.Children.Add(masterToggle);
        dock.Children.Add(togglePanel);

        bar.Child = dock;
        return bar;
    }

    /// <summary>
    /// Rebuilds channel strips to reflect project audio tracks and audio buses.
    /// </summary>
    public void RefreshTracks()
    {
        tracksContainer.Children.Clear();
        busesContainer.Children.Clear();
        trackMeters.Clear();
        trackClipLeds.Clear();
        busMeters.Clear();
        busClipLeds.Clear();

        // 1. Populate Audio Tracks
        var audioTracks = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();
        if (audioTracks.Count == 0)
        {
            tracksContainer.Children.Add(new TextBlock
            {
                Text = "No audio tracks in project.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(24, 40, 24, 0),
                FontStyle = FontStyles.Italic
            });
        }
        else
        {
            int trackIndex = 0;
            foreach (var track in audioTracks)
            {
                trackIndex++;
                var strip = BuildTrackStrip(track, trackIndex);
                tracksContainer.Children.Add(strip);
            }
        }

        // 2. Populate Audio Buses
        var buses = vm.Project.AudioBuses ?? [];
        if (buses.Count > 0)
        {
            foreach (var bus in buses)
            {
                var strip = BuildBusStrip(bus);
                busesContainer.Children.Add(strip);
            }
        }
        else
        {
            busesContainer.Children.Add(new TextBlock
            {
                Text = "No audio buses.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(16, 40, 16, 0),
                FontSize = 10,
                FontStyle = FontStyles.Italic
            });
        }

        // 3. Update Master Bus values
        masterFader.Value = LinearToDb(vm.Project.MasterAudioGain);
        masterFaderText.Text = vm.Project.MasterAudioGain <= 1e-4 ? "-inf dB" : $"{masterFader.Value:+0.0;-0.0;0.0} dB";
        masterMuteBtn.Background = vm.Project.MasterAudioMute ? new SolidColorBrush(Color.FromRgb(180, 40, 40)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
        masterMuteBtn.Foreground = vm.Project.MasterAudioMute ? Brushes.White : Brushes.Silver;
        isDimmed = vm.Project.MasterAudioDim;
        masterDimBtn.Background = isDimmed ? new SolidColorBrush(Color.FromRgb(200, 140, 20)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
        masterDimBtn.Foreground = isDimmed ? Brushes.Black : Brushes.Silver;
    }

    private FrameworkElement BuildTrackStrip(TimelineTrack track, int trackIndex)
    {
        var borderBrush = (Brush)FindResource("BorderBrush");
        var textBrush = (Brush)FindResource("TextBrush");

        var stripBorder = new Border
        {
            Width = 110,
            Margin = new Thickness(2, 4, 2, 4),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2)
        };

        var stripGrid = new Grid { Margin = new Thickness(6) };
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header / Track name
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bus assignment & FX
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Mute, Solo & Phase
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Pan Slider & text
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Fader & meter area
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // dB readout

        // 1. Header
        var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var indexBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 60, 80)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 4, 0)
        };
        indexBadge.Child = new TextBlock { Text = trackIndex.ToString(CultureInfo.InvariantCulture), FontSize = 10, Foreground = Brushes.White, FontWeight = FontWeights.Bold };
        DockPanel.SetDock(indexBadge, Dock.Left);
        headerPanel.Children.Add(indexBadge);

        var trackName = new TextBlock
        {
            Text = track.Name,
            FontSize = 11,
            Foreground = textBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = track.Name
        };
        headerPanel.Children.Add(trackName);
        Grid.SetRow(headerPanel, 0);
        stripGrid.Children.Add(headerPanel);

        // 2. Bus assignment ComboBox & FX/Env buttons
        var busRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var btnGroup = new StackPanel { Orientation = Orientation.Horizontal };
        var fxBtn = new Button
        {
            Content = "fx",
            Width = 22,
            Height = 18,
            FontSize = 10,
            Padding = new Thickness(0),
            ToolTip = "Track Audio FX"
        };
        fxBtn.Click += (_, _) =>
        {
            var win = new AudioTrackFxWindow(vm, track.Id) { Owner = this };
            win.ShowDialog();
        };
        btnGroup.Children.Add(fxBtn);

        var envBtn = new Button
        {
            Content = "env",
            Width = 22,
            Height = 18,
            Margin = new Thickness(2, 0, 0, 0),
            FontSize = 9,
            Padding = new Thickness(0),
            ToolTip = "Track Automation Envelopes"
        };
        envBtn.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            var volItem = new MenuItem { Header = "Volume Envelope", IsCheckable = true, IsChecked = vm.Editor.HasTrackEnvelope(track.Id, EnvelopeType.Volume) };
            volItem.Click += (_, _) => { vm.Editor.ToggleTrackEnvelope(track.Id, EnvelopeType.Volume); onProjectChanged(); RefreshTracks(); };
            menu.Items.Add(volItem);

            var panItem = new MenuItem { Header = "Pan Envelope", IsCheckable = true, IsChecked = vm.Editor.HasTrackEnvelope(track.Id, EnvelopeType.Pan) };
            panItem.Click += (_, _) => { vm.Editor.ToggleTrackEnvelope(track.Id, EnvelopeType.Pan); onProjectChanged(); RefreshTracks(); };
            menu.Items.Add(panItem);

            var muteItem = new MenuItem { Header = "Mute Envelope", IsCheckable = true, IsChecked = vm.Editor.HasTrackEnvelope(track.Id, EnvelopeType.Mute) };
            muteItem.Click += (_, _) => { vm.Editor.ToggleTrackEnvelope(track.Id, EnvelopeType.Mute); onProjectChanged(); RefreshTracks(); };
            menu.Items.Add(muteItem);

            menu.PlacementTarget = envBtn;
            menu.IsOpen = true;
        };
        btnGroup.Children.Add(envBtn);

        DockPanel.SetDock(btnGroup, Dock.Right);
        busRow.Children.Add(btnGroup);

        var busCombo = new ComboBox
        {
            Height = 20,
            FontSize = 9,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Audio Bus Output Routing"
        };
        busCombo.Items.Add(new BusComboItem("Master", null));
        if (vm.Project.AudioBuses != null)
        {
            foreach (var b in vm.Project.AudioBuses)
            {
                busCombo.Items.Add(new BusComboItem(b.Name, b.Id));
            }
        }
        int selectedIndex = 0;
        if (track.BusTrackId.HasValue)
        {
            for (int i = 1; i < busCombo.Items.Count; i++)
            {
                if (busCombo.Items[i] is BusComboItem item && item.BusId == track.BusTrackId.Value)
                {
                    selectedIndex = i;
                    break;
                }
            }
        }
        busCombo.SelectedIndex = selectedIndex;
        busCombo.SelectionChanged += (_, _) =>
        {
            if (busCombo.SelectedItem is BusComboItem item)
            {
                if (track.BusTrackId != item.BusId)
                {
                    vm.Editor.SetTrackAudioBus(track.Id, item.BusId);
                    onProjectChanged();
                }
            }
        };
        busRow.Children.Add(busCombo);
        Grid.SetRow(busRow, 1);
        stripGrid.Children.Add(busRow);

        // 3. Mute, Solo & Phase Invert row
        var muteSoloRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 6) };
        var muteBtn = new Button
        {
            Content = "M",
            Width = 24,
            Height = 20,
            Margin = new Thickness(1, 0, 1, 0),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Background = track.IsMuted ? new SolidColorBrush(Color.FromRgb(180, 40, 40)) : new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = track.IsMuted ? Brushes.White : Brushes.Silver,
            ToolTip = "Mute track"
        };
        muteBtn.Click += (_, _) =>
        {
            vm.Editor.Execute(p =>
            {
                var target = p.Tracks.FirstOrDefault(t => t.Id == track.Id);
                if (target != null) target.IsMuted = !target.IsMuted;
            });
            muteBtn.Background = track.IsMuted ? new SolidColorBrush(Color.FromRgb(180, 40, 40)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
            muteBtn.Foreground = track.IsMuted ? Brushes.White : Brushes.Silver;
            onProjectChanged();
        };

        var soloBtn = new Button
        {
            Content = "S",
            Width = 24,
            Height = 20,
            Margin = new Thickness(1, 0, 1, 0),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Background = track.IsSolo ? new SolidColorBrush(Color.FromRgb(30, 120, 200)) : new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = track.IsSolo ? Brushes.White : Brushes.Silver,
            ToolTip = "Solo track"
        };
        soloBtn.Click += (_, _) =>
        {
            vm.Editor.Execute(p =>
            {
                var target = p.Tracks.FirstOrDefault(t => t.Id == track.Id);
                if (target != null) target.IsSolo = !target.IsSolo;
            });
            soloBtn.Background = track.IsSolo ? new SolidColorBrush(Color.FromRgb(30, 120, 200)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
            soloBtn.Foreground = track.IsSolo ? Brushes.White : Brushes.Silver;
            onProjectChanged();
        };

        var phaseBtn = new Button
        {
            Content = "\u00D8", // Invert Phase symbol Ø
            Width = 24,
            Height = 20,
            Margin = new Thickness(1, 0, 1, 0),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Background = track.InvertPhase ? new SolidColorBrush(Color.FromRgb(255, 140, 0)) : new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = track.InvertPhase ? Brushes.White : Brushes.Silver,
            ToolTip = track.InvertPhase ? "Phase Inverted (180\u00B0)" : "Invert Phase (Normal)"
        };
        phaseBtn.Click += (_, _) =>
        {
            vm.Editor.SetTrackInvertPhase(track.Id, !track.InvertPhase);
            phaseBtn.Background = track.InvertPhase ? new SolidColorBrush(Color.FromRgb(255, 140, 0)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
            phaseBtn.Foreground = track.InvertPhase ? Brushes.White : Brushes.Silver;
            phaseBtn.ToolTip = track.InvertPhase ? "Phase Inverted (180\u00B0)" : "Invert Phase (Normal)";
            onProjectChanged();
        };

        muteSoloRow.Children.Add(muteBtn);
        muteSoloRow.Children.Add(soloBtn);
        muteSoloRow.Children.Add(phaseBtn);
        Grid.SetRow(muteSoloRow, 2);
        stripGrid.Children.Add(muteSoloRow);

        // 4. Pan control & label
        var panPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var panText = new TextBlock
        {
            Text = FormatPan(track.Pan),
            FontSize = 9,
            Foreground = Brushes.Silver,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        var panSlider = new Slider
        {
            Minimum = -1.0,
            Maximum = 1.0,
            Value = track.Pan,
            Height = 18,
            TickFrequency = 0.1,
            ToolTip = "Pan (Left / Right) - Double click to center"
        };
        panSlider.ValueChanged += (_, e) =>
        {
            panText.Text = FormatPan(e.NewValue);
            track.Pan = e.NewValue;
            onProjectChanged();
        };
        panSlider.MouseDoubleClick += (_, _) =>
        {
            panSlider.Value = 0.0;
        };

        panPanel.Children.Add(panText);
        panPanel.Children.Add(panSlider);
        Grid.SetRow(panPanel, 3);
        stripGrid.Children.Add(panPanel);

        // 5. Volume Fader & Peak Meter area
        var faderGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Fader
        faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // dB scale
        faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Meter column

        double currentDb = LinearToDb(track.Gain);
        var fader = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = -60,
            Maximum = 6,
            Value = currentDb,
            Height = 160,
            HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = "Track Volume Fader (-60 to +6 dB) - Double click to 0.0 dB"
        };

        var dbReadout = new TextBlock
        {
            Text = currentDb <= -59.5 ? "-inf dB" : $"{currentDb:+0.0;-0.0;0.0} dB",
            FontSize = 10,
            Foreground = Brushes.Silver,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };

        fader.ValueChanged += (_, e) =>
        {
            double db = e.NewValue;
            dbReadout.Text = db <= -59.5 ? "-inf dB" : $"{db:+0.0;-0.0;0.0} dB";
            track.Gain = DbToLinear(db);
            onProjectChanged();
        };
        fader.MouseDoubleClick += (_, _) => fader.Value = 0.0;

        Grid.SetColumn(fader, 0);
        faderGrid.Children.Add(fader);

        // Scale ticks
        var scaleGrid = new UniformGrid { Rows = 8, Margin = new Thickness(2, 0, 2, 0) };
        string[] scaleLabels = ["+6", "0", "-6", "-12", "-18", "-24", "-36", "-inf"];
        foreach (var lbl in scaleLabels)
        {
            scaleGrid.Children.Add(new TextBlock { Text = lbl, FontSize = 7, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        }
        Grid.SetColumn(scaleGrid, 1);
        faderGrid.Children.Add(scaleGrid);

        // Meter column (Clip LED + Peak Meter Bar)
        var meterCol = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        var clipLed = CreateClipLed();
        trackClipLeds[track.Id] = clipLed;
        meterCol.Children.Add(clipLed);

        var meterBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
            Width = 8,
            Height = 160,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2, 0, 0, 0)
        };
        var meterFill = new Border
        {
            Background = CreateMeterBrush(),
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 0,
            Width = 6
        };
        meterBorder.Child = meterFill;
        trackMeters[track.Id] = meterFill;
        meterCol.Children.Add(meterBorder);

        Grid.SetColumn(meterCol, 2);
        faderGrid.Children.Add(meterCol);

        Grid.SetRow(faderGrid, 4);
        stripGrid.Children.Add(faderGrid);

        // 6. Bottom dB Readout
        Grid.SetRow(dbReadout, 5);
        stripGrid.Children.Add(dbReadout);

        stripBorder.Child = stripGrid;
        return stripBorder;
    }

    private FrameworkElement BuildBusStrip(AudioBusTrack bus)
    {
        var borderBrush = (Brush)FindResource("BorderBrush");
        var textBrush = (Brush)FindResource("TextBrush");

        var stripBorder = new Border
        {
            Width = 110,
            Margin = new Thickness(2, 4, 2, 4),
            Background = new SolidColorBrush(Color.FromRgb(28, 24, 20)), // Vegas Bus warm tint
            BorderBrush = new SolidColorBrush(Color.FromRgb(68, 52, 36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2)
        };

        var stripGrid = new Grid { Margin = new Thickness(6) };
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header / Bus badge & delete
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bus FX button
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Mute & Solo
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Pan Slider & text
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Fader & meter area
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // dB readout

        // 1. Header: Bus badge & Delete button
        var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var delBtn = new Button
        {
            Content = "\u2715", // ✕
            Width = 18,
            Height = 18,
            FontSize = 9,
            Padding = new Thickness(0),
            Foreground = Brushes.Silver,
            ToolTip = $"Delete {bus.Name}"
        };
        delBtn.Click += (_, _) =>
        {
            vm.Editor.DeleteAudioBus(bus.Id);
            onProjectChanged();
            RefreshTracks();
        };
        DockPanel.SetDock(delBtn, Dock.Right);
        headerPanel.Children.Add(delBtn);

        var busBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(200, 122, 20)), // Vegas Bus Gold/Amber
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(6, 1, 6, 1)
        };
        busBadge.Child = new TextBlock
        {
            Text = bus.Name,
            FontSize = 10,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold
        };
        headerPanel.Children.Add(busBadge);
        Grid.SetRow(headerPanel, 0);
        stripGrid.Children.Add(headerPanel);

        // 2. Bus FX & Env buttons
        var fxRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        var fxBtn = new Button
        {
            Content = "fx",
            Width = 28,
            Height = 18,
            FontSize = 10,
            Padding = new Thickness(0),
            ToolTip = $"{bus.Name} Audio FX Chain"
        };
        fxBtn.Click += (_, _) =>
        {
            var win = new AudioTrackFxWindow(vm, bus.Id) { Owner = this };
            win.ShowDialog();
        };
        fxRow.Children.Add(fxBtn);

        var envBtn = new Button
        {
            Content = "env",
            Width = 28,
            Height = 18,
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 9,
            Padding = new Thickness(0),
            ToolTip = $"{bus.Name} Automation Envelopes"
        };
        envBtn.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            var volItem = new MenuItem { Header = "Volume Envelope", IsCheckable = true, IsChecked = vm.Editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Volume) };
            volItem.Click += (_, _) => { vm.Editor.ToggleAudioBusEnvelope(bus.Id, EnvelopeType.Volume); onProjectChanged(); RefreshTracks(); };
            menu.Items.Add(volItem);

            var panItem = new MenuItem { Header = "Pan Envelope", IsCheckable = true, IsChecked = vm.Editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Pan) };
            panItem.Click += (_, _) => { vm.Editor.ToggleAudioBusEnvelope(bus.Id, EnvelopeType.Pan); onProjectChanged(); RefreshTracks(); };
            menu.Items.Add(panItem);

            var muteItem = new MenuItem { Header = "Mute Envelope", IsCheckable = true, IsChecked = vm.Editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Mute) };
            muteItem.Click += (_, _) => { vm.Editor.ToggleAudioBusEnvelope(bus.Id, EnvelopeType.Mute); onProjectChanged(); RefreshTracks(); };
            menu.Items.Add(muteItem);

            menu.PlacementTarget = envBtn;
            menu.IsOpen = true;
        };
        fxRow.Children.Add(envBtn);
        Grid.SetRow(fxRow, 1);
        stripGrid.Children.Add(fxRow);

        // 3. Mute & Solo row
        var muteSoloRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 6) };
        var muteBtn = new Button
        {
            Content = "M",
            Width = 26,
            Height = 20,
            Margin = new Thickness(2, 0, 2, 0),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Background = bus.IsMuted ? new SolidColorBrush(Color.FromRgb(180, 40, 40)) : new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = bus.IsMuted ? Brushes.White : Brushes.Silver,
            ToolTip = "Mute audio bus"
        };
        muteBtn.Click += (_, _) =>
        {
            vm.Editor.SetAudioBusMute(bus.Id, !bus.IsMuted);
            muteBtn.Background = bus.IsMuted ? new SolidColorBrush(Color.FromRgb(180, 40, 40)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
            muteBtn.Foreground = bus.IsMuted ? Brushes.White : Brushes.Silver;
            onProjectChanged();
        };

        var soloBtn = new Button
        {
            Content = "S",
            Width = 26,
            Height = 20,
            Margin = new Thickness(2, 0, 2, 0),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Background = bus.IsSolo ? new SolidColorBrush(Color.FromRgb(30, 120, 200)) : new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = bus.IsSolo ? Brushes.White : Brushes.Silver,
            ToolTip = "Solo audio bus"
        };
        soloBtn.Click += (_, _) =>
        {
            vm.Editor.SetAudioBusSolo(bus.Id, !bus.IsSolo);
            soloBtn.Background = bus.IsSolo ? new SolidColorBrush(Color.FromRgb(30, 120, 200)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
            soloBtn.Foreground = bus.IsSolo ? Brushes.White : Brushes.Silver;
            onProjectChanged();
        };

        muteSoloRow.Children.Add(muteBtn);
        muteSoloRow.Children.Add(soloBtn);
        Grid.SetRow(muteSoloRow, 2);
        stripGrid.Children.Add(muteSoloRow);

        // 4. Pan control & label
        var panPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var panText = new TextBlock
        {
            Text = FormatPan(bus.Pan),
            FontSize = 9,
            Foreground = Brushes.Silver,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        var panSlider = new Slider
        {
            Minimum = -1.0,
            Maximum = 1.0,
            Value = bus.Pan,
            Height = 18,
            TickFrequency = 0.1,
            ToolTip = $"{bus.Name} Pan (Left / Right) - Double click to center"
        };
        panSlider.ValueChanged += (_, e) =>
        {
            panText.Text = FormatPan(e.NewValue);
            vm.Editor.SetAudioBusPan(bus.Id, e.NewValue);
            onProjectChanged();
        };
        panSlider.MouseDoubleClick += (_, _) => panSlider.Value = 0.0;

        panPanel.Children.Add(panText);
        panPanel.Children.Add(panSlider);
        Grid.SetRow(panPanel, 3);
        stripGrid.Children.Add(panPanel);

        // 5. Volume Fader & Peak Meter area
        var faderGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Fader
        faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // dB scale
        faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Meter column

        double currentDb = LinearToDb(bus.Gain);
        var fader = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = -60,
            Maximum = 6,
            Value = currentDb,
            Height = 160,
            HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = $"{bus.Name} Volume Fader (-60 to +6 dB) - Double click to 0.0 dB"
        };

        var dbReadout = new TextBlock
        {
            Text = currentDb <= -59.5 ? "-inf dB" : $"{currentDb:+0.0;-0.0;0.0} dB",
            FontSize = 10,
            Foreground = Brushes.Silver,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };

        fader.ValueChanged += (_, e) =>
        {
            double db = e.NewValue;
            dbReadout.Text = db <= -59.5 ? "-inf dB" : $"{db:+0.0;-0.0;0.0} dB";
            vm.Editor.SetAudioBusVolume(bus.Id, DbToLinear(db));
            onProjectChanged();
        };
        fader.MouseDoubleClick += (_, _) => fader.Value = 0.0;

        Grid.SetColumn(fader, 0);
        faderGrid.Children.Add(fader);

        // Scale ticks
        var scaleGrid = new UniformGrid { Rows = 8, Margin = new Thickness(2, 0, 2, 0) };
        string[] scaleLabels = ["+6", "0", "-6", "-12", "-18", "-24", "-36", "-inf"];
        foreach (var lbl in scaleLabels)
        {
            scaleGrid.Children.Add(new TextBlock { Text = lbl, FontSize = 7, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        }
        Grid.SetColumn(scaleGrid, 1);
        faderGrid.Children.Add(scaleGrid);

        // Meter column (Clip LED + Peak Meter Bar)
        var meterCol = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        var clipLed = CreateClipLed();
        busClipLeds[bus.Id] = clipLed;
        meterCol.Children.Add(clipLed);

        var meterBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
            Width = 8,
            Height = 160,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2, 0, 0, 0)
        };
        var meterFill = new Border
        {
            Background = CreateMeterBrush(),
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 0,
            Width = 6
        };
        meterBorder.Child = meterFill;
        busMeters[bus.Id] = meterFill;
        meterCol.Children.Add(meterBorder);

        Grid.SetColumn(meterCol, 2);
        faderGrid.Children.Add(meterCol);

        Grid.SetRow(faderGrid, 4);
        stripGrid.Children.Add(faderGrid);

        // 6. Bottom dB Readout
        Grid.SetRow(dbReadout, 5);
        stripGrid.Children.Add(dbReadout);

        stripBorder.Child = stripGrid;
        return stripBorder;
    }

    private Border BuildMasterStrip()
    {
        var borderBrush = (Brush)FindResource("BorderBrush");
        var textBrush = (Brush)FindResource("TextBrush");

        var masterBorder = new Border
        {
            Margin = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(28, 30, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(43, 59, 76)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2)
        };

        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Mute & Dim
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Fader & meters
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // dB readout
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Peak L/R

        // 1. Header
        var titleBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 80, 128)), // Vegas Master Navy
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 0, 6)
        };
        titleBadge.Child = new TextBlock
        {
            Text = "Master Bus",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(titleBadge, 0);
        grid.Children.Add(titleBadge);

        // 2. FX, Env, Mute & Dim buttons
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };

        var masterFxBtn = new Button { Content = "fx", Width = 24, Height = 20, Margin = new Thickness(1), ToolTip = "Master Audio FX Chain" };
        masterFxBtn.Click += (_, _) =>
        {
            var win = new AudioTrackFxWindow(vm, Guid.Empty) { Owner = this };
            win.ShowDialog();
        };

        var masterEnvBtn = new Button { Content = "env", Width = 24, Height = 20, Margin = new Thickness(1), ToolTip = "Master Automation Envelopes" };
        masterEnvBtn.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            var volItem = new MenuItem { Header = "Volume Envelope", IsCheckable = true, IsChecked = vm.Editor.HasMasterAudioEnvelope(EnvelopeType.Volume) };
            volItem.Click += (_, _) => { vm.Editor.ToggleMasterAudioEnvelope(EnvelopeType.Volume); onProjectChanged(); RefreshTracks(); };
            menu.Items.Add(volItem);

            var muteItem = new MenuItem { Header = "Mute Envelope", IsCheckable = true, IsChecked = vm.Editor.HasMasterAudioEnvelope(EnvelopeType.Mute) };
            muteItem.Click += (_, _) => { vm.Editor.ToggleMasterAudioEnvelope(EnvelopeType.Mute); onProjectChanged(); RefreshTracks(); };
            menu.Items.Add(muteItem);

            menu.PlacementTarget = masterEnvBtn;
            menu.IsOpen = true;
        };

        masterMuteBtn.Click += (_, _) =>
        {
            vm.Editor.SetMasterAudioMute(!vm.Project.MasterAudioMute);
            masterMuteBtn.Background = vm.Project.MasterAudioMute ? new SolidColorBrush(Color.FromRgb(180, 40, 40)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
            masterMuteBtn.Foreground = vm.Project.MasterAudioMute ? Brushes.White : Brushes.Silver;
            onProjectChanged();
        };

        masterDimBtn.Click += (_, _) =>
        {
            isDimmed = !isDimmed;
            vm.Editor.SetMasterAudioDim(isDimmed);
            masterDimBtn.Background = isDimmed ? new SolidColorBrush(Color.FromRgb(200, 140, 20)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
            masterDimBtn.Foreground = isDimmed ? Brushes.Black : Brushes.Silver;
            onProjectChanged();
        };

        btnRow.Children.Add(masterFxBtn);
        btnRow.Children.Add(masterEnvBtn);
        btnRow.Children.Add(masterMuteBtn);
        btnRow.Children.Add(masterDimBtn);
        Grid.SetRow(btnRow, 1);
        grid.Children.Add(btnRow);

        // 3. Fader & Stereo Meters
        var faderArea = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        faderArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Master fader
        faderArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Scale
        faderArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Stereo meters

        masterFader.ValueChanged += (_, e) =>
        {
            double db = e.NewValue;
            masterFaderText.Text = db <= -59.5 ? "-inf dB" : $"{db:+0.0;-0.0;0.0} dB";
            vm.Editor.SetMasterAudioGain(DbToLinear(db));
            onProjectChanged();
        };
        masterFader.MouseDoubleClick += (_, _) => masterFader.Value = 0.0;

        Grid.SetColumn(masterFader, 0);
        faderArea.Children.Add(masterFader);

        // Scale
        var scaleGrid = new UniformGrid { Rows = 8, Margin = new Thickness(4, 0, 4, 0) };
        string[] scaleLabels = ["+6", "0", "-6", "-12", "-18", "-24", "-36", "-inf"];
        foreach (var lbl in scaleLabels)
        {
            scaleGrid.Children.Add(new TextBlock { Text = lbl, FontSize = 7, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        }
        Grid.SetColumn(scaleGrid, 1);
        faderArea.Children.Add(scaleGrid);

        // Stereo Meter (Left & Right with Clip LEDs)
        var meterContainer = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var meterColL = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(1) };
        meterColL.Children.Add(masterLeftClip);
        var borderL = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)), Width = 8, Height = 160, BorderBrush = borderBrush, BorderThickness = new Thickness(1) };
        borderL.Child = masterLeftMeter;
        meterColL.Children.Add(borderL);

        var meterColR = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(1) };
        meterColR.Children.Add(masterRightClip);
        var borderR = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)), Width = 8, Height = 160, BorderBrush = borderBrush, BorderThickness = new Thickness(1) };
        borderR.Child = masterRightMeter;
        meterColR.Children.Add(borderR);

        meterContainer.Children.Add(meterColL);
        meterContainer.Children.Add(meterColR);

        Grid.SetColumn(meterContainer, 2);
        faderArea.Children.Add(meterContainer);

        Grid.SetRow(faderArea, 2);
        grid.Children.Add(faderArea);

        // 4. Readout
        Grid.SetRow(masterFaderText, 3);
        grid.Children.Add(masterFaderText);

        // 5. Peak L/R
        Grid.SetRow(masterPeakText, 4);
        grid.Children.Add(masterPeakText);

        masterBorder.Child = grid;
        return masterBorder;
    }

    /// <summary>
    /// Updates master, audio bus, and track peak meters with live audio levels during playback.
    /// Calibrated to -60 dB to +6 dB scale matching Vegas Pro master bus meters.
    /// Latches red overload clip LEDs if peak levels exceed 0.0 dB (peak > 1.0).
    /// </summary>
    public void UpdateMeters(double peakL, double peakR)
    {
        static double DbPosition(double peak)
        {
            if (peak <= 0.0001) return 0.0;
            double db = 20.0 * Math.Log10(peak);
            return Math.Clamp((db + 60.0) / 66.0, 0.0, 1.0);
        }

        double posL = DbPosition(peakL);
        double posR = DbPosition(peakR);
        masterLeftMeter.Height = 160.0 * posL;
        masterRightMeter.Height = 160.0 * posR;

        // Latch Master Clip LEDs
        if (peakL > 1.0) masterLeftClip.Background = new SolidColorBrush(Color.FromRgb(255, 32, 32));
        if (peakR > 1.0) masterRightClip.Background = new SolidColorBrush(Color.FromRgb(255, 32, 32));

        if (peakL <= 0.0001 && peakR <= 0.0001)
        {
            masterPeakText.Text = "-inf   -inf";
        }
        else
        {
            string textL = peakL <= 0.0001 ? "-inf" : $"{20.0 * Math.Log10(peakL):+0.0;-0.0;0.0}";
            string textR = peakR <= 0.0001 ? "-inf" : $"{20.0 * Math.Log10(peakR):+0.0;-0.0;0.0}";
            masterPeakText.Text = $"{textL}   {textR}";
        }

        // Dynamically update individual audio track channel strips
        double maxPeak = Math.Max(peakL, peakR);
        bool hasTrackSolo = vm.Project.Tracks.Any(t => t.IsSolo);
        var audioTracks = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();

        foreach (var track in audioTracks)
        {
            if (trackMeters.TryGetValue(track.Id, out var meter))
            {
                if (track.IsMuted || (hasTrackSolo && !track.IsSolo))
                {
                    meter.Height = 0;
                }
                else
                {
                    double trackLevel = maxPeak * track.Gain;
                    meter.Height = 160.0 * DbPosition(trackLevel);

                    if (trackLevel > 1.0 && trackClipLeds.TryGetValue(track.Id, out var clipLed))
                    {
                        clipLed.Background = new SolidColorBrush(Color.FromRgb(255, 32, 32));
                    }
                }
            }
        }

        // Dynamically update audio bus channel strips
        if (vm.Project.AudioBuses != null && vm.Project.AudioBuses.Count > 0)
        {
            bool hasBusSolo = vm.Project.AudioBuses.Any(b => b.IsSolo);
            foreach (var bus in vm.Project.AudioBuses)
            {
                if (busMeters.TryGetValue(bus.Id, out var meter))
                {
                    if (bus.IsMuted || (hasBusSolo && !bus.IsSolo))
                    {
                        meter.Height = 0;
                    }
                    else
                    {
                        // Bus level is driven by assigned tracks feeding this bus multiplied by bus gain
                        var feedingTracks = audioTracks.Where(t => t.BusTrackId == bus.Id && !t.IsMuted && (!hasTrackSolo || t.IsSolo)).ToList();
                        double busInput = feedingTracks.Count > 0 ? feedingTracks.Max(t => maxPeak * t.Gain) : 0.0;
                        double busLevel = busInput * bus.Gain;
                        meter.Height = 160.0 * DbPosition(busLevel);

                        if (busLevel > 1.0 && busClipLeds.TryGetValue(bus.Id, out var clipLed))
                        {
                            clipLed.Background = new SolidColorBrush(Color.FromRgb(255, 32, 32));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resets all peak meters to silent baseline when playback pauses or stops.
    /// (Note: Latching clip LEDs are preserved until clicked or Reset Clips is pressed).
    /// </summary>
    public void ResetMeters()
    {
        masterLeftMeter.Height = 0;
        masterRightMeter.Height = 0;
        masterPeakText.Text = "0.0   0.0";
        foreach (var meter in trackMeters.Values)
        {
            meter.Height = 0;
        }
        foreach (var meter in busMeters.Values)
        {
            meter.Height = 0;
        }
    }

    /// <summary>
    /// Resets all latching clip LEDs across tracks, buses, and master bus.
    /// </summary>
    public void ResetAllClipLeds()
    {
        var dimRed = new SolidColorBrush(Color.FromRgb(48, 16, 16));
        masterLeftClip.Background = dimRed;
        masterRightClip.Background = dimRed;
        foreach (var led in trackClipLeds.Values) led.Background = dimRed;
        foreach (var led in busClipLeds.Values) led.Background = dimRed;
    }

    private static Border CreateClipLed()
    {
        var led = new Border
        {
            Width = 8,
            Height = 6,
            Background = new SolidColorBrush(Color.FromRgb(48, 16, 16)), // Unlit dark red
            BorderBrush = new SolidColorBrush(Color.FromRgb(72, 20, 20)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = Cursors.Hand,
            ToolTip = "Clip Indicator (Click to reset)"
        };
        led.MouseLeftButtonDown += (_, _) => led.Background = new SolidColorBrush(Color.FromRgb(48, 16, 16));
        return led;
    }

    private static Brush CreateMeterBrush()
    {
        var grad = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
        grad.GradientStops.Add(new GradientStop(Color.FromRgb(30, 180, 60), 0.0));
        grad.GradientStops.Add(new GradientStop(Color.FromRgb(180, 200, 30), 0.75));
        grad.GradientStops.Add(new GradientStop(Color.FromRgb(220, 140, 20), 0.88));
        grad.GradientStops.Add(new GradientStop(Color.FromRgb(220, 40, 40), 1.0));
        return grad;
    }

    private static string FormatPan(double pan)
    {
        if (Math.Abs(pan) < 0.01) return "Center";
        if (pan < 0) return $"{Math.Abs(pan) * 100:0}% L";
        return $"{pan * 100:0}% R";
    }

    private static double LinearToDb(double linear)
    {
        if (linear <= 1e-4) return -60.0;
        return Math.Clamp(20.0 * Math.Log10(linear), -60.0, 6.0);
    }

    private static double DbToLinear(double db)
    {
        if (db <= -59.5) return 0.0;
        return Math.Pow(10.0, db / 20.0);
    }
}
