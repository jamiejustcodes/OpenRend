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

[JsonConverter(typeof(JsonStringEnumConverter<TrackCompositeMode>))]
public enum TrackCompositeMode
{
    SourceAlpha = 2,
    Add = 4,
    Subtract = 5,
    Multiply = 6,
    Cut = 7,
    Screen = 8,
    Overlay = 9,
    HardLight = 10,
    Dodge = 11,
    Burn = 12,
    Darken = 13,
    Lighten = 14,
    Difference = 15,
    DifferenceSquared = 16
}

[JsonConverter(typeof(JsonStringEnumConverter<VideoFieldOrder>))]
public enum VideoFieldOrder
{
    ProgressiveScan = 0,
    UpperFieldFirst = 1,
    LowerFieldFirst = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<VideoRenderQuality>))]
public enum VideoRenderQuality
{
    Draft = 1,
    Preview = 2,
    Good = 3,
    Best = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<VideoDeinterlaceMethod>))]
public enum VideoDeinterlaceMethod
{
    None = 0,
    BlendFields = 1,
    InterpolateFields = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<ProjectPixelFormat>))]
public enum ProjectPixelFormat
{
    Int8Bit = 0,
    Int8BitFullRange = 1,
    Float32Bit = 2,
    Float32BitFullRange = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<HDRMode>))]
public enum HDRMode
{
    Off = 2,
    HDR10 = 0,
    HLG = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<MotionBlurType>))]
public enum MotionBlurType
{
    Gaussian = 0,
    Pyramid = 1,
    Box = 2
}

