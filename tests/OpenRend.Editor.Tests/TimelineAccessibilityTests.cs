using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Threading;
using OpenRend.Core;
using OpenRend.Timeline;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TimelineAccessibilityTests
{
    private static (TimelineView View, TimelineEditor Editor) Create()
    {
        var media = new MediaAsset { Path = "generated.mp4", DurationSeconds = 10, HasVideo = true, HasAudio = true };
        var project = new ProjectDocument
        {
            Media = [media],
            Tracks = [
                new TimelineTrack { Name = "Picture", Kind = TrackKind.Video, Clips = [
                    new TimelineClip { Name = "Third", MediaId = media.Id, StartFrame = 120, DurationFrames = 30 },
                    new TimelineClip { Name = "First", MediaId = media.Id, DurationFrames = 30 },
                    new TimelineClip { Name = "Second", MediaId = media.Id, StartFrame = 60, DurationFrames = 30 }
                ] },
                new TimelineTrack { Name = "Sound", Kind = TrackKind.Audio, Clips = [new TimelineClip { Name = "Audio", MediaId = media.Id, DurationFrames = 150 }] }
            ]
        };
        var editor = new TimelineEditor(project);
        var view = new TimelineView { Project = project };
        view.TrackAction += (id, action) =>
        {
            var track = editor.Project.Tracks.Single(t => t.Id == id);
            editor.SetTrackState(id, isMuted: action == "Mute" ? !track.IsMuted : null,
                isSolo: action == "Solo" ? !track.IsSolo : null,
                isLocked: action == "Lock" ? !track.IsLocked : null,
                isVisible: action == "Visible" ? !track.IsVisible : null,
                armRecord: action == "Arm" ? !track.ArmRecord : null);
        };
        editor.Changed += (_, _) => view.Project = editor.Project;
        view.TrackLevelRequested += gesture => editor.SetTrackLevels(gesture.TrackId, gain: gesture.Level == "Gain" ? gesture.Value : null,
            opacity: gesture.Level == "Opacity" ? gesture.Value : null, pan: gesture.Level == "Pan" ? gesture.Value : null);
        return (view, editor);
    }

    [Fact]
    public void TrackLevelProvidersApplyUndoAndRejectLockedOrInvalidChanges() => Sta(() =>
    {
        var (view, editor) = Create();
        var peer = UIElementAutomationPeer.CreatePeerForElement(view);
        var picture = peer.GetChildren()[0]; var sound = peer.GetChildren()[1];
        IRangeValueProvider Range(AutomationPeer parent, string name) => Assert.IsAssignableFrom<IRangeValueProvider>(parent.GetChildren().Single(c => c.GetAutomationId().EndsWith("_" + name)).GetPattern(PatternInterface.RangeValue));
        var opacity = Range(picture, "Opacity"); var gain = Range(sound, "Gain"); var pan = Range(sound, "Pan");
        opacity.SetValue(.35); gain.SetValue(.5); pan.SetValue(-.75);
        Assert.Equal(.35, editor.Project.Tracks[0].Opacity);
        Assert.Equal(.5, editor.Project.Tracks[1].Gain);
        Assert.Equal(-.75, editor.Project.Tracks[1].Pan);
        Assert.Equal(-.75, pan.Value);
        editor.Undo(); Assert.Equal(0, pan.Value);
        editor.Redo(); Assert.Equal(-.75, pan.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => opacity.SetValue(1.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => gain.SetValue(double.NaN));
        editor.SetTrackState(editor.Project.Tracks[1].Id, isLocked: true);
        Assert.True(gain.IsReadOnly);
        Assert.Throws<ElementNotEnabledException>(() => gain.SetValue(.25));
        editor.Project.Validate();
    });

    [Fact]
    public void TrackGainPointerAndAutomationShareSixDecibelLimitAndPanHasACenterDetent() => Sta(() =>
    {
        var (view, editor) = Create();
        var sound = editor.Project.Tracks[1];
        var gain = view.TrackLevelBounds(sound.Id, "Gain");
        var pan = view.TrackLevelBounds(sound.Id, "Pan");
        var peer = UIElementAutomationPeer.CreatePeerForElement(view).GetChildren()[1];
        var provider = Assert.IsAssignableFrom<IRangeValueProvider>(peer.GetChildren().Single(child => child.GetAutomationId().EndsWith("_Gain"))
            .GetPattern(PatternInterface.RangeValue));
        Assert.Equal(6, 20 * Math.Log10(provider.Maximum), 8);
        Assert.Equal(provider.Maximum, view.TrackLevelAt(sound.Id, "Gain", gain.Right));
        Assert.Equal(0, view.TrackLevelAt(sound.Id, "Gain", gain.Left));
        Assert.Equal(-1, view.TrackLevelAt(sound.Id, "Pan", pan.Left));
        Assert.Equal(0, view.TrackLevelAt(sound.Id, "Pan", pan.Left + pan.Width / 2 + 1.5));
        Assert.True(view.TrackLevelAt(sound.Id, "Pan", pan.Left + pan.Width / 2 + 3) > 0);
        Assert.Equal(1, view.TrackLevelAt(sound.Id, "Pan", pan.Right));

        editor.SetTrackLevels(sound.Id, gain: 3);
        Assert.Equal(3, provider.Value);
        Assert.Equal(3, provider.Maximum);
        provider.SetValue(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.SetValue(3));
        editor.Undo();
        Assert.Equal(3, provider.Value);
    });

    [Fact]
    public void TrackSizesUpdateEventsDropTargetsAndAccessibleControlsWithoutEditing() => Sta(() =>
    {
        var (view, editor) = Create();
        view.Measure(new Size(900, 350));
        view.Arrange(new Rect(0, 0, 900, 350));
        var picture = editor.Project.Tracks[0];
        var sound = editor.Project.Tracks[1];
        var picturePeer = UIElementAutomationPeer.CreatePeerForElement(view).GetChildren()[0];
        var regular = view.GetTrackHeight(0);

        view.SetTrackRowSize(picture.Id, TrackRowSize.Minimized);
        view.SetTrackRowSize(sound.Id, TrackRowSize.Expanded);
        var compact = view.GetTrackHeight(0);
        Assert.True(compact < regular);
        Assert.Equal(regular * 2, view.GetTrackHeight(1));
        Assert.Equal(TimelineView.RulerHeight + compact, view.GetTrackY(1));
        Assert.Equal(compact - 2, view.ClipBounds(picture.Id, picture.Clips[0]).Height);
        Assert.Equal(compact + regular * 2, view.TotalTimelineContentHeight);
        Assert.Equal((false, 0, TrackKind.Video), view.EvaluateDragTarget(view.GetTrackY(0) + compact / 2, TrackKind.Video));
        Assert.Equal((false, 1, TrackKind.Audio), view.EvaluateDragTarget(view.GetTrackY(1) + regular, TrackKind.Audio));
        Assert.DoesNotContain(picturePeer.GetChildren(), child => child.GetAutomationControlType() == AutomationControlType.Slider);

        view.SetTrackRowSize(picture.Id, TrackRowSize.Normal);
        Assert.Equal(regular, view.GetTrackHeight(0));
        Assert.Contains(picturePeer.GetChildren(), child => child.GetAutomationControlType() == AutomationControlType.Slider);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public void FocusedTrackLevelAcceptsKeyboardAdjustmentsWithoutSelectingClips() => Sta(() =>
    {
        var (view, editor) = Create();
        var peer = UIElementAutomationPeer.CreatePeerForElement(view);
        var slider = peer.GetChildren()[0].GetChildren().Single(c => c.GetAutomationId().EndsWith("_Opacity"));
        slider.SetFocus();
        Assert.True(view.HandleKeyboardInput(Key.Left, ModifierKeys.None));
        Assert.Equal(.99, editor.Project.Tracks[0].Opacity, 5);
        Assert.Empty(view.Selection);
        view.HandleKeyboardInput(Key.Home, ModifierKeys.None); Assert.Equal(0, editor.Project.Tracks[0].Opacity);
        view.HandleKeyboardInput(Key.End, ModifierKeys.None); Assert.Equal(1, editor.Project.Tracks[0].Opacity);
        view.HandleKeyboardInput(Key.Escape, ModifierKeys.None);
        view.HandleKeyboardInput(Key.Right, ModifierKeys.Control); Assert.Single(view.Selection);
    });

    [Fact]
    public void ReplacingProjectClearsTrackLevelFocusAndStalePeersRejectChanges() => Sta(() =>
    {
        var (view, _) = Create();
        var peer = UIElementAutomationPeer.CreatePeerForElement(view);
        var slider = peer.GetChildren()[0].GetChildren().Single(c => c.GetAutomationId().EndsWith("_Opacity"));
        slider.SetFocus();
        view.Project = new ProjectDocument { Tracks = [new TimelineTrack { Kind = TrackKind.Video }] };
        Assert.True(view.HandleKeyboardInput(Key.Right, ModifierKeys.None));
        Assert.Empty(view.Selection);
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(slider.GetPattern(PatternInterface.RangeValue));
        Assert.Throws<ElementNotAvailableException>(() => range.SetValue(.5));
    });

    [Fact]
    public void VirtualClipSelectionUpdatesTheEditorSelectionAndSupportsMultipleItems() => Sta(() =>
    {
        var (view, _) = Create();
        var changed = 0;
        view.SelectionChanged += () => changed++;
        var peer = Assert.IsType<TimelineAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(view));
        Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
        Assert.Equal("Timeline", peer.GetName());
        Assert.Contains("Shift+M", peer.GetHelpText());
        var selection = Assert.IsAssignableFrom<ISelectionProvider>(peer.GetPattern(PatternInterface.Selection));
        Assert.True(selection.CanSelectMultiple);
        Assert.False(selection.IsSelectionRequired);
        var tracks = peer.GetChildren();
        Assert.Equal(2, tracks.Count);
        var clips = tracks[0].GetChildren().Where(c => c.GetAutomationControlType() == AutomationControlType.ListItem).ToArray();
        Assert.Equal(3, clips.Length);
        Assert.StartsWith("First, Picture, start 0", clips[0].GetName());
        var first = Assert.IsAssignableFrom<ISelectionItemProvider>(clips[0].GetPattern(PatternInterface.SelectionItem));
        var second = Assert.IsAssignableFrom<ISelectionItemProvider>(clips[1].GetPattern(PatternInterface.SelectionItem));
        first.Select();
        Assert.True(first.IsSelected);
        Assert.Single(view.Selection);
        Assert.Single(selection.GetSelection());
        second.AddToSelection();
        Assert.Equal(2, view.Selection.Count);
        Assert.Equal(2, selection.GetSelection().Length);
        first.RemoveFromSelection();
        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.Single(selection.GetSelection());
        Assert.Equal(3, changed);
    });

    [Fact]
    public void HeaderProvidersInvokeRealTrackTransactionsAndReflectUndoState() => Sta(() =>
    {
        var (view, editor) = Create();
        var peer = UIElementAutomationPeer.CreatePeerForElement(view);
        var video = peer.GetChildren()[0];
        var buttons = video.GetChildren().Where(c => c.GetAutomationControlType() == AutomationControlType.Button).ToArray();
        Assert.Equal(4, buttons.Length);
        foreach (var action in new[] { "Mute", "Solo", "Lock", "Visible" })
        {
            var button = buttons.Single(b => b.GetAutomationId().EndsWith("_" + action));
            var toggle = Assert.IsAssignableFrom<IToggleProvider>(button.GetPattern(PatternInterface.Toggle));
            var initial = toggle.ToggleState;
            Assert.IsAssignableFrom<IInvokeProvider>(button.GetPattern(PatternInterface.Invoke)).Invoke();
            Assert.NotEqual(initial, toggle.ToggleState);
            Assert.True(editor.CanUndo);
            editor.Undo();
            Assert.Equal(initial, toggle.ToggleState);
        }
        Assert.Equal(4, peer.GetChildren()[1].GetChildren().Count(c => c.GetAutomationControlType() == AutomationControlType.Button));
        var armBtn = peer.GetChildren()[1].GetChildren().Single(c => c.GetAutomationId().EndsWith("_Arm"));
        var armToggle = Assert.IsAssignableFrom<IToggleProvider>(armBtn.GetPattern(PatternInterface.Toggle));
        Assert.Equal(ToggleState.Off, armToggle.ToggleState);
        Assert.IsAssignableFrom<IInvokeProvider>(armBtn.GetPattern(PatternInterface.Invoke)).Invoke();
        Assert.Equal(ToggleState.On, armToggle.ToggleState);
        editor.Undo();
        Assert.Equal(ToggleState.Off, armToggle.ToggleState);
        view.IsEnabled = false;
        Assert.Throws<ElementNotEnabledException>(() => ((IInvokeProvider)buttons[0].GetPattern(PatternInterface.Invoke)).Invoke());
    });

    [Fact]
    public void KeyboardNavigationSelectsChronologicalClipsAndExtendsTheSelection() => Sta(() =>
    {
        var (view, editor) = Create();
        Assert.True(view.HandleKeyboardInput(Key.Right, ModifierKeys.Control));
        Assert.Equal("First", editor.Project.Tracks[0].Clips.Single(c => view.Selection.Contains(c.Id)).Name);
        view.HandleKeyboardInput(Key.Right, ModifierKeys.Control | ModifierKeys.Shift);
        view.HandleKeyboardInput(Key.Right, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal(3, view.Selection.Count);
        view.HandleKeyboardInput(Key.Left, ModifierKeys.Control);
        Assert.Equal("Second", editor.Project.Tracks[0].Clips.Single(c => view.Selection.Contains(c.Id)).Name);
        view.HandleKeyboardInput(Key.Down, ModifierKeys.None);
        Assert.Equal(1, view.ActiveTrackIndex);
        view.HandleKeyboardInput(Key.Right, ModifierKeys.Control);
        Assert.Equal(editor.Project.Tracks[1].Clips[0].Id, Assert.Single(view.Selection));
        Assert.False(view.HandleKeyboardInput(Key.V, ModifierKeys.None));
        view.HandleKeyboardInput(Key.Up, ModifierKeys.None);
        view.HandleKeyboardInput(Key.M, ModifierKeys.None);
        Assert.True(editor.Project.Tracks[0].IsMuted);
        view.HandleKeyboardInput(Key.M, ModifierKeys.Shift);
        Assert.True(editor.Project.Tracks[0].IsSolo);
        view.HandleKeyboardInput(Key.L, ModifierKeys.None);
        Assert.True(editor.Project.Tracks[0].IsLocked);
        view.HandleKeyboardInput(Key.V, ModifierKeys.None);
        Assert.False(editor.Project.Tracks[0].IsVisible);
        Assert.False(view.HandleKeyboardInput(Key.Right, ModifierKeys.Alt));
        Assert.False(view.HandleKeyboardInput(Key.A, ModifierKeys.Control));
    });

    [Fact]
    public void TimelineCursorKeysSeekOnTheRationalFrameGrid() => Sta(() =>
    {
        var (view, editor) = Create();
        editor.Project.FrameRate = new Rational(30000, 1001);
        view.SeekRequested += frame => view.Position = frame;
        view.Position = 10;
        Assert.True(view.HandleKeyboardInput(Key.Right, ModifierKeys.None));
        Assert.Equal(11, view.Position);
        Assert.True(view.HandleKeyboardInput(Key.Right, ModifierKeys.Shift));
        Assert.Equal(41, view.Position);
        Assert.True(view.HandleKeyboardInput(Key.Left, ModifierKeys.Shift));
        Assert.Equal(11, view.Position);
        Assert.True(view.HandleKeyboardInput(Key.End, ModifierKeys.None));
        Assert.Equal(editor.Project.DurationFrames, view.Position);
        Assert.True(view.HandleKeyboardInput(Key.Home, ModifierKeys.None));
        Assert.Equal(0, view.Position);
        Assert.Empty(view.Selection);
    });

    [Fact]
    public void ClipProvidersRebindToRestoredSnapshotsAndRejectRemovedClips() => Sta(() =>
    {
        var (view, editor) = Create();
        var peer = UIElementAutomationPeer.CreatePeerForElement(view);
        var clipPeer = peer.GetChildren()[0].GetChildren().First(c => c.GetAutomationControlType() == AutomationControlType.ListItem);
        var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(clipPeer.GetPattern(PatternInterface.SelectionItem));
        selection.Select();
        var id = Assert.Single(view.Selection);
        editor.Execute(p => p.Tracks[0].Clips.Single(c => c.Id == id).Name = "Renamed");
        Assert.StartsWith("Renamed,", clipPeer.GetName());
        editor.Undo();
        Assert.StartsWith("First,", clipPeer.GetName());
        selection.Select();
        Assert.True(selection.IsSelected);
        editor.DeleteClip(id);
        Assert.Throws<ElementNotAvailableException>(() => selection.Select());
    });

    [Fact]
    public void VirtualBoundsMatchRenderedGeometryAndClippedItemsAreOffscreen() => Sta(() =>
    {
        var (view, _) = Create();
        var window = new Window { Width = 900, Height = 350, Left = -10000, ShowActivated = false, ShowInTaskbar = false, Content = view };
        try
        {
            window.Show();
            view.UpdateLayout();
            var peer = UIElementAutomationPeer.CreatePeerForElement(view);
            var track = peer.GetChildren()[0];
            var clip = track.GetChildren().First(c => c.GetAutomationControlType() == AutomationControlType.ListItem);
            var bounds = clip.GetBoundingRectangle();
            var expected = view.PointToScreen(new Point(TimelineView.HeaderWidth, TimelineView.RulerHeight + 1));
            Assert.Equal(expected.X, bounds.X, 1);
            Assert.Equal(expected.Y, bounds.Y, 1);
            Assert.True(bounds.Width > 0);
            var mute = track.GetChildren().Single(c => c.GetAutomationId().EndsWith("_Mute"));
            var button = mute.GetBoundingRectangle();
            Assert.Equal(view.PointToScreen(new Point(207, TimelineView.RulerHeight + 4)).X, button.X, 1);
            Assert.Equal(21 * VisualTreeDpi(view), button.Width, 1);
            view.Offset = 350;
            Assert.True(clip.IsOffscreen());
            Assert.True(clip.GetBoundingRectangle().IsEmpty);
        }
        finally { window.Close(); }
    });

    private static double VisualTreeDpi(TimelineView view) => System.Windows.Media.VisualTreeHelper.GetDpi(view).DpiScaleX;

    [Fact]
    public void LoopInOutKeysKeepEventSelectionAndClearOnProjectReplacement() => Sta(() =>
    {
        var (view, editor) = Create();
        var clipId = editor.Project.Tracks[0].Clips[0].Id;
        view.Selection.Add(clipId);
        view.Position = 30;
        Assert.True(view.HandleKeyboardInput(Key.I, ModifierKeys.None));
        view.Position = 90;
        Assert.True(view.HandleKeyboardInput(Key.O, ModifierKeys.None));
        Assert.Equal(new FrameRange(30, 60), view.LoopRegion);
        Assert.Equal(clipId, Assert.Single(view.Selection));
        Assert.False(editor.CanUndo);
        view.Project = editor.Project;
        Assert.NotNull(view.LoopRegion);
        view.Project = editor.Project.DeepClone();
        Assert.Null(view.LoopRegion);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.LoopRegion = new FrameRange(149, 2));
    });

    [Fact]
    public void OverlapCutShortcutUsesCursorAndActiveTrackWithoutChangingSelection() => Sta(() =>
    {
        var (view, editor) = Create();
        var clips = editor.Project.Tracks[0].Clips;
        var outgoing = clips.Single(clip => clip.Name == "First");
        var incoming = clips.Single(clip => clip.Name == "Second");
        outgoing.DurationFrames = 75;
        view.Position = 65;
        var unrelated = clips.Single(clip => clip.Name == "Third").Id;
        view.Selection.Add(unrelated);
        (Guid, Guid)? requested = null;
        view.OverlapToCutRequested += (left, right) => requested = (left, right);
        Assert.True(view.HandleKeyboardInput(Key.Divide, ModifierKeys.Control));
        Assert.Equal((outgoing.Id, incoming.Id), requested);
        Assert.Equal(unrelated, Assert.Single(view.Selection));
        Assert.False(editor.CanUndo);
        requested = null;
        view.Position = 75;
        Assert.True(view.HandleKeyboardInput(Key.Divide, ModifierKeys.Control));
        Assert.Null(requested);
        view.Position = 65;
        Assert.False(view.HandleKeyboardInput(Key.Divide, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(view.HandleKeyboardInput(Key.OemQuestion, ModifierKeys.Control));
        Assert.Null(requested);
    });

    [Fact]
    public void OverlapCutShortcutIgnoresLockedAudioAndAmbiguousTracks() => Sta(() =>
    {
        var (view, editor) = Create();
        var track = editor.Project.Tracks[0];
        track.Clips.Single(clip => clip.Name == "First").DurationFrames = 75;
        view.Position = 65;
        var requests = 0; view.OverlapToCutRequested += (_, _) => requests++;
        track.IsLocked = true;
        view.HandleKeyboardInput(Key.Divide, ModifierKeys.Control);
        track.IsLocked = false;
        view.HandleKeyboardInput(Key.Down, ModifierKeys.None);
        view.HandleKeyboardInput(Key.Divide, ModifierKeys.Control);
        view.HandleKeyboardInput(Key.Up, ModifierKeys.None);
        track.Clips.Add(new() { MediaId = track.Clips[0].MediaId, StartFrame = 64, DurationFrames = 4 });
        view.HandleKeyboardInput(Key.Divide, ModifierKeys.Control);
        Assert.Equal(0, requests);
        view.Project = new();
        Assert.True(view.HandleKeyboardInput(Key.Divide, ModifierKeys.Control));
    });

    [Fact]
    public void VideoLoopMenuRoutesModesAndReflectsUndoAndTrackLocks() => Sta(() =>
    {
        var (view, editor) = Create();
        var id = editor.Project.Tracks[0].Clips[0].Id;
        var requests = 0;
        view.VideoSourceModeRequested += (clip, mode) => { requests++; editor.SetVideoSourceMode(clip, mode); };
        var menu = view.CreateEventMenu(id);
        var switches = menu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => Equals(item.Header, "Switches"));
        var loop = Assert.IsType<System.Windows.Controls.MenuItem>(switches.Items[0]);
        void Open() => menu.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.ContextMenu.OpenedEvent));
        void Click(bool value) { loop.IsChecked = value; loop.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); }
        Open(); Assert.Equal(Visibility.Visible, switches.Visibility); Assert.False(loop.IsChecked);
        Assert.Equal("VideoEventLoop", AutomationProperties.GetAutomationId(loop));
        Click(true); Assert.Equal(VideoSourceMode.Loop, editor.Project.Tracks[0].Clips[0].VideoSourceMode);
        Open(); Assert.True(loop.IsChecked);
        Click(false); Assert.Equal(VideoSourceMode.HoldLastFrame, editor.Project.Tracks[0].Clips[0].VideoSourceMode);
        editor.Undo(); Open(); Assert.True(loop.IsChecked);
        editor.Project.Tracks[0].IsLocked = true; Open(); Assert.False(loop.IsEnabled);
        Click(false); Assert.Equal(2, requests);
        editor.Project.Tracks[0].IsLocked = false;
        editor.Project.Media[0].Path = "openrend://color?hex=FFFFFF";
        Open(); Assert.Equal(Visibility.Collapsed, switches.Visibility);
        var audioMenu = view.CreateEventMenu(editor.Project.Tracks[1].Clips[0].Id);
        audioMenu.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.ContextMenu.OpenedEvent));
        Assert.Equal(Visibility.Collapsed, audioMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => Equals(item.Header, "Switches")).Visibility);
    });

    [Fact]
    public void VideoFadeCurveMenuRoutesShapesAndReflectsUndoAndFades() => Sta(() =>
    {
        var (view, editor) = Create();
        var id = editor.Project.Tracks[0].Clips[0].Id;
        editor.Project.Tracks[0].Clips[0].FadeInFrames = 10;
        editor.Project.Tracks[0].Clips[0].FadeOutFrames = 10;
        var requests = 0;
        view.VideoFadeCurveRequested += (clip, curve, recip, fadeOut) => { requests++; editor.SetVideoFadeCurve(clip, curve, recip, fadeOut); };
        var menu = view.CreateEventMenu(id);
        var inMenu = menu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => Equals(item.Header, "Fade In Curve"));
        var outMenu = menu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => Equals(item.Header, "Fade Out Curve"));
        var typeMenu = menu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => Equals(item.Header, "Fade Type"));
        void Open() => menu.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.ContextMenu.OpenedEvent));
        Open();
        Assert.Equal(Visibility.Visible, inMenu.Visibility);
        Assert.Equal(Visibility.Visible, outMenu.Visibility);
        Assert.Equal(Visibility.Visible, typeMenu.Visibility);
        Assert.Equal("FadeInCurveMenu", AutomationProperties.GetAutomationId(inMenu));
        Assert.Equal("FadeOutCurveMenu", AutomationProperties.GetAutomationId(outMenu));
        Assert.Equal("FadeTypeMenu", AutomationProperties.GetAutomationId(typeMenu));
        var inFast = inMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => Equals(item.Header, "Fast"));
        var inLinear = inMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => Equals(item.Header, "Linear"));
        Assert.True(inLinear.IsChecked);
        Assert.False(inFast.IsChecked);

        var grid = Assert.IsType<System.Windows.Controls.Primitives.UniformGrid>(Assert.Single(typeMenu.Items));
        Assert.Equal(25, grid.Children.Count);
        var smoothSmooth = grid.Children.OfType<System.Windows.Controls.Button>()
            .Single(b => AutomationProperties.GetAutomationId(b) == "FadeType_Smooth_Smooth");
        smoothSmooth.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(VideoFadeCurve.Smooth, editor.Project.Tracks[0].Clips[0].FadeInCurve);
        Assert.Equal(VideoFadeCurve.Smooth, editor.Project.Tracks[0].Clips[0].FadeInReciprocalCurve);

        inFast.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Assert.Equal(VideoFadeCurve.Fast, editor.Project.Tracks[0].Clips[0].FadeInCurve);
        Open();
        Assert.True(inFast.IsChecked);
        Assert.False(inLinear.IsChecked);

        var outSlow = outMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => Equals(item.Header, "Slow"));
        outSlow.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Assert.Equal(VideoFadeCurve.Slow, editor.Project.Tracks[0].Clips[0].FadeOutCurve);
        Open();
        Assert.True(outSlow.IsChecked);

        editor.Undo();
        Open();
        Assert.Equal(VideoFadeCurve.Linear, editor.Project.Tracks[0].Clips[0].FadeOutCurve);
        Assert.False(outSlow.IsChecked);

        editor.Project.Tracks[0].IsLocked = true;
        Open();
        Assert.False(inMenu.IsEnabled);
        Assert.False(outMenu.IsEnabled);
        Assert.False(typeMenu.IsEnabled);
        editor.Project.Tracks[0].IsLocked = false;

        editor.Project.Tracks[0].Clips[0].FadeInFrames = 0;
        editor.Project.Tracks[0].Clips[0].FadeOutFrames = 0;
        Open();
        Assert.Equal(Visibility.Collapsed, inMenu.Visibility);
        Assert.Equal(Visibility.Collapsed, outMenu.Visibility);
        Assert.Equal(Visibility.Collapsed, typeMenu.Visibility);

        var audioMenu = view.CreateEventMenu(editor.Project.Tracks[1].Clips[0].Id);
        audioMenu.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.ContextMenu.OpenedEvent));
        Assert.Equal(Visibility.Collapsed, audioMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => Equals(item.Header, "Fade In Curve")).Visibility);
    });

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Timeline accessibility check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
