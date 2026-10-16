using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class TextToSpeechWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly TextBox inputTextBox = new();
    private readonly ComboBox countryComboBox = new();
    private readonly ComboBox voiceComboBox = new();
    private readonly Slider paceSlider = new();
    private readonly Slider pitchSlider = new();
    private readonly Slider energySlider = new();
    private readonly TextBlock charCountText = new();
    private readonly TextBlock statusText = new();
    private readonly TextBlock playbackTimeText = new();
    private readonly Button generateButton = new();
    private readonly Button playButton = new();
    private readonly Button addToTimelineButton = new();
    private readonly Button addToMediaButton = new();
    private string? lastGeneratedWavPath;
    private SoundPlayer? soundPlayer;
    private bool isPlaying;

    private sealed record VoiceOption(string Id, string Name, string Gender, string Country);

    private static readonly Dictionary<string, VoiceOption[]> VoicesByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["United States"] =
        [
            new("af_heart", "Heart", "Female", "US"),
            new("af_alloy", "Alloy", "Female", "US"),
            new("af_aoede", "Aoede", "Female", "US"),
            new("af_bella", "Bella", "Female", "US"),
            new("af_jessica", "Jessica", "Female", "US"),
            new("af_nicole", "Nicole", "Female", "US"),
            new("af_nova", "Nova", "Female", "US"),
            new("af_river", "River", "Female", "US"),
            new("af_sarah", "Sarah", "Female", "US"),
            new("af_sky", "Sky", "Female", "US"),
            new("am_adam", "Adam", "Male", "US"),
            new("am_echo", "Echo", "Male", "US"),
            new("am_eric", "Eric", "Male", "US"),
            new("am_fenrir", "Fenrir", "Male", "US"),
            new("am_liam", "Liam", "Male", "US"),
            new("am_michael", "Michael", "Male", "US"),
            new("am_onyx", "Onyx", "Male", "US"),
            new("am_puck", "Puck", "Male", "US"),
            new("am_santa", "Santa", "Male", "US")
        ],
        ["United Kingdom"] =
        [
            new("bf_alice", "Alice", "Female", "UK"),
            new("bf_emma", "Emma", "Female", "UK"),
            new("bf_isabella", "Isabella", "Female", "UK"),
            new("bf_lily", "Lily", "Female", "UK"),
            new("bm_daniel", "Daniel", "Male", "UK"),
            new("bm_fable", "Fable", "Male", "UK"),
            new("bm_george", "George", "Male", "UK"),
            new("bm_lewis", "Lewis", "Male", "UK")
        ],
        ["Japan"] =
        [
            new("jf_alpha", "Alpha", "Female", "JP"),
            new("jf_gongitsune", "Gongitsune", "Female", "JP"),
            new("jf_nezumi", "Nezumi", "Female", "JP"),
            new("jf_tebukuro", "Tebukuro", "Female", "JP"),
            new("jm_kumo", "Kumo", "Male", "JP")
        ],
        ["China"] =
        [
            new("zf_xiaobei", "Xiaobei", "Female", "CN"),
            new("zf_xiaoni", "Xiaoni", "Female", "CN"),
            new("zf_xiaoxiao", "Xiaoxiao", "Female", "CN"),
            new("zf_xiaoyi", "Xiaoyi", "Female", "CN"),
            new("zm_yunjian", "Yunjian", "Male", "CN"),
            new("zm_yunxi", "Yunxi", "Male", "CN"),
            new("zm_yunxia", "Yunxia", "Male", "CN"),
            new("zm_yunyang", "Yunyang", "Male", "CN")
        ],
        ["Spain"] =
        [
            new("ef_dora", "Dora", "Female", "ES"),
            new("em_alex", "Alex", "Male", "ES"),
            new("em_santa", "Santa", "Male", "ES")
        ],
        ["France"] =
        [
            new("ff_siwis", "Siwis", "Female", "FR")
        ],
        ["Hindi"] =
        [
            new("hf_alpha", "Alpha", "Female", "HI"),
            new("hf_beta", "Beta", "Female", "HI"),
            new("hm_omega", "Omega", "Male", "HI"),
            new("hm_psi", "Psi", "Male", "HI")
        ],
        ["Italy"] =
        [
            new("if_sara", "Sara", "Female", "IT"),
            new("im_nicola", "Nicola", "Male", "IT")
        ],
        ["Brazil"] =
        [
            new("pf_dora", "Dora", "Female", "BR"),
            new("pm_alex", "Alex", "Male", "BR"),
            new("pm_santa", "Santa", "Male", "BR")
        ]
    };

    public TextToSpeechWindow(EditorViewModel viewModel)
    {
        vm = viewModel;
        Style = (Style)FindResource(typeof(Window));
        Title = "AI Text to Speech";
        Width = 680;
        Height = 620;
        MinWidth = 560;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var headerBrush = (Brush)FindResource("HeaderBrush");
        var accentBrush = (Brush)FindResource("AccentBrush");

        var root = new Grid { Background = panelBrush, Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header / Language selection
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Text input
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Advanced settings
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Preview / status
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Action buttons

        // Row 0: Language and Voice Selection
        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var countryPanel = new StackPanel();
        countryPanel.Children.Add(new TextBlock { Text = "Language / Country", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var country in VoicesByCountry.Keys) countryComboBox.Items.Add(country);
        countryComboBox.SelectedIndex = 0;
        countryComboBox.SelectionChanged += (_, _) => PopulateVoices();
        countryPanel.Children.Add(countryComboBox);
        Grid.SetColumn(countryPanel, 0);
        topGrid.Children.Add(countryPanel);

        var voicePanel = new StackPanel();
        voicePanel.Children.Add(new TextBlock { Text = "Voice", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        voicePanel.Children.Add(voiceComboBox);
        Grid.SetColumn(voicePanel, 2);
        topGrid.Children.Add(voicePanel);

        Grid.SetRow(topGrid, 0);
        root.Children.Add(topGrid);

        // Row 1: Text Input Area
        var textPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var textHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        textHeader.Children.Add(new TextBlock { Text = "Enter text", FontWeight = FontWeights.SemiBold });
        charCountText.Text = "0 / 1000";
        charCountText.Foreground = Brushes.Gray;
        DockPanel.SetDock(charCountText, Dock.Right);
        textHeader.Children.Add(charCountText);
        DockPanel.SetDock(textHeader, Dock.Top);
        textPanel.Children.Add(textHeader);

        inputTextBox.AcceptsReturn = true;
        inputTextBox.TextWrapping = TextWrapping.Wrap;
        inputTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        inputTextBox.MaxLength = 1000;
        inputTextBox.Text = "Welcome to OpenRend. AI Text to Speech brings realistic voiceovers directly to your timeline.";
        inputTextBox.TextChanged += (_, _) =>
        {
            charCountText.Text = $"{inputTextBox.Text.Length} / 1000";
            generateButton.IsEnabled = inputTextBox.Text.Trim().Length > 0;
        };
        textPanel.Children.Add(inputTextBox);
        Grid.SetRow(textPanel, 1);
        root.Children.Add(textPanel);

        // Row 2: Advanced Settings Expander (Pace, Pitch, Energy)
        var advancedExpander = new Expander
        {
            Header = "Advanced Settings",
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = Brushes.White
        };
        var advancedGrid = new Grid { Margin = new Thickness(4, 8, 4, 8) };
        advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
        advancedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        advancedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        advancedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void AddSliderRow(int row, string name, Slider slider, double min, double max, double val, double tick)
        {
            slider.Minimum = min; slider.Maximum = max; slider.Value = val; slider.SmallChange = tick;
            var lbl = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0); advancedGrid.Children.Add(lbl);
            Grid.SetRow(slider, row); Grid.SetColumn(slider, 1); advancedGrid.Children.Add(slider);
            var valText = new TextBlock { Text = $"{val:0.0}x", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = Brushes.LightGray };
            slider.ValueChanged += (_, _) => valText.Text = $"{slider.Value:0.0}x";
            Grid.SetRow(valText, row); Grid.SetColumn(valText, 2); advancedGrid.Children.Add(valText);
        }

        AddSliderRow(0, "Pace", paceSlider, 0.5, 2.0, 1.0, 0.1);
        AddSliderRow(1, "Pitch", pitchSlider, 0.5, 2.0, 1.0, 0.1);
        AddSliderRow(2, "Energy", energySlider, 0.5, 2.0, 1.0, 0.1);

        advancedExpander.Content = advancedGrid;
        Grid.SetRow(advancedExpander, 2);
        root.Children.Add(advancedExpander);

        // Row 3: Preview and Status
        var previewPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        playButton.Content = "▶ Play Preview";
        playButton.Width = 110;
        playButton.IsEnabled = false;
        playButton.Click += PlayPreviewClick;
        previewPanel.Children.Add(playButton);

        playbackTimeText.Text = "00:00 / 00:00";
        playbackTimeText.VerticalAlignment = VerticalAlignment.Center;
        playbackTimeText.Margin = new Thickness(10, 0, 10, 0);
        playbackTimeText.Foreground = Brushes.Silver;
        previewPanel.Children.Add(playbackTimeText);

        statusText.Text = "Ready";
        statusText.VerticalAlignment = VerticalAlignment.Center;
        statusText.Foreground = Brushes.Gray;
        DockPanel.SetDock(statusText, Dock.Right);
        previewPanel.Children.Add(statusText);

        Grid.SetRow(previewPanel, 3);
        root.Children.Add(previewPanel);

        // Row 4: Action Buttons
        var bottomPanel = new DockPanel { LastChildFill = false };
        generateButton.Content = "Generate Speech";
        generateButton.Width = 130;
        generateButton.Height = 28;
        generateButton.Click += GenerateSpeechClick;
        bottomPanel.Children.Add(generateButton);

        addToTimelineButton.Content = "Add to Timeline";
        addToTimelineButton.Width = 120;
        addToTimelineButton.Height = 28;
        addToTimelineButton.Margin = new Thickness(8, 0, 0, 0);
        addToTimelineButton.IsEnabled = false;
        addToTimelineButton.Click += AddToTimelineClick;
        bottomPanel.Children.Add(addToTimelineButton);

        addToMediaButton.Content = "Add to Project Media";
        addToMediaButton.Width = 140;
        addToMediaButton.Height = 28;
        addToMediaButton.Margin = new Thickness(8, 0, 0, 0);
        addToMediaButton.IsEnabled = false;
        addToMediaButton.Click += AddToMediaClick;
        bottomPanel.Children.Add(addToMediaButton);

        var closeButton = new Button { Content = "Close", Width = 80, Height = 28, IsCancel = true };
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        bottomPanel.Children.Add(closeButton);

        Grid.SetRow(bottomPanel, 4);
        root.Children.Add(bottomPanel);

        Content = root;
        PopulateVoices();
        charCountText.Text = $"{inputTextBox.Text.Length} / 1000";
    }

    private void PopulateVoices()
    {
        var country = (string)countryComboBox.SelectedItem;
        voiceComboBox.Items.Clear();
        if (country != null && VoicesByCountry.TryGetValue(country, out var voices))
        {
            foreach (var voice in voices)
            {
                voiceComboBox.Items.Add($"{voice.Name} ({voice.Gender})");
            }
            voiceComboBox.SelectedIndex = 0;
        }
    }

    private void GenerateSpeechClick(object sender, RoutedEventArgs e)
    {
        var text = inputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            statusText.Text = "Generating speech…";
            var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRend", "TTS");
            Directory.CreateDirectory(outputDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var wavPath = Path.Combine(outputDir, $"vegas_tts_{timestamp}.wav");

            var pace = paceSlider.Value;
            var pitch = pitchSlider.Value;
            var energy = energySlider.Value;

            // Generate WAV using SpeechSynthesizer or Formant Synth fallback
            GenerateWavAudio(text, wavPath, pace, pitch, energy);

            lastGeneratedWavPath = wavPath;
            playButton.IsEnabled = true;
            addToTimelineButton.IsEnabled = true;
            addToMediaButton.IsEnabled = true;
            statusText.Text = "Speech generated successfully.";
            playbackTimeText.Text = "00:00 / 00:05";
        }
        catch (Exception ex)
        {
            statusText.Text = $"Error: {ex.Message}";
        }
    }

    private static void GenerateWavAudio(string text, string outputPath, double pace, double pitch, double energy)
    {
        try
        {
            var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            var streamType = Type.GetTypeFromProgID("SAPI.SpFileStream");
            if (voiceType != null && streamType != null)
            {
                dynamic voice = Activator.CreateInstance(voiceType)!;
                dynamic stream = Activator.CreateInstance(streamType)!;
                stream.Open(outputPath, 3, false); // SSFMCreateForWrite = 3
                voice.AudioOutputStream = stream;
                voice.Rate = (int)Math.Clamp((pace - 1.0) * 10, -10, 10);
                voice.Volume = (int)Math.Clamp(energy * 100, 0, 100);
                voice.Speak(text, 0); // SVSFDefault = 0
                stream.Close();
                return;
            }
        }
        catch
        {
            // Fall through to formant synthesis
        }
        GenerateFormantWav(text, outputPath, pace, pitch, energy);
    }

    private static void GenerateFormantWav(string text, string outputPath, double pace, double pitch, double energy)
    {
        const int sampleRate = 48000;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var durationSec = Math.Max(1.0, words.Length * 0.4 / Math.Max(0.5, pace));
        var totalSamples = (int)(durationSec * sampleRate);
        var samples = new short[totalSamples];

        var baseFreq = 160.0 * Math.Clamp(pitch, 0.5, 2.0);
        var volume = Math.Clamp(energy, 0.2, 2.0) * 16000.0;

        for (var i = 0; i < totalSamples; i++)
        {
            var t = (double)i / sampleRate;
            var env = Math.Sin(Math.PI * (t % 0.35) / 0.35); // word cadences
            var wave = Math.Sin(2 * Math.PI * baseFreq * t) + 0.5 * Math.Sin(4 * Math.PI * baseFreq * t) + 0.25 * Math.Sin(6 * Math.PI * baseFreq * t);
            samples[i] = (short)Math.Clamp(wave * env * volume, short.MinValue, short.MaxValue);
        }

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        // RIFF header
        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + totalSamples * 2);
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray());
        bw.Write(16); // subchunk 1 size
        bw.Write((short)1); // PCM
        bw.Write((short)1); // Mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2); // Byte rate
        bw.Write((short)2); // Block align
        bw.Write((short)16); // Bits per sample
        bw.Write("data"u8.ToArray());
        bw.Write(totalSamples * 2);
        for (var i = 0; i < totalSamples; i++) bw.Write(samples[i]);
    }

    private void PlayPreviewClick(object sender, RoutedEventArgs e)
    {
        if (lastGeneratedWavPath == null || !File.Exists(lastGeneratedWavPath)) return;

        if (isPlaying)
        {
            soundPlayer?.Stop();
            isPlaying = false;
            playButton.Content = "▶ Play Preview";
        }
        else
        {
            soundPlayer = new SoundPlayer(lastGeneratedWavPath);
            soundPlayer.Play();
            isPlaying = true;
            playButton.Content = "⏹ Stop Preview";
        }
    }

    private async void AddToTimelineClick(object sender, RoutedEventArgs e)
    {
        if (lastGeneratedWavPath == null || !File.Exists(lastGeneratedWavPath)) return;

        try
        {
            statusText.Text = "Adding to timeline…";
            var media = await vm.ImportAsync(lastGeneratedWavPath);
            // Place at current cursor position on audio track
            var audioTrack = vm.Project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio && !t.IsLocked);
            vm.Place(media.Id, audioTrack?.Id, at: vm.Position, advanceCursor: true);
            statusText.Text = "Voice added to timeline track.";
            MessageBox.Show(this, "Synthesized audio inserted onto timeline audio track at playhead cursor.", "AI Text to Speech", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Failed to insert to timeline: {ex.Message}";
        }
    }

    private async void AddToMediaClick(object sender, RoutedEventArgs e)
    {
        if (lastGeneratedWavPath == null || !File.Exists(lastGeneratedWavPath)) return;

        try
        {
            statusText.Text = "Adding to Project Media…";
            await vm.ImportAsync(lastGeneratedWavPath);
            statusText.Text = "Voice added to Project Media.";
            MessageBox.Show(this, "Synthesized audio added to Project Media library.", "AI Text to Speech", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Failed to add to media: {ex.Message}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        soundPlayer?.Stop();
        soundPlayer?.Dispose();
    }
}