public sealed class ProjectDocument
{
    public string Name { get; set; } = "Untitled";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public Rational FrameRate { get; set; } = new(30, 1);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public VideoFieldOrder FieldOrder { get; set; } = VideoFieldOrder.ProgressiveScan;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double PixelAspectRatio { get; set; } = 1.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public VideoRenderQuality RenderQuality { get; set; } = VideoRenderQuality.Good;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MotionBlurType MotionBlur { get; set; } = MotionBlurType.Gaussian;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public VideoDeinterlaceMethod DeinterlaceMethod { get; set; } = VideoDeinterlaceMethod.BlendFields;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ProjectPixelFormat PixelFormat { get; set; } = ProjectPixelFormat.Int8Bit;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public HDRMode HDRMode { get; set; } = HDRMode.Off;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AudioSampleRate { get; set; } = 48000;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AudioBitDepth { get; set; } = 24;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrerenderFolder { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RulerTimeFormat { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Author { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Copyright { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Comments { get; set; }
    public List<MediaAsset> Media { get; set; } = [];
    public List<TimelineTrack> Tracks { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<VideoEffect> VideoOutputEffects { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<AudioEffect> MasterAudioEffects { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double MasterAudioGain { get; set; } = 1.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MasterAudioMute { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MasterAudioDim { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<TimelineMarker> Markers { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ProjectNoteItem> Notes { get; set; } = [];
    [JsonIgnore] public long DurationFrames => Tracks.SelectMany(t => t.Clips).Select(c => c.EndFrame).DefaultIfEmpty(0).Max();

    public ProjectDocument DeepClone() => new()
    {
        Name = Name,
        Width = Width,
        Height = Height,
        FrameRate = FrameRate,
        FieldOrder = FieldOrder,
        PixelAspectRatio = PixelAspectRatio,
        RenderQuality = RenderQuality,
        MotionBlur = MotionBlur,
        DeinterlaceMethod = DeinterlaceMethod,
        PixelFormat = PixelFormat,
        HDRMode = HDRMode,
        AudioSampleRate = AudioSampleRate,
        AudioBitDepth = AudioBitDepth,
        PrerenderFolder = PrerenderFolder,
        RulerTimeFormat = RulerTimeFormat,
        Author = Author,
        Copyright = Copyright,
        Comments = Comments,
        Media = Media.Select(m => m.DeepClone()).ToList(),
        Tracks = Tracks.Select(t => t.DeepClone()).ToList(),
        VideoOutputEffects = VideoOutputEffects.Select(e => e.DeepClone()).ToList(),
        MasterAudioEffects = MasterAudioEffects.Select(e => e.DeepClone()).ToList(),
        MasterAudioGain = MasterAudioGain,
        MasterAudioMute = MasterAudioMute,
        MasterAudioDim = MasterAudioDim,
        Markers = Markers.Select(m => m.DeepClone()).ToList(),
        Notes = Notes.Select(n => n.DeepClone()).ToList()
    };

    public void Validate()
    {
        if (Width is <= 0 or > 32768 || Height is <= 0 or > 32768) throw new InvalidDataException("Project dimensions must be between 1 and 32768.");
        if (FrameRate.Numerator <= 0 || FrameRate.Denominator <= 0) throw new InvalidDataException("Project frame rate must be positive.");
        if (!double.IsFinite(PixelAspectRatio) || PixelAspectRatio <= 0 || PixelAspectRatio > 4.0)
            throw new InvalidDataException("Project pixel aspect ratio must be positive and not exceed 4.0.");
        if (AudioSampleRate is < 8000 or > 192000)
            throw new InvalidDataException("Project audio sample rate must be between 8000 and 192000 Hz.");
        if (AudioBitDepth is not (16 or 24 or 32))
            throw new InvalidDataException("Project audio bit depth must be 16, 24, or 32.");
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
        if (!double.IsFinite(MasterAudioGain) || MasterAudioGain < 0) throw new InvalidDataException("Master audio gain is invalid.");
        if (MasterAudioEffects != null)
        {
            var masterAudioEffectIds = new HashSet<Guid>();
            foreach (var effect in MasterAudioEffects)
            {
                if (effect == null || !masterAudioEffectIds.Add(effect.Id)) throw new InvalidDataException("Master audio effect IDs must be unique.");
                effect.Validate();
            }
        }
        if (Markers != null)
        {
            var markerIds = new HashSet<Guid>();
            foreach (var marker in Markers)
            {
                if (marker == null || !markerIds.Add(marker.Id)) throw new InvalidDataException("Marker IDs must be unique.");
                marker.Validate();
            }
        }
        if (Notes != null)
        {
            var noteIds = new HashSet<Guid>();
            foreach (var note in Notes)
            {
                if (note == null || !noteIds.Add(note.Id)) throw new InvalidDataException("Project note IDs must be unique.");
                note.Validate();
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
            if (media.IsGenerated && media.Generator is not null)
            {
                media.Generator.Validate();
            }
            mediaById.Add(media.Id, media);
        }
        var trackIds = new HashSet<Guid>();
        var clipIds = new HashSet<Guid>();
        foreach (var track in Tracks)
        {
            if (track == null || track.Id == Guid.Empty || !trackIds.Add(track.Id) || !Enum.IsDefined(track.Kind) || track.Clips == null) throw new InvalidDataException("Track is invalid.");
            if (!Enum.IsDefined(track.CompositeMode)) throw new InvalidDataException("Track composite mode is invalid.");
            if (track.Kind != TrackKind.Video && track.CompositeMode != TrackCompositeMode.SourceAlpha)
                throw new InvalidDataException("Audio tracks only support SourceAlpha composite mode.");
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
            if (track.AudioEffects != null)
            {
                if (track.Kind != TrackKind.Audio && track.AudioEffects.Count != 0)
                    throw new InvalidDataException("Track audio effects require an audio track.");
                var trackAudioEffectIds = new HashSet<Guid>();
                foreach (var effect in track.AudioEffects)
                {
                    if (effect == null || !trackAudioEffectIds.Add(effect.Id)) throw new InvalidDataException("Track audio effect IDs must be unique within a track.");
                    effect.Validate();
                }
            }
            if (track.Envelopes != null)
            {
                var trackEnvTypes = new HashSet<EnvelopeType>();
                foreach (var env in track.Envelopes)
                {
                    if (env == null || !trackEnvTypes.Add(env.Type)) throw new InvalidDataException("Track envelope types must be unique within a track.");
                    env.Validate();
                    if (track.Kind == TrackKind.Video && env.Type is not (EnvelopeType.Composite or EnvelopeType.FadeToColor or EnvelopeType.Mute))
                        throw new InvalidDataException("Video tracks only support Composite, FadeToColor, and Mute envelopes.");
                    if (track.Kind == TrackKind.Audio && env.Type is not (EnvelopeType.Volume or EnvelopeType.Pan or EnvelopeType.Mute))
                        throw new InvalidDataException("Audio tracks only support Volume, Pan, and Mute envelopes.");
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
                if (!double.IsFinite(clip.PlaybackRate) || Math.Abs(clip.PlaybackRate) < 0.05 || Math.Abs(clip.PlaybackRate) > 10.0) throw new InvalidDataException("Clip playback rate must be between 0.05 and 10.0.");
                if (!double.IsFinite(clip.UnderSampleRate) || clip.UnderSampleRate < 0.001 || clip.UnderSampleRate > 1.0) throw new InvalidDataException("Clip undersample rate must be between 0.001 and 1.0.");
                if (!Enum.IsDefined(clip.ResampleMode)) throw new InvalidDataException("Clip resample mode is invalid.");
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
                if (clip.Envelopes != null)
                {
                    var envTypes = new HashSet<EnvelopeType>();
                    foreach (var env in clip.Envelopes)
                    {
                        if (env == null || !envTypes.Add(env.Type)) throw new InvalidDataException("Envelope types must be unique within a clip.");
                        env.Validate();
                    }
                }
                if (clip.Takes != null)
                {
                    var takeIds = new HashSet<Guid>();
                    foreach (var take in clip.Takes)
                    {
                        if (take == null || take.Id == Guid.Empty || !takeIds.Add(take.Id))
                            throw new InvalidDataException("Take IDs must be unique and non-empty.");
                        if (!mediaIds.Contains(take.MediaId))
                            throw new InvalidDataException("A take references unknown media.");
                        var takeSource = mediaById[take.MediaId];
                        if (track.Kind == TrackKind.Video && !takeSource.HasVideo || track.Kind == TrackKind.Audio && !takeSource.HasAudio)
                            throw new InvalidDataException("A take has no compatible media stream.");
                    }
                    if (clip.Takes.Count > 0 && (clip.ActiveTakeIndex < 0 || clip.ActiveTakeIndex >= clip.Takes.Count))
                        throw new InvalidDataException("Active take index is out of bounds.");
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
    [JsonIgnore] public string Name => IsGenerated && Generator != null ? $"{Generator.Title} ({Generator.PresetName})" : System.IO.Path.GetFileName(Path);
    public bool IsGenerated { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MediaGeneratorData? Generator { get; set; }
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
        clone.Generator = Generator?.DeepClone();
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TrackCompositeMode CompositeMode { get; set; } = TrackCompositeMode.SourceAlpha;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TrackMotion? Motion { get; set; }
    public List<TimelineClip> Clips { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<AudioEffect> AudioEffects { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool InvertPhase { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<VideoEffect> VideoEffects { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ClipEnvelope>? Envelopes { get; set; }
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
        CompositeMode = CompositeMode,
        Motion = Motion?.DeepClone(),
        Clips = Clips.Select(c => c.DeepClone()).ToList(),
        VideoEffects = VideoEffects.Select(e => e.DeepClone()).ToList(),
        AudioEffects = AudioEffects.Select(e => e.DeepClone()).ToList(),
        InvertPhase = InvertPhase,
        Envelopes = Envelopes?.Select(e => e.DeepClone()).ToList()
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<VideoResampleMode>))]
public enum VideoResampleMode
{
    Smart = 0,
    Force = 1,
    Disable = 2
}
public sealed class TimelineTake
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MediaId { get; set; }
    public string Name { get; set; } = "Take";
    public long SourceInFrame { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public VideoSourceMode VideoSourceMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsReversed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double PlaybackRate { get; set; } = 1.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double UnderSampleRate { get; set; } = 1.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public VideoResampleMode ResampleMode { get; set; } = VideoResampleMode.Smart;
    public bool MaintainAspectRatio { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool ReduceInterlace { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Normalize { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool InvertPhase { get; set; }

    public TimelineTake DeepClone() => new()
    {
        Id = Id,
        MediaId = MediaId,
        Name = Name,
        SourceInFrame = SourceInFrame,
        VideoSourceMode = VideoSourceMode,
        IsReversed = IsReversed,
        PlaybackRate = PlaybackRate
    };

    public void Validate()
    {
        if (Id == Guid.Empty || MediaId == Guid.Empty)
            throw new InvalidDataException("Take IDs must be non-empty.");
    }
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsReversed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double PlaybackRate { get; set; } = 1.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double UnderSampleRate { get; set; } = 1.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public VideoResampleMode ResampleMode { get; set; } = VideoResampleMode.Smart;
    public bool MaintainAspectRatio { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool ReduceInterlace { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Normalize { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool InvertPhase { get; set; }
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClipEnvelope>? Envelopes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TimelineTake>? Takes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ActiveTakeIndex { get; set; }
    public TimelineClip DeepClone()
    {
        var clone = (TimelineClip)MemberwiseClone();
        clone.Crop = Crop?.DeepClone();
        clone.FadeInTransition = FadeInTransition?.DeepClone();
        clone.FadeOutTransition = FadeOutTransition?.DeepClone();
        clone.VideoEffects = VideoEffects.Select(effect => effect.DeepClone()).ToList();
        clone.MotionTracks = MotionTracks.Select(track => track.DeepClone()).ToList();
        clone.Transcript = Transcript?.DeepClone();
        if (Envelopes != null) clone.Envelopes = Envelopes.Select(env => env.DeepClone()).ToList();
        if (Takes != null) clone.Takes = Takes.Select(t => t.DeepClone()).ToList();
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
