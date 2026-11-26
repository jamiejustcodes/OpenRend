using System.Windows.Input;

namespace OpenRend.App;

public partial class MainWindow
{
    private double activeTimelineRate;
    private bool timelineShuttle;
    private Key heldShuttleDirection;

    private void ResetShuttleState()
    {
        activeTimelineRate = 0;
        timelineShuttle = false;
        heldShuttleDirection = Key.None;
    }

    private bool HandleShuttleKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || e.Key is not (Key.J or Key.K or Key.L))
            return false;
        e.Handled = true;
        if (e.IsRepeat) return true;

        if (e.Key == Key.K)
        {
            if (Keyboard.IsKeyDown(Key.L)) ShuttleTimeline(Key.L, slow: true);
            else if (Keyboard.IsKeyDown(Key.J)) ShuttleTimeline(Key.J, slow: true);
            else Pause();
        }
        else
            ShuttleTimeline(e.Key, Keyboard.IsKeyDown(Key.K));
        return true;
    }

    private async void ShuttleTimeline(Key direction, bool slow) => await GuardAsync(async () =>
    {
        var sign = direction == Key.J ? -1 : 1;
        var previousSpeed = activeTimelineRate * sign;
        var speed = slow ? .25 : previousSpeed >= 1 ? Math.Min(16, previousSpeed * 2) : 1;
        var rate = sign * speed;
        if (timelineShuttle && rate == activeTimelineRate) return;

        var rememberOrigin = activeTimelineRate == 0;
        Pause();
        if (sign < 0 && vm.Position == 0 || sign > 0 && vm.Position == vm.Project.DurationFrames)
            return;

        heldShuttleDirection = slow ? direction : Key.None;
        await StartTimelinePlaybackAsync(rememberOrigin, rate, shuttle: true);
    });

    private void ShuttleKeyUp(object sender, KeyEventArgs e)
    {
        if (heldShuttleDirection == Key.None || (e.Key != Key.K && e.Key != heldShuttleDirection))
            return;
        e.Handled = true;
        Pause();
    }

    private void EndHeldShuttle(object? sender, EventArgs e)
    {
        if (heldShuttleDirection != Key.None)
            Pause();
    }
}
