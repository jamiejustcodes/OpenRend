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
    SourceAlpha3D = 3,
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
    public RulerFormat RulerFormat { get; set; } = RulerFormat.TimeAndFrames;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RulerTimeFormat
    {
        get => RulerFormat.ToDisplayName();
        set
        {
            if (value is not null && RulerFormatExtensions.TryParseDisplayName(value, out var parsed))
                RulerFormat = parsed;
        }
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long RulerStartTimeFrames { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double BeatsPerMinute { get; set; } = 120.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BeatsPerMeasure { get; set; } = 4;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BeatValue { get; set; } = 4;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Author { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Copyright { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Comments { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Engineer { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UniversalAlbumId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordedFilesFolder { get; set; }
    public List<MediaAsset> Media { get; set; } = [];
    public List<TimelineTrack> Tracks { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<VideoEffect> VideoOutputEffects { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<AudioEffect> MasterAudioEffects { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClipEnvelope>? MasterAudioEnvelopes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double MasterAudioGain { get; set; } = 1.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MasterAudioMute { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MasterAudioDim { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<TimelineMarker> Markers { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<TimelineRegion> Regions { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ProjectNoteItem> Notes { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MediaBin> MediaBins { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<AudioBusTrack> AudioBuses { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<TrackGroup> TrackGroups { get; set; } = [];
    [JsonIgnore]
    public long DurationFrames
    {
        get
        {
            long max = 0;
            for (var t = 0; t < Tracks.Count; t++)
            {
                var clips = Tracks[t].Clips;
                for (var c = 0; c < clips.Count; c++)
                {
                    var end = clips[c].EndFrame;
                    if (end > max) max = end;
                }
            }
            return max;
        }
    }

    public ProjectDocument DeepClone()
    {
        var clone = new ProjectDocument
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
            RulerFormat = RulerFormat,
            RulerTimeFormat = RulerTimeFormat,
            RulerStartTimeFrames = RulerStartTimeFrames,
            BeatsPerMinute = BeatsPerMinute,
            BeatsPerMeasure = BeatsPerMeasure,
            BeatValue = BeatValue,
            Author = Author,
            Copyright = Copyright,
            Comments = Comments,
            Engineer = Engineer,
            UniversalAlbumId = UniversalAlbumId,
            RecordedFilesFolder = RecordedFilesFolder,
            MasterAudioGain = MasterAudioGain,
            MasterAudioMute = MasterAudioMute,
            MasterAudioDim = MasterAudioDim
        };

        clone.Media = new List<MediaAsset>(Media.Count);
        for (var i = 0; i < Media.Count; i++) clone.Media.Add(Media[i].DeepClone());

        clone.Tracks = new List<TimelineTrack>(Tracks.Count);
        for (var i = 0; i < Tracks.Count; i++) clone.Tracks.Add(Tracks[i].DeepClone());

        clone.VideoOutputEffects = new List<VideoEffect>(VideoOutputEffects.Count);
        for (var i = 0; i < VideoOutputEffects.Count; i++) clone.VideoOutputEffects.Add(VideoOutputEffects[i].DeepClone());

        clone.MasterAudioEffects = new List<AudioEffect>(MasterAudioEffects.Count);
        for (var i = 0; i < MasterAudioEffects.Count; i++) clone.MasterAudioEffects.Add(MasterAudioEffects[i].DeepClone());

        if (MasterAudioEnvelopes != null)
        {
            clone.MasterAudioEnvelopes = new List<ClipEnvelope>(MasterAudioEnvelopes.Count);
            for (var i = 0; i < MasterAudioEnvelopes.Count; i++) clone.MasterAudioEnvelopes.Add(MasterAudioEnvelopes[i].DeepClone());
        }

        clone.Markers = new List<TimelineMarker>(Markers.Count);
        for (var i = 0; i < Markers.Count; i++) clone.Markers.Add(Markers[i].DeepClone());

        clone.Regions = new List<TimelineRegion>(Regions.Count);
        for (var i = 0; i < Regions.Count; i++) clone.Regions.Add(Regions[i].DeepClone());

        clone.Notes = new List<ProjectNoteItem>(Notes.Count);
        for (var i = 0; i < Notes.Count; i++) clone.Notes.Add(Notes[i].DeepClone());

        clone.MediaBins = new List<MediaBin>(MediaBins.Count);
        for (var i = 0; i < MediaBins.Count; i++) clone.MediaBins.Add(MediaBins[i].DeepClone());

        clone.AudioBuses = new List<AudioBusTrack>(AudioBuses.Count);
        for (var i = 0; i < AudioBuses.Count; i++) clone.AudioBuses.Add(AudioBuses[i].DeepClone());

        clone.TrackGroups = new List<TrackGroup>(TrackGroups.Count);
        for (var i = 0; i < TrackGroups.Count; i++) clone.TrackGroups.Add(TrackGroups[i].DeepClone());

        return clone;
    }

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
        if (MasterAudioEnvelopes != null)
        {
            var masterEnvTypes = new HashSet<EnvelopeType>();
            foreach (var env in MasterAudioEnvelopes)
            {
                if (env == null || !masterEnvTypes.Add(env.Type)) throw new InvalidDataException("Master audio envelope types must be unique.");
                if (env.Type is not (EnvelopeType.Volume or EnvelopeType.Mute))
                    throw new InvalidDataException($"Envelope type '{env.Type}' is not supported on the master audio bus.");
                env.Validate();
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
        if (Regions != null)
        {
            var regionIds = new HashSet<Guid>();
            foreach (var region in Regions)
            {
                if (region == null || !regionIds.Add(region.Id)) throw new InvalidDataException("Region IDs must be unique.");
                region.Validate();
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
        if (MediaBins != null)
        {
            var binIds = new HashSet<Guid>();
            foreach (var bin in MediaBins)
            {
                if (bin == null || !binIds.Add(bin.Id)) throw new InvalidDataException("Project media bin IDs must be unique.");
                bin.Validate();
            }
        }
        if (AudioBuses != null)
        {
            var busIds = new HashSet<Guid>();
            foreach (var bus in AudioBuses)
            {
                if (bus == null || !busIds.Add(bus.Id)) throw new InvalidDataException("Audio bus IDs must be unique.");
                bus.Validate();
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
        var hasSeenVideoTrack = false;
        var prevVideoNestingLevel = 0;
        foreach (var track in Tracks)
        {
            if (track == null || track.Id == Guid.Empty || !trackIds.Add(track.Id) || !Enum.IsDefined(track.Kind) || track.Clips == null) throw new InvalidDataException("Track is invalid.");
            if (!Enum.IsDefined(track.CompositeMode)) throw new InvalidDataException("Track composite mode is invalid.");
            if (track.Kind != TrackKind.Video && track.CompositeMode != TrackCompositeMode.SourceAlpha)
                throw new InvalidDataException("Audio tracks only support SourceAlpha composite mode.");
            if (track.CompositeNestingLevel < 0) throw new InvalidDataException("Composite nesting level cannot be negative.");
            if (track.Kind != TrackKind.Video && track.CompositeNestingLevel > 0)
                throw new InvalidDataException("Composite nesting is only supported on video tracks.");
            if (track.Kind != TrackKind.Video && track.IsAdjustmentTrack)
                throw new InvalidDataException("Adjustment tracks must be video tracks.");
            if (track.BusTrackId.HasValue && track.Kind != TrackKind.Audio)
                throw new InvalidDataException("Bus routing requires an audio track.");
            if (track.BusTrackId.HasValue && (AudioBuses == null || !AudioBuses.Any(b => b.Id == track.BusTrackId.Value)))
                throw new InvalidDataException("Track references an unknown audio bus.");
            if (track.Kind == TrackKind.Video)
            {
                if (!hasSeenVideoTrack)
                {
                    if (track.CompositeNestingLevel != 0)
                        throw new InvalidDataException("The first video track cannot be a compositing child.");
                    hasSeenVideoTrack = true;
                    prevVideoNestingLevel = 0;
                }
                else
                {
                    if (track.CompositeNestingLevel > prevVideoNestingLevel + 1)
                        throw new InvalidDataException($"Track '{track.Name}' nesting level ({track.CompositeNestingLevel}) cannot exceed previous level + 1 ({prevVideoNestingLevel + 1}).");
                    prevVideoNestingLevel = track.CompositeNestingLevel;
                }
            }
            if (!double.IsFinite(track.Gain) || track.Gain < 0 || !double.IsFinite(track.Opacity) || track.Opacity is < 0 or > 1 || !double.IsFinite(track.Pan) || track.Pan is < -1 or > 1) throw new InvalidDataException("Track levels are invalid.");
            if (!Enum.IsDefined(track.PanMode)) throw new InvalidDataException("Track pan mode is invalid.");
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
                if (clip.SnapOffsetFrames < 0 || (clip.DurationFrames > 0 && clip.SnapOffsetFrames >= clip.DurationFrames)) throw new InvalidDataException("Clip snap offset must be within clip duration.");
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
                if (clip.Mask is not null)
                {
                    if (track.Kind != TrackKind.Video) throw new InvalidDataException("Mask animation requires a video clip.");
                    clip.Mask.Validate();
                }
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
                if (clip.IsAdjustmentEvent)
                {
                    if (track.Kind != TrackKind.Video)
                        throw new InvalidDataException("Adjustment events are only supported on video tracks.");
                    if (clip.FrameWidth < 0 || clip.FrameHeight < 0)
                        throw new InvalidDataException("Adjustment event dimensions cannot be negative.");
                    if (!double.IsFinite(clip.PixelAspectRatio) || clip.PixelAspectRatio <= 0 || clip.PixelAspectRatio > 4.0)
                        throw new InvalidDataException("Adjustment event pixel aspect ratio is invalid.");
                }
            }
        }
        if (TrackGroups != null)
        {
            var groupIds = new HashSet<Guid>();
            var groupedTrackIds = new HashSet<Guid>();
            foreach (var group in TrackGroups)
            {
                if (group == null || !groupIds.Add(group.Id))
                    throw new InvalidDataException("Track group IDs must be unique.");
                group.Validate();

                var indices = new List<int>();
                foreach (var trackId in group.TrackIds)
                {
                    if (!trackIds.Contains(trackId))
                        throw new InvalidDataException($"Track group '{group.Name}' references non-existent track ID {trackId}.");
                    if (!groupedTrackIds.Add(trackId))
                        throw new InvalidDataException($"Track {trackId} belongs to more than one track group.");

                    var idx = Tracks.FindIndex(t => t.Id == trackId);
                    indices.Add(idx);
                }

                indices.Sort();
                for (var i = 1; i < indices.Count; i++)
                {
                    if (indices[i] != indices[i - 1] + 1)
                        throw new InvalidDataException($"Track grouping requires contiguous tracks to be selected (group '{group.Name}').");
                }
            }
        }
    }
}

public sealed class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
    [JsonIgnore] public string Name => !string.IsNullOrEmpty(DisplayName) ? DisplayName : (IsGenerated && Generator != null ? $"{Generator.Title} ({Generator.PresetName})" : System.IO.Path.GetFileName(Path));
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Tags { get; set; } = [];
    public MediaAsset DeepClone()
    {
        var clone = (MediaAsset)MemberwiseClone();
        clone.VideoEffects = VideoEffects.Select(e => e.DeepClone()).ToList();
        clone.Tags = [..Tags];
        clone.Generator = Generator?.DeepClone();
        return clone;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<TrackAutomationMode>))]
public enum TrackAutomationMode
{
    AutomationOff = 0,
    AutomationRead = 1,
    AutomationTouch = 2,
    AutomationLatch = 3,
    AutomationWrite = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<AutoRippleMode>))]
public enum AutoRippleMode
{
    AffectedTracks = 0,
    AffectedTracksAndMarkers = 1,
    AllTracksAndMarkers = 2
}

public static class VegasTrackColors
{
    public static readonly (string Name, string Hex)[] Palette =
    [
        ("Default", ""),
        ("Track 1 - Violet", "#706295"),
        ("Track 2 - Rose", "#C28493"),
        ("Track 3 - Coral", "#FF857A"),
        ("Track 4 - Orange", "#FFAB7A"),
        ("Track 5 - Amber", "#FFD17A"),
        ("Track 6 - Mint", "#72C889"),
        ("Track 7 - Slate", "#8CB7CA"),
        ("Track 8 - Ocean", "#467D96"),
        ("Red", "#D32F2F"),
        ("Indigo", "#3F51B5"),
        ("Teal", "#00897B"),
        ("Olive", "#827717"),
        ("Brown", "#795548"),
        ("Lime", "#7CB342"),
        ("Steel Gray", "#607D8B")
    ];
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
    public AudioPanMode PanMode { get; set; } = AudioPanMode.Balance;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TrackCompositeMode CompositeMode { get; set; } = TrackCompositeMode.SourceAlpha;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CompositeNestingLevel { get; set; }
    [JsonIgnore]
    public bool IsCompositingChild => CompositeNestingLevel > 0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAdjustmentTrack { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? BusTrackId { get; set; }
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ArmRecord { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TrackColor { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TrackAutomationMode AutomationMode { get; set; } = TrackAutomationMode.AutomationRead;
    public TimelineTrack DeepClone()
    {
        var clone = new TimelineTrack
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
            PanMode = PanMode,
            CompositeMode = CompositeMode,
            CompositeNestingLevel = CompositeNestingLevel,
            IsAdjustmentTrack = IsAdjustmentTrack,
            BusTrackId = BusTrackId,
            Motion = Motion?.DeepClone(),
            InvertPhase = InvertPhase,
            ArmRecord = ArmRecord,
            TrackColor = TrackColor,
            AutomationMode = AutomationMode
        };

        clone.Clips = new List<TimelineClip>(Clips.Count);
        for (var i = 0; i < Clips.Count; i++) clone.Clips.Add(Clips[i].DeepClone());

        clone.VideoEffects = new List<VideoEffect>(VideoEffects.Count);
        for (var i = 0; i < VideoEffects.Count; i++) clone.VideoEffects.Add(VideoEffects[i].DeepClone());

        clone.AudioEffects = new List<AudioEffect>(AudioEffects.Count);
        for (var i = 0; i < AudioEffects.Count; i++) clone.AudioEffects.Add(AudioEffects[i].DeepClone());

        if (Envelopes != null)
        {
            clone.Envelopes = new List<ClipEnvelope>(Envelopes.Count);
            for (var i = 0; i < Envelopes.Count; i++) clone.Envelopes.Add(Envelopes[i].DeepClone());
        }

        return clone;
    }
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public AudioChannelMode ChannelMode { get; set; } = AudioChannelMode.Both;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double PitchSemis { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long SnapOffsetFrames { get; set; }

    public TimelineTake DeepClone() => new()
    {
        Id = Id,
        MediaId = MediaId,
        Name = Name,
        SourceInFrame = SourceInFrame,
        VideoSourceMode = VideoSourceMode,
        IsReversed = IsReversed,
        PlaybackRate = PlaybackRate,
        UnderSampleRate = UnderSampleRate,
        ResampleMode = ResampleMode,
        MaintainAspectRatio = MaintainAspectRatio,
        ReduceInterlace = ReduceInterlace,
        Normalize = Normalize,
        InvertPhase = InvertPhase,
        ChannelMode = ChannelMode,
        PitchSemis = PitchSemis,
        SnapOffsetFrames = SnapOffsetFrames
    };

    public void Validate()
    {
        if (Id == Guid.Empty || MediaId == Guid.Empty)
            throw new InvalidDataException("Take IDs must be non-empty.");
        if (SnapOffsetFrames < 0)
            throw new InvalidDataException("Take snap offset must be non-negative.");
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public AudioChannelMode ChannelMode { get; set; } = AudioChannelMode.Both;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double PitchSemis { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long SnapOffsetFrames { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsAdjustmentEvent { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int FrameWidth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int FrameHeight { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double PixelAspectRatio { get; set; } = 1.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool UseAlternateAdjustmentEventBehavior { get; set; }
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MaskAnimation? Mask { get; set; }
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
        clone.Mask = Mask?.DeepClone();
        clone.FadeInTransition = FadeInTransition?.DeepClone();
        clone.FadeOutTransition = FadeOutTransition?.DeepClone();

        clone.VideoEffects = new List<VideoEffect>(VideoEffects.Count);
        for (var i = 0; i < VideoEffects.Count; i++) clone.VideoEffects.Add(VideoEffects[i].DeepClone());

        clone.MotionTracks = new List<MotionTrackData>(MotionTracks.Count);
        for (var i = 0; i < MotionTracks.Count; i++) clone.MotionTracks.Add(MotionTracks[i].DeepClone());

        clone.Transcript = Transcript?.DeepClone();

        if (Envelopes != null)
        {
            clone.Envelopes = new List<ClipEnvelope>(Envelopes.Count);
            for (var i = 0; i < Envelopes.Count; i++) clone.Envelopes.Add(Envelopes[i].DeepClone());
        }

        if (Takes != null)
        {
            clone.Takes = new List<TimelineTake>(Takes.Count);
            for (var i = 0; i < Takes.Count; i++) clone.Takes.Add(Takes[i].DeepClone());
        }

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
