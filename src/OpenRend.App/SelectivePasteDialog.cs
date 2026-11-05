namespace OpenRend.App;

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using OpenRend.Core;
using OpenRend.Timeline;

/// <summary>
/// Vegas Pro 1:1 "Selectively Paste Event Attributes" modal dialog.
/// Matches ScriptPortal.Vegas.SelPastAttrConfig and vegas2027.exe.
/// Allows granular selection of attribute categories (Video FX, Pan/Crop/Mask,
/// Switches, Fades, Audio FX, Gain/Pan, Pitch, Audio Fades) and FX replacement mode.
/// </summary>
public sealed class SelectivePasteDialog : Window
{
    private readonly CheckBox videoFxCheck;
    private readonly CheckBox panCropCheck;
    private readonly CheckBox videoSwitchesCheck;
    private readonly CheckBox videoFadesCheck;

    private readonly CheckBox audioFxCheck;
    private readonly CheckBox audioGainPanCheck;
    private readonly CheckBox audioPitchCheck;
    private readonly CheckBox audioSwitchesCheck;
    private readonly CheckBox audioFadesCheck;

    private readonly CheckBox replaceExistingFxCheck;

    public PasteEventAttributesOptions Options { get; } = new();

    public SelectivePasteDialog(TrackKind? sourceKind = null)
    {
        Title = "Selectively Paste Event Attributes";
        Width = 470;
        Height = 490;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        var textBrush = Brushes.White;
        var textMutedBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        var borderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var buttonBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        Background = panelBrush;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header hint
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Video Group
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Audio Group
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Options Group
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        var header = new TextBlock
        {
            Text = "Select the attributes to paste from the clipboard event into the selected events:",
            Foreground = textBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Video Group
        var videoGroup = new GroupBox
        {
            Header = "Video Attributes",
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var videoStack = new StackPanel();
        videoFxCheck = new CheckBox { Content = "Video Event FX", Foreground = textBrush, IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
        AutomationProperties.SetAutomationId(videoFxCheck, "SelectivePasteVideoFxCheck");
        panCropCheck = new CheckBox { Content = "Event Pan/Crop (including Mask)", Foreground = textBrush, IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
        AutomationProperties.SetAutomationId(panCropCheck, "SelectivePastePanCropCheck");
        videoSwitchesCheck = new CheckBox { Content = "Switches (Mute, Lock, Loop, Rate, Aspect, Interlace)", Foreground = textBrush, IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
        AutomationProperties.SetAutomationId(videoSwitchesCheck, "SelectivePasteVideoSwitchesCheck");
        videoFadesCheck = new CheckBox { Content = "Opacity & Fade Curves", Foreground = textBrush, IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
        AutomationProperties.SetAutomationId(videoFadesCheck, "SelectivePasteVideoFadesCheck");

        videoStack.Children.Add(videoFxCheck);
        videoStack.Children.Add(panCropCheck);
        videoStack.Children.Add(videoSwitchesCheck);
        videoStack.Children.Add(videoFadesCheck);
        videoGroup.Content = videoStack;
        Grid.SetRow(videoGroup, 1);
        root.Children.Add(videoGroup);

        // Audio Group
        var audioGroup = new GroupBox
        {
            Header = "Audio Attributes",
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var audioStack = new StackPanel();
        audioFxCheck = new CheckBox { Content = "Audio Event FX", Foreground = textBrush, IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
        AutomationProperties.SetAutomationId(audioFxCheck, "SelectivePasteAudioFxCheck");
        audioGainPanCheck = new CheckBox { Content = "Volume & Pan", Foreground = textBrush, IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
        AutomationProperties.SetAutomationId(audioGainPanCheck, "SelectivePasteAudioGainPanCheck");
        audioPitchCheck = new CheckBox { Content = "Pitch, Invert Phase, Normalize & Channels", Foreground = textBrush, IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
        AutomationProperties.SetAutomationId(audioPitchCheck, "SelectivePasteAudioPitchCheck");
        audioSwitchesCheck = new CheckBox { Content = "Switches (Mute, Lock, Rate)", Foreground = textBrush, IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
        AutomationProperties.SetAutomationId(audioSwitchesCheck, "SelectivePasteAudioSwitchesCheck");
        audioFadesCheck = new CheckBox { Content = "Fade Curves", Foreground = textBrush, IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
        AutomationProperties.SetAutomationId(audioFadesCheck, "SelectivePasteAudioFadesCheck");

        audioStack.Children.Add(audioFxCheck);
        audioStack.Children.Add(audioGainPanCheck);
        audioStack.Children.Add(audioPitchCheck);
        audioStack.Children.Add(audioSwitchesCheck);
        audioStack.Children.Add(audioFadesCheck);
        audioGroup.Content = audioStack;
        Grid.SetRow(audioGroup, 2);
        root.Children.Add(audioGroup);

        // Options Group
        var optionsGroup = new GroupBox
        {
            Header = "Options",
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var optionsStack = new StackPanel();
        replaceExistingFxCheck = new CheckBox
        {
            Content = "Replace existing FX on destination events (uncheck to append)",
            Foreground = textBrush,
            IsChecked = true,
            Margin = new Thickness(0, 2, 0, 2)
        };
        AutomationProperties.SetAutomationId(replaceExistingFxCheck, "SelectivePasteReplaceFxCheck");
        optionsStack.Children.Add(replaceExistingFxCheck);
        optionsGroup.Content = optionsStack;
        Grid.SetRow(optionsGroup, 3);
        root.Children.Add(optionsGroup);

        if (sourceKind == TrackKind.Video)
        {
            audioFxCheck.IsChecked = false;
            audioGainPanCheck.IsChecked = false;
            audioPitchCheck.IsChecked = false;
            audioSwitchesCheck.IsChecked = false;
            audioFadesCheck.IsChecked = false;
            audioGroup.IsEnabled = false;
        }
        else if (sourceKind == TrackKind.Audio)
        {
            videoFxCheck.IsChecked = false;
            panCropCheck.IsChecked = false;
            videoSwitchesCheck.IsChecked = false;
            videoFadesCheck.IsChecked = false;
            videoGroup.IsEnabled = false;
        }

        // Button Row
        var btnGrid = new Grid();
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftBtns = new StackPanel { Orientation = Orientation.Horizontal };
        var selectAllBtn = new Button
        {
            Content = "Select All",
            Width = 80,
            Height = 24,
            Margin = new Thickness(0, 0, 6, 0),
            Background = buttonBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };
        AutomationProperties.SetAutomationId(selectAllBtn, "SelectivePasteSelectAllBtn");
        selectAllBtn.Click += (_, _) => SetAllChecked(true);
        leftBtns.Children.Add(selectAllBtn);

        var selectNoneBtn = new Button
        {
            Content = "Select None",
            Width = 80,
            Height = 24,
            Background = buttonBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };
        AutomationProperties.SetAutomationId(selectNoneBtn, "SelectivePasteSelectNoneBtn");
        selectNoneBtn.Click += (_, _) => SetAllChecked(false);
        leftBtns.Children.Add(selectNoneBtn);

        btnGrid.Children.Add(leftBtns);

        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal };
        var okBtn = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 24,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = buttonBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };
        AutomationProperties.SetAutomationId(okBtn, "SelectivePasteOkBtn");
        okBtn.Click += (_, _) => OnOk();
        rightBtns.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 24,
            IsCancel = true,
            Background = buttonBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };
        AutomationProperties.SetAutomationId(cancelBtn, "SelectivePasteCancelBtn");
        rightBtns.Children.Add(cancelBtn);

        Grid.SetColumn(rightBtns, 1);
        btnGrid.Children.Add(rightBtns);

        Grid.SetRow(btnGrid, 5);
        root.Children.Add(btnGrid);

        Content = root;
    }

    private void SetAllChecked(bool isChecked)
    {
        if (videoFxCheck.IsEnabled) videoFxCheck.IsChecked = isChecked;
        if (panCropCheck.IsEnabled) panCropCheck.IsChecked = isChecked;
        if (videoSwitchesCheck.IsEnabled) videoSwitchesCheck.IsChecked = isChecked;
        if (videoFadesCheck.IsEnabled) videoFadesCheck.IsChecked = isChecked;

        if (audioFxCheck.IsEnabled) audioFxCheck.IsChecked = isChecked;
        if (audioGainPanCheck.IsEnabled) audioGainPanCheck.IsChecked = isChecked;
        if (audioPitchCheck.IsEnabled) audioPitchCheck.IsChecked = isChecked;
        if (audioSwitchesCheck.IsEnabled) audioSwitchesCheck.IsChecked = isChecked;
        if (audioFadesCheck.IsEnabled) audioFadesCheck.IsChecked = isChecked;
    }

    private void OnOk()
    {
        Options.VideoEffects = videoFxCheck.IsChecked == true;
        Options.PanCrop = panCropCheck.IsChecked == true;
        Options.VideoSwitches = videoSwitchesCheck.IsChecked == true;
        Options.VideoFadesAndOpacity = videoFadesCheck.IsChecked == true;

        Options.AudioEffects = audioFxCheck.IsChecked == true;
        Options.AudioGainAndPan = audioGainPanCheck.IsChecked == true;
        Options.AudioPitchAndChannels = audioPitchCheck.IsChecked == true;
        Options.AudioSwitches = audioSwitchesCheck.IsChecked == true;
        Options.AudioFades = audioFadesCheck.IsChecked == true;

        Options.ReplaceExistingFX = replaceExistingFxCheck.IsChecked == true;

        DialogResult = true;
        Close();
    }
}

