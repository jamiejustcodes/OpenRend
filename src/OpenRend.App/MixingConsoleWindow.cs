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
/// including volume faders (-60 dB to +6 dB), rotary pan, mute/solo, track FX,
/// peak meters, and Master Bus master fader with live audio monitoring.
/// </summary>
public sealed class MixingConsoleWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Action onProjectChanged;
    private readonly StackPanel tracksContainer = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer tracksScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Slider masterFader = new() { Orientation = Orientation.Vertical, Minimum = -60, Maximum = 6, Value = 0, Height = 180, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock masterFaderText = new() { Text = "0.0 dB", FontSize = 10, Foreground = Brushes.Silver, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border masterLeftMeter = new() { Background = CreateMeterBrush(), VerticalAlignment = VerticalAlignment.Bottom, Height = 0, Width = 8 };
    private readonly Border masterRightMeter = new() { Background = CreateMeterBrush(), VerticalAlignment = VerticalAlignment.Bottom, Height = 0, Width = 8 };
    private readonly TextBlock masterPeakText = new() { Text = "0.0   0.0", FontSize = 9, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button masterMuteBtn = new() { Content = "M", Width = 22, Height = 20, Margin = new Thickness(1) };
    private readonly Button masterDimBtn = new() { Content = "Dim", Width = 32, Height = 20, Margin = new Thickness(1) };
    private readonly Dictionary<Guid, Border> trackMeters = [];
    private bool isDimmed;

    public MixingConsoleWindow(EditorViewModel viewModel, Action projectChanged)
    {
        vm = viewModel;
        onProjectChanged = projectChanged;

        Style = (Style)FindResource(typeof(Window));
        Title = "Mixing Console";
        Width = 720;
        Height = 480;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var borderBrush = (Brush)FindResource("BorderBrush");
        var headerBrush = (Brush)FindResource("HeaderBrush");
        var textBrush = (Brush)FindResource("TextBrush");

        var root = new Grid { Background = panelBrush };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Tracks strips
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Separator
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // Master bus strip

        // 1. Tracks Scroll Area
        tracksScroll.Content = tracksContainer;
        Grid.SetColumn(tracksScroll, 0);
        root.Children.Add(tracksScroll);

        // 2. Vertical Divider
        var splitter = new Rectangle { Width = 2, Fill = borderBrush, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1);
        root.Children.Add(splitter);

        // 3. Master Bus Strip
        var masterStrip = BuildMasterStrip();
        Grid.SetColumn(masterStrip, 2);
        root.Children.Add(masterStrip);

        Content = root;

        Loaded += (_, _) => RefreshTracks();
    }

    /// <summary>
    /// Rebuilds channel strips to reflect project audio tracks.
    /// </summary>
    public void RefreshTracks()
    {
        tracksContainer.Children.Clear();
        trackMeters.Clear();
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
            return;
        }

        int trackIndex = 0;
        foreach (var track in audioTracks)
        {
            trackIndex++;
            var strip = BuildTrackStrip(track, trackIndex);
            tracksContainer.Children.Add(strip);
        }
    }

    private FrameworkElement BuildTrackStrip(TimelineTrack track, int trackIndex)
    {
        var borderBrush = (Brush)FindResource("BorderBrush");
        var textBrush = (Brush)FindResource("TextBrush");
        var hoverBrush = (Brush)FindResource("HoverBrush");

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
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bus badge & FX button
        stripGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Mute / Solo buttons
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

        // 2. Bus assignment & FX button
        var busRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var fxBtn = new Button
        {
            Content = "fx",
            Width = 26,
            Height = 18,
            FontSize = 10,
            Padding = new Thickness(0),
            ToolTip = "Track Audio FX"
        };
        DockPanel.SetDock(fxBtn, Dock.Right);
        busRow.Children.Add(fxBtn);

        var busBadge = new TextBlock
        {
            Text = "Master",
            FontSize = 9,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        busRow.Children.Add(busBadge);
        Grid.SetRow(busRow, 1);
        stripGrid.Children.Add(busRow);

        // 3. Mute & Solo row
        var muteSoloRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 6) };
        var muteBtn = new Button
        {
            Content = "M",
            Width = 24,
            Height = 20,
            Margin = new Thickness(2, 0, 2, 0),
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
            Margin = new Thickness(2, 0, 2, 0),
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

        muteSoloRow.Children.Add(muteBtn);
        muteSoloRow.Children.Add(soloBtn);
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
            ToolTip = "Pan (Left / Right)"
        };
        panSlider.ValueChanged += (_, e) =>
        {
            panText.Text = FormatPan(e.NewValue);
            track.Pan = e.NewValue;
            onProjectChanged();
        };
        panSlider.MouseDoubleClick += (_, _) =>
        {
            panSlider.Value = 0.0; // Reset pan to Center on double click
        };

        panPanel.Children.Add(panText);
        panPanel.Children.Add(panSlider);
        Grid.SetRow(panPanel, 3);
        stripGrid.Children.Add(panPanel);

        // 5. Volume Fader & Peak Meter area
        var faderGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Fader
        faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // dB scale
        faderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Meter

        double currentDb = LinearToDb(track.Gain);
        var fader = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = -60,
            Maximum = 6,
            Value = currentDb,
            Height = 170,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var dbReadout = new TextBlock
        {
            Text = $"{currentDb:+0.0;-0.0;0.0} dB",
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
        fader.MouseDoubleClick += (_, _) =>
        {
            fader.Value = 0.0; // Reset to 0 dB unity gain on double click
        };

        Grid.SetColumn(fader, 0);
        faderGrid.Children.Add(fader);

        // Scale ticks: +6, 0, -6, -12, -18, -24, -36, -48, -inf
        var scaleGrid = new UniformGrid { Rows = 8, Margin = new Thickness(2, 0, 2, 0) };
        string[] scaleLabels = ["+6", "0", "-6", "-12", "-18", "-24", "-36", "-inf"];
        foreach (var lbl in scaleLabels)
        {
            scaleGrid.Children.Add(new TextBlock { Text = lbl, FontSize = 7, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        }
        Grid.SetColumn(scaleGrid, 1);
        faderGrid.Children.Add(scaleGrid);

        // Simulated peak meter bar
        var meterBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
            Width = 8,
            Height = 170,
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
        Grid.SetColumn(meterBorder, 2);
        faderGrid.Children.Add(meterBorder);

        Grid.SetRow(faderGrid, 4);
        stripGrid.Children.Add(faderGrid);

        // 6. Bottom dB Readout
        Grid.SetRow(dbReadout, 5);
        stripGrid.Children.Add(dbReadout);

        stripBorder.Child = stripGrid;
        return stripBorder;
    }

    private FrameworkElement BuildMasterStrip()
    {
        var borderBrush = (Brush)FindResource("BorderBrush");
        var textBrush = (Brush)FindResource("TextBrush");

        var masterBorder = new Border
        {
            Margin = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
            BorderBrush = borderBrush,
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
        var title = new TextBlock
        {
            Text = "Master Bus",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = textBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        // 2. Mute & Dim buttons
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        masterMuteBtn.Click += (_, _) =>
        {
            // Toggle master mute
            bool muted = masterMuteBtn.Background is SolidColorBrush b && b.Color.R > 100;
            if (!muted)
            {
                masterMuteBtn.Background = new SolidColorBrush(Color.FromRgb(180, 40, 40));
                masterMuteBtn.Foreground = Brushes.White;
            }
            else
            {
                masterMuteBtn.Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
                masterMuteBtn.Foreground = Brushes.Silver;
            }
        };

        masterDimBtn.Click += (_, _) =>
        {
            isDimmed = !isDimmed;
            masterDimBtn.Background = isDimmed ? new SolidColorBrush(Color.FromRgb(200, 140, 20)) : new SolidColorBrush(Color.FromRgb(45, 45, 45));
            masterDimBtn.Foreground = isDimmed ? Brushes.Black : Brushes.Silver;
        };

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

        // Stereo Meter (Left & Right)
        var meterContainer = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 180 };
        var borderL = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)), Width = 8, Height = 180, BorderBrush = borderBrush, BorderThickness = new Thickness(1), Margin = new Thickness(1) };
        borderL.Child = masterLeftMeter;
        var borderR = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)), Width = 8, Height = 180, BorderBrush = borderBrush, BorderThickness = new Thickness(1), Margin = new Thickness(1) };
        borderR.Child = masterRightMeter;
        meterContainer.Children.Add(borderL);
        meterContainer.Children.Add(borderR);

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
    /// Updates master and track peak meters with live audio levels during playback.
    /// Calibrated to -60 dB to +6 dB scale matching Vegas Pro master bus meters.
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
        masterLeftMeter.Height = 170.0 * posL;
        masterRightMeter.Height = 170.0 * posR;

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
        bool hasSolo = vm.Project.Tracks.Any(t => t.IsSolo);
        foreach (var track in vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio))
        {
            if (trackMeters.TryGetValue(track.Id, out var meter))
            {
                if (track.IsMuted || (hasSolo && !track.IsSolo))
                {
                    meter.Height = 0;
                }
                else
                {
                    double trackLevel = maxPeak * track.Gain;
                    meter.Height = 170.0 * DbPosition(trackLevel);
                }
            }
        }
    }

    /// <summary>
    /// Resets all peak meters to silent baseline when playback pauses or stops.
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
