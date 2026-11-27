using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.App;

public partial class MainWindow
{
    private Guid? inspectorClipId;

    private void UpdateInspector()
    {
        var selected = vm.Project.Tracks.SelectMany(track => track.Clips.Where(clip => vm.Selection.Contains(clip.Id))
            .Select(clip => (Track: track, Clip: clip))).ToArray();
        var video = selected.Where(item => item.Track.Kind == TrackKind.Video).Take(2).ToArray();
        DockColorGrading.BindToClip(video.Length == 1 ? video[0].Clip : null);

        var active = vm.Project.Tracks.ElementAtOrDefault(Timeline.ActiveTrackIndex);
        var target = selected.FirstOrDefault(item => item.Clip.Id == Timeline.ActiveClipId);
        if (target.Clip is null) target = selected.FirstOrDefault(item => item.Track == active);
        if (target.Clip is null) target = selected.FirstOrDefault();
        var (track, clip) = target;
        inspectorClipId = clip?.Id;
        ClipName.Text = clip?.Name ?? "Select a timeline event";
        ClipName.ToolTip = clip?.Name;
        Inspector.IsEnabled = clip is not null && !clip.IsLocked && !track.IsLocked;
        Inspector.Visibility = clip is null ? Visibility.Collapsed : Visibility.Visible;
        ClearClipPropertyError();

        var audio = selected.Where(item => item.Track.Kind == TrackKind.Audio).ToArray();
        LockEventButton.IsEnabled = selected.Length > 0 && selected.All(item => !item.Track.IsLocked);
        LockEventButton.IsChecked = selected.Length > 0 && selected.All(item => item.Clip.IsLocked);
        MuteEventButton.IsEnabled = LockEventButton.IsEnabled && selected.All(item => !item.Clip.IsLocked);
        MuteEventButton.IsChecked = selected.Length > 0 && selected.All(item => item.Clip.IsMuted);
        NormalizeClipAudioButton.IsEnabled = audio.Length > 0 && audio.All(item => !item.Track.IsLocked && !item.Clip.IsLocked);
        if (audio.Length == 0 && selected.FirstOrDefault().Clip?.GroupId is { } group)
            audio = vm.Project.Tracks.Where(item => item.Kind == TrackKind.Audio)
                .SelectMany(item => item.Clips.Where(member => member.GroupId == group).Select(member => (Track: item, Clip: member))).ToArray();
        MuteAudioButton.IsEnabled = audio.Length > 0 && audio.All(item => !item.Track.IsLocked && !item.Clip.IsLocked);
        MuteAudioButton.IsChecked = audio.Length > 0 && audio.All(item => item.Clip.IsMuted);
        var isText = clip is not null && vm.Project.Media.Any(media => media.Id == clip.MediaId
            && (media.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase)
                || media.Path.StartsWith("openrend://subtitles", StringComparison.OrdinalIgnoreCase)));
        EditTextMediaButton.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        EditTextMediaButton.IsEnabled = Inspector.IsEnabled;
        vm.Renderer.ActiveSubtitleClipId = isText ? clip?.Id : null;
        if (clip is null) return;

        var isVideo = track.Kind == TrackKind.Video;
        ClipOpacity.Visibility = ClipOpacityLabel.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
        ClipGain.Visibility = ClipGainLabel.Visibility = ClipPan.Visibility = ClipPanLabel.Visibility =
            isVideo ? Visibility.Collapsed : Visibility.Visible;

        ClipStart.Text = clip.StartFrame.ToString(CultureInfo.CurrentCulture);
        ClipIn.Text = clip.SourceInFrame.ToString(CultureInfo.CurrentCulture);
        ClipDuration.Text = clip.DurationFrames.ToString(CultureInfo.CurrentCulture);
        ClipOpacity.Text = clip.Opacity.ToString(CultureInfo.CurrentCulture);
        ClipGain.Text = clip.Gain.ToString(CultureInfo.CurrentCulture);
        ClipPan.Text = clip.Pan.ToString(CultureInfo.CurrentCulture);
        ClipFadeIn.Text = clip.FadeInFrames.ToString(CultureInfo.CurrentCulture);
        ClipFadeOut.Text = clip.FadeOutFrames.ToString(CultureInfo.CurrentCulture);
    }

    private void ClearClipPropertyError()
    {
        ClipPropertyError.Text = string.Empty;
        ClipPropertyError.Visibility = Visibility.Collapsed;
    }

    private void ShowClipPropertyError(string message, TextBox? field = null)
    {
        ClipPropertyError.Text = message;
        ClipPropertyError.Visibility = Visibility.Visible;
        UIElementAutomationPeer.CreatePeerForElement(ClipPropertyError)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        field?.Focus();
        field?.SelectAll();
    }

    private bool ReadClipFrames(TextBox field, long minimum, string error, out long value)
    {
        if (long.TryParse(field.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value >= minimum)
            return true;
        ShowClipPropertyError(error, field);
        return false;
    }

    private bool ReadClipLevel(TextBox field, double min, double max, string error, out double value)
    {
        if (double.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            && double.IsFinite(value) && value >= min && value <= max)
            return true;
        ShowClipPropertyError(error, field);
        return false;
    }

    private void ClipPropertiesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (!e.IsRepeat) ApplyPropertiesClick(sender, e);
        e.Handled = true;
    }

    private void ApplyPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (vm.Busy || !Inspector.IsEnabled || inspectorClipId is not { } clipId) return;
        var (_, clip) = vm.Editor.FindClip(clipId);
        ClearClipPropertyError();
        var minimumSource = clip.VideoSourceMode == VideoSourceMode.Loop ? long.MinValue : 0;
        if (!ReadClipFrames(ClipStart, 0, "Enter a non-negative position in frames.", out var start)
            || !ReadClipFrames(ClipIn, minimumSource, "Enter a valid source position in frames.", out var source)
            || !ReadClipFrames(ClipDuration, 1, "Enter a duration of at least one frame.", out var duration)
            || !ReadClipLevel(ClipOpacity, 0, 1, "Enter opacity between 0 and 1.", out var opacity)
            || !ReadClipLevel(ClipGain, 0, double.MaxValue, "Enter a non-negative audio gain.", out var gain)
            || !ReadClipLevel(ClipPan, -1, 1, "Enter pan between −1 and 1.", out var pan)
            || !ReadClipFrames(ClipFadeIn, 0, "Enter a non-negative fade-in length in frames.", out var fadeIn)
            || !ReadClipFrames(ClipFadeOut, 0, "Enter a non-negative fade-out length in frames.", out var fadeOut)) return;

        try
        {
            Pause();
            if (vm.Editor.SetClipProperties(clipId, new(start, source, duration, opacity, gain, pan, fadeIn, fadeOut), vm.IgnoreGroups))
                vm.Status = "Event properties updated.";
        }
        catch (OverflowException) { ShowClipPropertyError("The requested frame range is too large."); }
        catch (ArgumentOutOfRangeException error) { ShowClipPropertyError(error.Message.Split('\n')[0].TrimEnd()); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            ShowClipPropertyError(error.Message);
        }
    }
}
