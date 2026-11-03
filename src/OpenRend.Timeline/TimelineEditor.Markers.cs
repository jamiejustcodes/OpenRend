using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public TimelineMarker AddMarker(long frame, string label = "", int colorId = 0)
    {
        var clampedFrame = Math.Max(0, frame);
        var nextIndex = Project.Markers.Count == 0 ? 1 : Project.Markers.Max(m => m.Index) + 1;
        var marker = new TimelineMarker
        {
            Index = nextIndex,
            Frame = clampedFrame,
            Label = label ?? "",
            ColorId = Math.Clamp(colorId, 0, TimelineMarker.MarkerColors.Length - 1)
        };
        Execute(p =>
        {
            p.Markers.Add(marker);
            p.Markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
        return marker;
    }

    public bool RemoveMarker(Guid markerId)
    {
        if (Project.Markers.All(m => m.Id != markerId)) return false;
        Execute(p => p.Markers.RemoveAll(m => m.Id == markerId));
        return true;
    }

    public bool SetMarkerLabel(Guid markerId, string label)
    {
        var target = Project.Markers.FirstOrDefault(m => m.Id == markerId);
        if (target == null) return false;
        Execute(p =>
        {
            var m = p.Markers.First(x => x.Id == markerId);
            m.Label = label ?? "";
        });
        return true;
    }

    public bool SetMarkerFrame(Guid markerId, long frame)
    {
        var target = Project.Markers.FirstOrDefault(m => m.Id == markerId);
        if (target == null) return false;
        var clamped = Math.Max(0, frame);
        Execute(p =>
        {
            var m = p.Markers.First(x => x.Id == markerId);
            m.Frame = clamped;
            p.Markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
        return true;
    }

    public ProjectNoteItem AddNote(string noteText, long frame, string userLabel = "", int colorId = 0, Guid? markerId = null)
    {
        var clampedFrame = Math.Max(0, frame);
        var timecode = FormatMarkerTimecode(clampedFrame, Project.FrameRate);
        var note = new ProjectNoteItem
        {
            Note = noteText ?? "",
            Frame = clampedFrame,
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            TimecodeMarker = timecode,
            OverrideLabel = userLabel ?? "",
            UserOverrideLabel = !string.IsNullOrWhiteSpace(userLabel),
            OverrideColorId = Math.Clamp(colorId, 0, TimelineMarker.MarkerColors.Length - 1),
            UseOverrideColor = colorId > 0,
            Status = ProjectNoteStatus.Active,
            MarkerId = markerId
        };
        Execute(p =>
        {
            p.Notes.Add(note);
            p.Notes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
        return note;
    }

    public bool RemoveNote(Guid noteId)
    {
        if (Project.Notes.All(n => n.Id != noteId)) return false;
        Execute(p => p.Notes.RemoveAll(n => n.Id == noteId));
        return true;
    }

    public bool SetNoteStatus(Guid noteId, ProjectNoteStatus status)
    {
        var target = Project.Notes.FirstOrDefault(n => n.Id == noteId);
        if (target == null) return false;
        Execute(p =>
        {
            var n = p.Notes.First(x => x.Id == noteId);
            n.Status = status;
        });
        return true;
    }

    public bool SetNoteText(Guid noteId, string noteText)
    {
        var target = Project.Notes.FirstOrDefault(n => n.Id == noteId);
        if (target == null) return false;
        Execute(p =>
        {
            var n = p.Notes.First(x => x.Id == noteId);
            n.Note = noteText ?? "";
        });
        return true;
    }

    public void ClearNotes()
    {
        if (Project.Notes.Count == 0) return;
        Execute(p => p.Notes.Clear());
    }

    private static string FormatMarkerTimecode(long frame, Rational rate)
    {
        if (rate.Numerator <= 0 || rate.Denominator <= 0) rate = new Rational(30, 1);
        var totalSeconds = (double)frame * rate.Denominator / rate.Numerator;
        var hours = (int)(totalSeconds / 3600);
        var minutes = (int)((totalSeconds % 3600) / 60);
        var seconds = (int)(totalSeconds % 60);
        var frames = (int)(frame % Math.Max(1, (long)Math.Round(rate.Value)));
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}:{frames:D2}";
    }
}

