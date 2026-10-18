using System.Text.Json.Serialization;

namespace OpenRend.Core;

public readonly record struct Rational
{
    public int Numerator { get; }
    public int Denominator { get; }

    [JsonConstructor]
    public Rational(int numerator, int denominator)
    {
        if (numerator <= 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var divisor = Gcd(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public double Value => (double)Numerator / Denominator;
    public double Seconds(long frames) => frames * (double)Denominator / Numerator;
    public long Frames(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        return checked((long)Math.Round(seconds * Numerator / Denominator, MidpointRounding.AwayFromZero));
    }

    public override string ToString() => $"{Numerator}/{Denominator}";
    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
}

public enum TrackKind { Video, Audio }

public sealed class ProjectDocument
{
    public string Name { get; set; } = "Untitled";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public Rational FrameRate { get; set; } = new(30, 1);
    public List<MediaAsset> Media { get; set; } = [];
    public List<TimelineTrack> Tracks { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<VideoEffect> VideoOutputEffects { get; set; } = [];
    [JsonIgnore] public long DurationFrames => Tracks.SelectMany(t => t.Clips).Select(c => c.EndFrame).DefaultIfEmpty(0).Max();

    public ProjectDocument DeepClone() => new()
    {
        Name = Name,
        Width = Width,
        Height = Height,
        FrameRate = FrameRate,
        Media = Media.Select(m => m.DeepClone()).ToList(),
        Tracks = Tracks.Select(t => t.DeepClone()).ToList(),
        VideoOutputEffects = VideoOutputEffects.Select(e => e.DeepClone()).ToList()
    };

    public void Validate()
    {
        if (Width is <= 0 or > 32768 || Height is <= 0 or > 32768) throw new InvalidDataException("Project dimensions must be between 1 and 32768.");
        if (FrameRate.Numerator <= 0 || FrameRate.Denominator <= 0) throw new InvalidDataException("Project frame rate must be positive.");
        if (Media == null || Tracks == null) throw new InvalidDataException("Project collections are missing.");
        if (VideoOutputEffects != null)
        {
            var outputEffectIds = new HashSet<Guid>();
            foreach (var effect in VideoOutputEffects)
            {
                if (effect == null || !outputEffectIds.Add(effect.Id)) throw new InvalidDataException("Video output effect IDs must be unique.");
                effect.Validate();
            }
        }
        var mediaIds = new HashSet<Guid>();
        var mediaById = new Dictionary<Guid, MediaAsset>();
        foreach (var media in Media)
        {
            if (media == null || media.Id == Guid.Empty || !mediaIds.Add(media.Id)) throw new InvalidDataException("Media IDs must be unique and non-empty.");
            if (!double.IsFinite(media.DurationSeconds) || media.DurationSeconds < 0) throw new InvalidDataException("Media duration is invalid.");
            if (!double.IsFinite(media.FrameRate) || media.FrameRate < 0 || media.Width < 0 || media.Height < 0) throw new InvalidDataException("Media metadata is invalid.");
            if (string.IsNullOrWhiteSpace(media.Path)) throw new InvalidDataException("Media path is empty.");
            if (media.VideoEffects != null)
            {
                var mediaEffectIds = new HashSet<Guid>();
                foreach (var effect in media.VideoEffects)
                {
                    if (effect == null || !mediaEffectIds.Add(effect.Id)) throw new InvalidDataException("Media video effect IDs must be unique.");
                    effect.Validate();
                }
            }
            mediaById.Add(media.Id, media);
        }
        var trackIds = new HashSet<Guid>();
        var clipIds = new HashSet<Guid>();
        foreach (var track in Tracks)
        {
            if (track == null || track.Id == Guid.Empty || !trackIds.Add(track.Id) || !Enum.IsDefined(track.Kind) || track.Clips == null) throw new InvalidDataException("Track is invalid.");
            if (!double.IsFinite(track.Gain) || track.Gain < 0 || !double.IsFinite(track.Opacity) || track.Opacity is < 0 or > 1 || !double.IsFinite(track.Pan) || track.Pan is < -1 or > 1) throw new InvalidDataException("Track levels are invalid.");
            if (track.VideoEffects != null)
            {
                if (track.Kind != TrackKind.Video && track.VideoEffects.Count != 0)
                    throw new InvalidDataException("Track video effects require a video track.");
                var trackEffectIds = new HashSet<Guid>();
                foreach (var effect in track.VideoEffects)
                {
                    if (effect == null || !trackEffectIds.Add(effect.Id)) throw new InvalidDataException("Track video effect IDs must be unique within a track.");
                    effect.Validate();
                }
            }
            foreach (var clip in track.Clips)
            {
                if (clip == null || clip.Id == Guid.Empty || !clipIds.Add(clip.Id)) throw new InvalidDataException("Clip IDs must be unique and non-empty.");
                if (!mediaIds.Contains(clip.MediaId)) throw new InvalidDataException("A clip references unknown media.");
                if (clip.StartFrame < 0 || clip.SourceInFrame < 0 && clip.VideoSourceMode != VideoSourceMode.Loop || clip.DurationFrames <= 0 || clip.StartFrame > long.MaxValue - clip.DurationFrames || clip.SourceInFrame > long.MaxValue - clip.DurationFrames) throw new InvalidDataException("Clip range is invalid.");
                var source = mediaById[clip.MediaId];
                if (track.Kind == TrackKind.Video && !source.HasVideo || track.Kind == TrackKind.Audio && !source.HasAudio) throw new InvalidDataException("A clip has no compatible media stream.");
                if (!Enum.IsDefined(clip.VideoSourceMode) || clip.VideoSourceMode != VideoSourceMode.Bounded &&
                    (track.Kind != TrackKind.Video || source.DurationSeconds < 1e-28 || source.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Source playback modes require a finite video source.");
                var available = FrameRate.Frames(source.DurationSeconds);
                if (clip.VideoSourceMode == VideoSourceMode.Bounded && available > 0 && clip.SourceInFrame + clip.DurationFrames > available) throw new InvalidDataException("A clip extends beyond its source media.");
                if (!double.IsFinite(clip.Gain) || clip.Gain < 0 || !double.IsFinite(clip.Opacity) || clip.Opacity is < 0 or > 1 || !double.IsFinite(clip.Pan) || clip.Pan is < -1 or > 1) throw new InvalidDataException("Clip levels are invalid.");
                var envelope = clip.FadeDurationFrames ?? clip.DurationFrames;
                if (envelope <= 0 || clip.FadeOffsetFrames < 0 || clip.FadeOffsetFrames > envelope - clip.DurationFrames || clip.FadeInFrames < 0 || clip.FadeOutFrames < 0 || clip.FadeInFrames > envelope || clip.FadeOutFrames > envelope - clip.FadeInFrames) throw new InvalidDataException("Clip fade range is invalid.");
                if (track.Kind != TrackKind.Video && (clip.FadeInTransition is not null || clip.FadeOutTransition is not null))
                    throw new InvalidDataException("Video transitions require a video clip.");
                if (!Enum.IsDefined(clip.FadeInCurve) || !Enum.IsDefined(clip.FadeOutCurve) ||
                    !Enum.IsDefined(clip.FadeInReciprocalCurve) || !Enum.IsDefined(clip.FadeOutReciprocalCurve) ||
                    track.Kind != TrackKind.Video && (clip.FadeInCurve != VideoFadeCurve.Linear || clip.FadeOutCurve != VideoFadeCurve.Linear ||
                        clip.FadeInReciprocalCurve != VideoFadeCurve.Linear || clip.FadeOutReciprocalCurve != VideoFadeCurve.Linear))
                    throw new InvalidDataException("Video fade curves require a video clip and a supported shape.");
                clip.FadeInTransition?.Validate();
                clip.FadeOutTransition?.Validate();
                if (clip.Crop is not null)
                {
                    if (track.Kind != TrackKind.Video || clip.CropOffsetFrames > long.MaxValue - clip.DurationFrames) throw new InvalidDataException("Clip crop range is invalid.");
                    clip.Crop.Validate();
                }
                else if (clip.CropOffsetFrames != 0) throw new InvalidDataException("A crop offset requires crop keyframes.");
                if (clip.VideoEffects == null || track.Kind != TrackKind.Video && clip.VideoEffects.Count != 0)
                    throw new InvalidDataException("Video effects require a video clip.");
                var effectIds = new HashSet<Guid>();
                foreach (var effect in clip.VideoEffects)
                {
                    if (effect == null || !effectIds.Add(effect.Id)) throw new InvalidDataException("Video effect IDs must be unique within a clip.");
                    effect.Validate();
                }
                if (clip.MotionTracks != null)
                {
                    var mtIds = new HashSet<Guid>();
                    foreach (var mt in clip.MotionTracks)
                    {
                        if (mt == null || !mtIds.Add(mt.Id)) throw new InvalidDataException("Motion track IDs must be unique within a clip.");
                        mt.Validate();
                    }
                }
                clip.Transcript?.Validate();
            }
        }
    }
}

public sealed class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = "";
    [JsonIgnore] public string Name => System.IO.Path.GetFileName(Path);
    public double DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<VideoEffect> VideoEffects { get; set; } = [];
    public MediaAsset DeepClone()
    {
        var clone = (MediaAsset)MemberwiseClone();
        clone.VideoEffects = VideoEffects.Select(e => e.DeepClone()).ToList();
        return clone;
    }
}

public sealed class TimelineTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Track";
    public TrackKind Kind { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }
    public bool IsLocked { get; set; }
    public bool IsVisible { get; set; } = true;
    public double Gain { get; set; } = 1;
    public double Opacity { get; set; } = 1;
    public double Pan { get; set; }
    public List<TimelineClip> Clips { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<VideoEffect> VideoEffects { get; set; } = [];
    public TimelineTrack DeepClone() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        IsMuted = IsMuted,
        IsSolo = IsSolo,
        IsLocked = IsLocked,
        IsVisible = IsVisible,
        Gain = Gain,
        Opacity = Opacity,
        Pan = Pan,
        Clips = Clips.Select(c => c.DeepClone()).ToList(),
        VideoEffects = VideoEffects.Select(e => e.DeepClone()).ToList()
    };
}

public sealed class TimelineClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GroupId { get; set; }
    public Guid MediaId { get; set; }
    public string Name { get; set; } = "Clip";
    public long StartFrame { get; set; }
    public long SourceInFrame { get; set; }
    public long DurationFrames { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public VideoSourceMode VideoSourceMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsLocked { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsMuted { get; set; }
    [JsonIgnore] public long EndFrame => checked(StartFrame + DurationFrames);
    public double Gain { get; set; } = 1;
    public double Opacity { get; set; } = 1;
    public long FadeInFrames { get; set; }
    public long FadeOutFrames { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public VideoFadeCurve FadeInCurve { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public VideoFadeCurve FadeOutCurve { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public VideoFadeCurve FadeInReciprocalCurve { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public VideoFadeCurve FadeOutReciprocalCurve { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public VideoTransition? FadeInTransition { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public VideoTransition? FadeOutTransition { get; set; }
    public long FadeOffsetFrames { get; set; }
    public long? FadeDurationFrames { get; set; }
    public double Pan { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CropAnimation? Crop { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long CropOffsetFrames { get; set; }
    public List<VideoEffect> VideoEffects { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MotionTrackData> MotionTracks { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClipTranscript? Transcript { get; set; }
    public TimelineClip DeepClone()
    {
        var clone = (TimelineClip)MemberwiseClone();
        clone.Crop = Crop?.DeepClone();
        clone.FadeInTransition = FadeInTransition?.DeepClone();
        clone.FadeOutTransition = FadeOutTransition?.DeepClone();
        clone.VideoEffects = VideoEffects.Select(effect => effect.DeepClone()).ToList();
        clone.MotionTracks = MotionTracks.Select(track => track.DeepClone()).ToList();
        clone.Transcript = Transcript?.DeepClone();
        return clone;
    }
}

public enum VideoTransitionKind
{
    Dissolve,
    LinearWipe,
    BarnDoor,
    Push,
    Slide,
    Zoom,
    Flash,
    Iris,
    Split,
    ClockWipe,
    VenetianBlinds,
    CrossEffect,
    Swap,
    Squeeze,
    StarWipe,
    PageLoop,
    PagePeel,
    PageRoll
}
public enum CutTransitionAlignment { BeforeCut, CenteredOnCut, AfterCut }

public sealed class VideoTransition
{
    public VideoTransitionKind Kind { get; set; } = VideoTransitionKind.Dissolve;
    public string Preset { get; set; } = "Default";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Parameters { get; set; }

    public VideoTransition DeepClone()
    {
        var clone = (VideoTransition)MemberwiseClone();
        if (Parameters != null)
            clone.Parameters = new Dictionary<string, string>(Parameters);
        return clone;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new InvalidDataException("Video transition is not supported.");
        if (string.IsNullOrWhiteSpace(Preset))
            throw new InvalidDataException("Video transition preset is missing.");
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<ChannelRemapping>))]
public enum ChannelRemapping
{
    None = 0,
    Mono = 1,
    Swap = 2,
    MuteLeft = 3,
    MuteRight = 4
}
