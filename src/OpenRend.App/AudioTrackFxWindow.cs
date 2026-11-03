using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro Audio Plug-In Chain Dialog.
/// Supports Track Audio FX chains and Master Bus FX chains with 4-band Parametric EQ,
/// Track Compressor, and Track Noise Gate, including live curve visualization and factory presets.
/// </summary>
public sealed class AudioTrackFxWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Guid trackId; // Guid.Empty for Master Bus
    private readonly bool isMasterBus;
    private readonly ComboBox presetCombo = new() { Width = 260, Height = 24, Margin = new Thickness(6, 0, 6, 0) };
    private readonly StackPanel chainPanel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
    private readonly ContentControl editorHost = new() { Margin = new Thickness(12) };
    private int selectedEffectIndex;

    public AudioTrackFxWindow(EditorViewModel viewModel, Guid targetTrackId)
    {
        vm = viewModel;
        trackId = targetTrackId;
        isMasterBus = targetTrackId == Guid.Empty;

        var track = !isMasterBus ? vm.Project.Tracks.FirstOrDefault(t => t.Id == trackId) : null;
        var trackName = track?.Name ?? "Audio";
        Title = isMasterBus ? "Audio Master Bus FX - Master" : $"Audio Track FX - {trackName}";
        Width = 740;
        Height = 580;
        MinWidth = 600;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(32, 32, 32));

        // Ensure default chain exists if empty
        EnsureDefaultChain();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Preset bar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Plug-in chain bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Editor panel

        // 1. Preset Bar
        var presetBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6, 8, 6)
        };
        var presetDock = new DockPanel();
        var presetLabel = new TextBlock
        {
            Text = "Preset:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Silver,
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 0)
        };
        presetDock.Children.Add(presetLabel);
        PopulatePresets();
        presetCombo.SelectionChanged += PresetCombo_SelectionChanged;
        presetDock.Children.Add(presetCombo);

        var chainTitle = new TextBlock
        {
            Text = isMasterBus ? "Master FX Chain" : "Track FX Chain",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 180, 240)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Margin = new Thickness(12, 0, 6, 0)
        };
        DockPanel.SetDock(chainTitle, Dock.Right);
        presetDock.Children.Add(chainTitle);
        presetBar.Child = presetDock;
        Grid.SetRow(presetBar, 0);
        root.Children.Add(presetBar);

        // 2. Chain Plug-In Bar
        var chainBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4, 8, 4)
        };
        chainBar.Child = chainPanel;
        Grid.SetRow(chainBar, 1);
        root.Children.Add(chainBar);

        // 3. Editor Host
        Grid.SetRow(editorHost, 2);
        root.Children.Add(editorHost);

        Content = root;
        RebuildChainUI();
    }

    private List<AudioEffect> GetEffects()
    {
        if (isMasterBus) return vm.Project.MasterAudioEffects;
        var track = vm.Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        return track?.AudioEffects ?? [];
    }

    private void EnsureDefaultChain()
    {
        var effects = GetEffects();
        if (effects.Count == 0)
        {
            var defaultChain = AudioEffectCatalog.CreateDefaultTrackChain();
            foreach (var fx in defaultChain)
            {
                if (isMasterBus)
                    vm.Editor.AddMasterAudioEffect(fx);
                else
                    vm.Editor.AddTrackAudioEffect(trackId, fx);
            }
        }
    }

    private void PopulatePresets()
    {
        presetCombo.Items.Clear();
        var current = GetSelectedEffect();
        if (current == null) return;

        var presets = current.Kind switch
        {
            AudioEffectKind.TrackEQ => new[]
            {
                "[Sys] Default",
                "Warm Vocal",
                "Bass Boost (+6 dB)",
                "Treble Boost (+6 dB)",
                "Mid Cut (-4 dB at 1 kHz)",
                "Telephone / Radio Bandpass"
            },
            AudioEffectKind.TrackCompressor => new[]
            {
                "[Sys] Default",
                "Gentle Compression (2:1)",
                "Vocal Leveler (4:1, -18 dB)",
                "Heavy Limiting (10:1)",
                "Snappy Drums (Fast Attack)"
            },
            AudioEffectKind.TrackNoiseGate => new[]
            {
                "[Sys] Default",
                "Strict Gate (-35 dB)",
                "Gentle Ambient Gate (-55 dB)"
            },
            _ => new[] { "[Sys] Default" }
        };

        foreach (var p in presets) presetCombo.Items.Add(p);
        presetCombo.SelectedIndex = 0;
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (presetCombo.SelectedItem is not string preset) return;
        var current = GetSelectedEffect();
        if (current == null) return;

        var templated = current.Kind switch
        {
            AudioEffectKind.TrackEQ => AudioEffectCatalog.CreateTrackEq(preset),
            AudioEffectKind.TrackCompressor => AudioEffectCatalog.CreateTrackCompressor(preset),
            AudioEffectKind.TrackNoiseGate => AudioEffectCatalog.CreateTrackNoiseGate(preset),
            _ => null
        };

        if (templated != null)
        {
            foreach (var (k, v) in templated.Parameters)
            {
                if (isMasterBus)
                    vm.Editor.UpdateMasterAudioEffectParameter(current.Id, k, v);
                else
                    vm.Editor.UpdateTrackAudioEffectParameter(trackId, current.Id, k, v);
            }
            RebuildEditorUI();
        }
    }

    private AudioEffect? GetSelectedEffect()
    {
        var effects = GetEffects();
        if (selectedEffectIndex >= 0 && selectedEffectIndex < effects.Count)
            return effects[selectedEffectIndex];
        return effects.FirstOrDefault();
    }

    private void RebuildChainUI()
    {
        chainPanel.Children.Clear();
        var effects = GetEffects();

        for (int i = 0; i < effects.Count; i++)
        {
            int index = i;
            var fx = effects[i];
            var isSelected = (index == selectedEffectIndex);

            var tabBorder = new Border
            {
                Background = isSelected
                    ? new SolidColorBrush(Color.FromRgb(43, 108, 176)) // Vegas Blue
                    : new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var check = new CheckBox
            {
                IsChecked = !fx.IsBypassed,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Enable/Bypass effect"
            };
            check.Click += (_, _) =>
            {
                var bypassed = !(check.IsChecked ?? false);
                if (isMasterBus)
                    vm.Editor.SetMasterAudioEffectBypassed(fx.Id, bypassed);
                else
                    vm.Editor.SetTrackAudioEffectBypassed(trackId, fx.Id, bypassed);
            };

            var nameText = new TextBlock
            {
                Text = fx.Name,
                Foreground = Brushes.White,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };

            sp.Children.Add(check);
            sp.Children.Add(nameText);
            tabBorder.Child = sp;

            tabBorder.MouseLeftButtonDown += (_, _) =>
            {
                selectedEffectIndex = index;
                PopulatePresets();
                RebuildChainUI();
            };

            chainPanel.Children.Add(tabBorder);
        }

        RebuildEditorUI();
    }

    private void RebuildEditorUI()
    {
        var fx = GetSelectedEffect();
        if (fx == null)
        {
            editorHost.Content = new TextBlock
            {
                Text = "No plug-in selected in chain.",
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return;
        }

        editorHost.Content = fx.Kind switch
        {
            AudioEffectKind.TrackEQ => BuildTrackEqEditor(fx),
            AudioEffectKind.TrackCompressor => BuildTrackCompressorEditor(fx),
            AudioEffectKind.TrackNoiseGate => BuildTrackNoiseGateEditor(fx),
            _ => new TextBlock { Text = $"{fx.Name} parameter controls", Foreground = Brushes.Silver }
        };
    }

    private FrameworkElement BuildTrackEqEditor(AudioEffect fx)
    {
        var root = new Grid { Margin = new Thickness(4) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) }); // Frequency response graph
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4 Band controls

        // 1. Response curve canvas
        var canvasBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(16, 18, 22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var graph = new Canvas { ClipToBounds = true };
        DrawEqCurve(graph, fx.Parameters);
        canvasBorder.Child = graph;
        Grid.SetRow(canvasBorder, 0);
        root.Children.Add(canvasBorder);

        // 2. 4 Band Controls (Low Shelf, Mid Peaking 1, Mid Peaking 2, High Shelf)
        var bandsGrid = new Grid();
        for (int b = 0; b < 4; b++)
            bandsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bandsGrid.Children.Add(BuildEqBandStrip(fx, 1, "Band 1: Low Shelf", 80, graph));
        bandsGrid.Children.Add(BuildEqBandStrip(fx, 2, "Band 2: Peaking", 250, graph));
        bandsGrid.Children.Add(BuildEqBandStrip(fx, 3, "Band 3: Peaking", 2500, graph));
        bandsGrid.Children.Add(BuildEqBandStrip(fx, 4, "Band 4: High Shelf", 8000, graph));

        for (int i = 0; i < 4; i++) Grid.SetColumn((UIElement)bandsGrid.Children[i], i);

        Grid.SetRow(bandsGrid, 1);
        root.Children.Add(bandsGrid);
        return root;
    }

    private FrameworkElement BuildEqBandStrip(AudioEffect fx, int band, string label, double defaultFreq, Canvas graph)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2),
            Padding = new Thickness(6)
        };

        var sp = new StackPanel();
        var header = new TextBlock { Text = label, FontWeight = FontWeights.Bold, FontSize = 10, Foreground = Brushes.Silver, Margin = new Thickness(0, 0, 0, 6) };
        sp.Children.Add(header);

        var enabledCheck = new CheckBox { Content = "Enabled", IsChecked = fx.Parameters.GetValueOrDefault($"Band{band}_Enabled", 1.0) > 0.5, Foreground = Brushes.Silver, FontSize = 10, Margin = new Thickness(0, 0, 0, 6) };
        enabledCheck.Click += (_, _) =>
        {
            double val = (enabledCheck.IsChecked ?? false) ? 1.0 : 0.0;
            UpdateParam(fx, $"Band{band}_Enabled", val);
            DrawEqCurve(graph, fx.Parameters);
        };
        sp.Children.Add(enabledCheck);

        // Frequency Slider
        var curFreq = fx.Parameters.GetValueOrDefault($"Band{band}_Freq", defaultFreq);
        var freqText = new TextBlock { Text = $"Freq: {curFreq:0} Hz", FontSize = 10, Foreground = Brushes.Gray };
        var freqSlider = new Slider { Minimum = 20, Maximum = 20000, Value = curFreq, Margin = new Thickness(0, 2, 0, 6) };
        freqSlider.ValueChanged += (_, _) =>
        {
            freqText.Text = $"Freq: {freqSlider.Value:0} Hz";
            UpdateParam(fx, $"Band{band}_Freq", freqSlider.Value);
            DrawEqCurve(graph, fx.Parameters);
        };
        sp.Children.Add(freqText);
        sp.Children.Add(freqSlider);

        // Gain Slider
        var curGain = fx.Parameters.GetValueOrDefault($"Band{band}_GainDb", 0.0);
        var gainText = new TextBlock { Text = $"Gain: {curGain:+0.0;-0.0;0.0} dB", FontSize = 10, Foreground = Brushes.Gray };
        var gainSlider = new Slider { Minimum = -24, Maximum = 24, Value = curGain, Margin = new Thickness(0, 2, 0, 6) };
        gainSlider.ValueChanged += (_, _) =>
        {
            gainText.Text = $"Gain: {gainSlider.Value:+0.0;-0.0;0.0} dB";
            UpdateParam(fx, $"Band{band}_GainDb", gainSlider.Value);
            DrawEqCurve(graph, fx.Parameters);
        };
        sp.Children.Add(gainText);
        sp.Children.Add(gainSlider);

        // Q Slider
        var curQ = fx.Parameters.GetValueOrDefault($"Band{band}_Q", 1.0);
        var qText = new TextBlock { Text = $"Q: {curQ:0.00}", FontSize = 10, Foreground = Brushes.Gray };
        var qSlider = new Slider { Minimum = 0.1, Maximum = 10.0, Value = curQ, Margin = new Thickness(0, 2, 0, 6) };
        qSlider.ValueChanged += (_, _) =>
        {
            qText.Text = $"Q: {qSlider.Value:0.00}";
            UpdateParam(fx, $"Band{band}_Q", qSlider.Value);
            DrawEqCurve(graph, fx.Parameters);
        };
        sp.Children.Add(qText);
        sp.Children.Add(qSlider);

        border.Child = sp;
        return border;
    }

    private static void DrawEqCurve(Canvas canvas, IReadOnlyDictionary<string, double> parameters)
    {
        canvas.Children.Clear();
        double w = 700;
        double h = 138;
        double zeroY = h / 2.0;

        // Draw 0 dB baseline
        canvas.Children.Add(new Line
        {
            X1 = 0, Y1 = zeroY, X2 = w, Y2 = zeroY,
            Stroke = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            StrokeThickness = 1,
            StrokeDashArray = [4, 4]
        });

        // Plot composite frequency response
        var poly = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 210, 255)), // Vegas Cyan
            StrokeThickness = 2.0
        };

        const int points = 120;
        for (int i = 0; i < points; i++)
        {
            double frac = (double)i / (points - 1);
            // Logarithmic frequency from 20 Hz to 20 kHz
            double freq = 20.0 * Math.Pow(1000.0, frac);

            // Compute dB gain sum for the 4 bands
            double totalDb = 0.0;
            for (int band = 1; band <= 4; band++)
            {
                if (parameters.GetValueOrDefault($"Band{band}_Enabled", 1.0) <= 0.5) continue;
                double bGain = parameters.GetValueOrDefault($"Band{band}_GainDb", 0.0);
                double bFreq = parameters.GetValueOrDefault($"Band{band}_Freq", 1000.0);
                double bQ = Math.Max(0.1, parameters.GetValueOrDefault($"Band{band}_Q", 1.0));

                double diff = Math.Log10(freq / bFreq);
                // Gaussian bell curve approximation for preview visualization
                double response = Math.Exp(-0.5 * Math.Pow(diff * (bQ * 2.5), 2.0));
                totalDb += bGain * response;
            }

            double y = zeroY - (totalDb / 24.0) * (h * 0.45);
            y = Math.Clamp(y, 4, h - 4);
            poly.Points.Add(new Point(frac * w, y));
        }

        canvas.Children.Add(poly);
    }

    private FrameworkElement BuildTrackCompressorEditor(AudioEffect fx)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(BuildSliderRow("Threshold (dB)", -60, 0, fx.Parameters.GetValueOrDefault("ThresholdDb", -18.0), "dB", v => UpdateParam(fx, "ThresholdDb", v)));
        panel.Children.Add(BuildSliderRow("Ratio (x:1)", 1.0, 30.0, fx.Parameters.GetValueOrDefault("Ratio", 4.0), ":1", v => UpdateParam(fx, "Ratio", v)));
        panel.Children.Add(BuildSliderRow("Attack (ms)", 0.1, 500.0, fx.Parameters.GetValueOrDefault("AttackMs", 15.0), "ms", v => UpdateParam(fx, "AttackMs", v)));
        panel.Children.Add(BuildSliderRow("Release (ms)", 10.0, 2500.0, fx.Parameters.GetValueOrDefault("ReleaseMs", 100.0), "ms", v => UpdateParam(fx, "ReleaseMs", v)));
        panel.Children.Add(BuildSliderRow("Output Gain (dB)", -24.0, 24.0, fx.Parameters.GetValueOrDefault("OutputGainDb", 0.0), "dB", v => UpdateParam(fx, "OutputGainDb", v)));

        return panel;
    }

    private FrameworkElement BuildTrackNoiseGateEditor(AudioEffect fx)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(BuildSliderRow("Threshold (dB)", -90, -10, fx.Parameters.GetValueOrDefault("ThresholdDb", -45.0), "dB", v => UpdateParam(fx, "ThresholdDb", v)));
        panel.Children.Add(BuildSliderRow("Attack (ms)", 0.1, 50.0, fx.Parameters.GetValueOrDefault("AttackMs", 5.0), "ms", v => UpdateParam(fx, "AttackMs", v)));
        panel.Children.Add(BuildSliderRow("Release (ms)", 10.0, 1000.0, fx.Parameters.GetValueOrDefault("ReleaseMs", 150.0), "ms", v => UpdateParam(fx, "ReleaseMs", v)));
        panel.Children.Add(BuildSliderRow("Damping (dB)", -60.0, 0.0, fx.Parameters.GetValueOrDefault("DampingDb", -60.0), "dB", v => UpdateParam(fx, "DampingDb", v)));

        return panel;
    }

    private static FrameworkElement BuildSliderRow(string title, double min, double max, double cur, string unit, Action<double> onChange)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var label = new TextBlock { Text = title, Foreground = Brushes.Silver, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        var readout = new TextBlock { Text = $"{cur:0.0} {unit}", Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var slider = new Slider { Minimum = min, Maximum = max, Value = cur, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };

        slider.ValueChanged += (_, _) =>
        {
            readout.Text = $"{slider.Value:0.0} {unit}";
            onChange(slider.Value);
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(readout, 2);

        grid.Children.Add(label);
        grid.Children.Add(slider);
        grid.Children.Add(readout);
        return grid;
    }

    private void UpdateParam(AudioEffect fx, string param, double value)
    {
        if (isMasterBus)
            vm.Editor.UpdateMasterAudioEffectParameter(fx.Id, param, value);
        else
            vm.Editor.UpdateTrackAudioEffectParameter(trackId, fx.Id, param, value);
    }
}
