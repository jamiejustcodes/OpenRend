using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OpenRend.Core;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class EventButtonTests
{
    private static (TimelineView View, TimelineTrack Track, TimelineClip Clip) Create()
    {
        var clip = new TimelineClip { Name = "Picture", StartFrame = 30, DurationFrames = 60 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        var view = new TimelineView { Project = new() { FrameRate = new(30, 1), Tracks = [track] }, PixelsPerSecond = 100 };
        view.Measure(new Size(1000, 400)); view.Arrange(new Rect(0, 0, 1000, 400));
        return (view, track, clip);
    }

    private static Point Center(Rect rect) => new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    [Theory]
    [InlineData("PanCrop")]
    [InlineData("VideoEffects")]
    [InlineData("More")]
    public void BottomToolbarHasSeparateTargetsAndHandCursor(string action) => Sta(() =>
    {
        var (view, track, clip) = Create();
        var bounds = view.EventButtonBounds(track.Id, clip, action);
        var point = Center(bounds);
        Assert.False(bounds.IsEmpty);
        Assert.Equal(action, view.HitEventButton(point)!.Value.Action);
        Assert.Same(Cursors.Hand, view.CursorAt(point));
        Assert.Null(view.HitEventButton(new Point(point.X, bounds.Top - 2)));
    });

    [Fact]
    public void EventActionIsInvokedOnReleaseWithTheClickedIdAfterCaptureIsReleased() => Sta(() =>
    {
        var (view, track, clip) = Create();
        var other = new TimelineClip { StartFrame = 180, DurationFrames = 60 }; track.Clips.Add(other);
        view.Selection.Add(other.Id);
        var invoked = false;
        view.VideoEventActionRequested += (id, action) =>
        {
            Assert.False(view.IsMouseCaptured); Assert.Equal(clip.Id, id); Assert.Equal("PanCrop", action);
            Assert.Equal([clip.Id], view.Selection); invoked = true;
        };
        var point = Center(view.EventButtonBounds(track.Id, clip, "PanCrop"));
        Assert.True(view.BeginEventButton(point)); Assert.False(invoked);
        view.CompleteEventButton(point); Assert.True(invoked);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasingElsewhereOrCancellingDoesNotInvoke(bool cancel) => Sta(() =>
    {
        var (view, track, clip) = Create(); var invoked = false;
        view.VideoEventActionRequested += (_, _) => invoked = true;
        var point = Center(view.EventButtonBounds(track.Id, clip, "VideoEffects"));
        Assert.True(view.BeginEventButton(point));
        if (cancel) view.CancelPointerInput();
        view.CompleteEventButton(cancel ? point : new Point(800, 200));
        Assert.False(invoked);
    });

    [Fact]
    public void LockedButtonsCannotInvokeOrStartATrim() => Sta(() =>
    {
        var (view, track, clip) = Create(); track.IsLocked = true; var invoked = false;
        view.VideoEventActionRequested += (_, _) => invoked = true;
        var point = Center(view.EventButtonBounds(track.Id, clip, "VideoEffects"));
        Assert.True(view.BeginEventButton(point)); view.CompleteEventButton(point);
        Assert.False(invoked); Assert.Same(Cursors.No, view.CursorAt(point));
        Assert.Throws<ElementNotEnabledException>(() => view.InvokeEventAction(clip.Id, "VideoEffects"));
    });

    [Fact]
    public void ShortEventsRetainOverflowWithoutOverlappingButtons() => Sta(() =>
    {
        var (view, track, clip) = Create(); clip.DurationFrames = 9;
        Assert.Equal(["More"], view.EventButtonActions(track, clip));
        clip.DurationFrames = 6;
        Assert.Empty(view.EventButtonActions(track, clip));
        Assert.Equal("Move", view.HitClip(new Point(366, 80))!.Value.Mode);
    });

    [Fact]
    public void ScrollingDoesNotPinButtonsToTheViewportAndClippedButtonsAreNotHitTargets() => Sta(() =>
    {
        var (view, track, clip) = Create(); clip.DurationFrames = 300;
        Assert.Empty(view.EventButtonActions(track, clip));
        clip.DurationFrames = 60; view.Offset = 270;
        Assert.Equal(["More"], view.EventButtonActions(track, clip));
        view.VerticalOffset = 60;
        Assert.Empty(view.EventButtonActions(track, clip));
    });

    [Fact]
    public void OverlappingLaterEventOwnsThePointerInsteadOfCoveredButtons() => Sta(() =>
    {
        var (view, track, clip) = Create();
        var point = Center(view.EventButtonBounds(track.Id, clip, "PanCrop"));
        var covering = new TimelineClip { StartFrame = 60, DurationFrames = 90 }; track.Clips.Add(covering);
        Assert.Null(view.HitEventButton(point));
        Assert.Equal(covering.Id, view.HitClip(point)!.Value.Clip.Id);
    });

    [Fact]
    public void AudioEventsHaveOverflowAndNoVideoButtons() => Sta(() =>
    {
        var (view, track, clip) = Create(); track.Kind = TrackKind.Audio;
        Assert.Equal(["More"], view.EventButtonActions(track, clip));
        Assert.Throws<InvalidOperationException>(() => view.InvokeEventAction(clip.Id, "PanCrop"));
    });

    [Fact]
    public void OverflowDisplayOptionsChangeTheActualViewState() => Sta(() =>
    {
        var (view, _, _) = Create(); var menu = view.CreateEventMenu();
        void Toggle(string header) => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Toggle("Show event headers"); Toggle("Show event names"); Toggle("Show event lengths");
        Assert.False(view.ShowEventHeaders); Assert.False(view.ShowEventNames); Assert.True(view.ShowEventLengths);
    });

    [Fact]
    public void OverflowKeepsTheClickedVideoActionsEnabledWhenGroupSelectionAddsOtherEvents() => Sta(() =>
    {
        var (view, track, clip) = Create();
        var linked = new TimelineClip { StartFrame = 30, DurationFrames = 60 };
        var secondVideo = new TimelineClip { StartFrame = 180, DurationFrames = 60 };
        track.Clips.Add(secondVideo);
        view.Project.Tracks.Add(new TimelineTrack { Kind = TrackKind.Audio, Clips = [linked] });
        view.SelectionChanged += () => { view.Selection.Add(linked.Id); view.Selection.Add(secondVideo.Id); };
        var menu = view.CreateEventMenu(clip.Id);
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        var crop = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Video event Pan/Crop…"));
        Assert.True(crop.IsEnabled);
        Guid invoked = Guid.Empty; view.VideoEventActionRequested += (id, _) => invoked = id;
        crop.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Equal(clip.Id, invoked);
    });

    [Fact]
    public void AutomationExposesEnabledInvokeButtonsUnderTheEvent() => Sta(() =>
    {
        var (view, track, clip) = Create();
        var peer = UIElementAutomationPeer.CreatePeerForElement(view)!;
        var clipPeer = peer.GetChildren().Single().GetChildren().Single(child => child.GetAutomationId() == $"Clip_{clip.Id:N}");
        var button = clipPeer.GetChildren().Single(child => child.GetAutomationId() == $"Clip_{clip.Id:N}_VideoEffects");
        Assert.Equal(AutomationControlType.Button, button.GetAutomationControlType());
        var invoked = false; var returned = false;
        view.VideoEventActionRequested += (id, action) => { Assert.True(returned); invoked = id == clip.Id && action == "VideoEffects"; };
        ((IInvokeProvider)button.GetPattern(PatternInterface.Invoke)).Invoke();
        returned = true; Assert.False(invoked); PumpDispatcher(); Assert.True(invoked);
        track.IsLocked = true; Assert.False(button.IsEnabled());
        Assert.Throws<ElementNotEnabledException>(() => ((IInvokeProvider)button.GetPattern(PatternInterface.Invoke)).Invoke());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedAutomationInvocationIgnoresEventsLockedOrDeletedBeforeDispatch(bool delete) => Sta(() =>
    {
        var (view, track, clip) = Create();
        var peer = UIElementAutomationPeer.CreatePeerForElement(view)!;
        var clipPeer = peer.GetChildren().Single().GetChildren().Single(child => child.GetAutomationId() == $"Clip_{clip.Id:N}");
        var button = clipPeer.GetChildren().Single(child => child.GetAutomationId() == $"Clip_{clip.Id:N}_PanCrop");
        var invoked = false; view.VideoEventActionRequested += (_, _) => invoked = true;
        ((IInvokeProvider)button.GetPattern(PatternInterface.Invoke)).Invoke();
        if (delete) track.Clips.Clear(); else track.IsLocked = true;
        PumpDispatcher(); Assert.False(invoked);
    });

    [Fact]
    public void CachedAutomationChildrenFollowZoomScrollAndViewportChanges() => Sta(() =>
    {
        var (view, _, clip) = Create(); view.PixelsPerSecond = 13.4;
        var peer = UIElementAutomationPeer.CreatePeerForElement(view)!;
        var clipPeer = peer.GetChildren().Single().GetChildren().Single(child => child.GetAutomationId() == $"Clip_{clip.Id:N}");
        Assert.Single(clipPeer.GetChildren());
        view.PixelsPerSecond = 100;
        Assert.Equal(3, clipPeer.GetChildren().Count);
        view.Offset = 270;
        Assert.EndsWith("_More", Assert.Single(clipPeer.GetChildren()).GetAutomationId());
        view.Offset = 0;
        view.Arrange(new Rect(0, 0, 500, 400));
        view.UpdateLayout();
        Assert.Empty(clipPeer.GetChildren());
        view.Arrange(new Rect(0, 0, 1000, 400));
        view.UpdateLayout();
        Assert.Equal(3, clipPeer.GetChildren().Count);
        view.VerticalOffset = 100;
        Assert.Empty(clipPeer.GetChildren());
    });

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Event toolbar check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
