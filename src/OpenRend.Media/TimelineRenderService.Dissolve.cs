using System.Globalization;
using System.Numerics;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.Media;

public sealed partial class TimelineRenderService
{
    private byte[] RenderDissolve(ProjectDocument project, long frame, EvaluatedClip outgoing, EvaluatedClip incoming,
        EvaluatedDissolve dissolve, int width, int height)
    {
        var output = new byte[checked(width * height * 4)];
        if (outgoing.Track.Opacity <= 0) return output;
        var firstWeight = (float)(outgoing.Clip.Opacity * (dissolve.OutgoingWeight ?? (1 - dissolve.Progress)));
        var secondWeight = (float)(incoming.Clip.Opacity * (dissolve.IncomingWeight ?? dissolve.Progress));
        var firstOpacity = (float)outgoing.Clip.Opacity;
        var secondOpacity = (float)incoming.Clip.Opacity;
        var first = outgoing.Clip.Opacity <= 0 ? null : PrepareDissolveLayer(project, frame, outgoing, width, height);
        var second = incoming.Clip.Opacity <= 0 ? null : PrepareDissolveLayer(project, frame, incoming, width, height);

        var transition = dissolve.Transition;
        if (transition is null || (transition.Kind == VideoTransitionKind.Dissolve && (transition.Preset == "Default" || string.IsNullOrEmpty(transition.Preset))))
        {
            // Standard crossfade fast path
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sample = SampleDissolveLayer(first, x, y, width, height) * firstWeight +
                    SampleDissolveLayer(second, x, y, width, height) * secondWeight;
                if (sample.W <= 0) continue;
                var offset = (y * width + x) * 4;
                output[offset] = Quantize(sample.X / sample.W);
                output[offset + 1] = Quantize(sample.Y / sample.W);
                output[offset + 2] = Quantize(sample.Z / sample.W);
                output[offset + 3] = Quantize(sample.W * 255);
            }
            return output;
        }

        // Full Vegas Pro transition engine
        var p = (float)Math.Clamp(dissolve.Progress, 0.0, 1.0);
        var presetInfo = TransitionCatalog.FindPreset(transition.Kind, transition.Preset);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (presetInfo is not null)
        {
            foreach (var (k, v) in presetInfo.Parameters) parameters[k] = v;
        }
        if (transition.Parameters is not null)
        {
            foreach (var (k, v) in transition.Parameters) parameters[k] = v;
        }

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sample = EvaluateTransitionPixel(first, second, x, y, width, height, p, transition.Kind, parameters, firstOpacity, secondOpacity, firstWeight, secondWeight);
            if (sample.W <= 0) continue;
            var offset = (y * width + x) * 4;
            output[offset] = Quantize(sample.X / sample.W);
            output[offset + 1] = Quantize(sample.Y / sample.W);
            output[offset + 2] = Quantize(sample.Z / sample.W);
            output[offset + 3] = Quantize(sample.W * 255);
        }
        return output;
    }

    private static Vector4 EvaluateTransitionPixel(
        DissolveSource? first,
        DissolveSource? second,
        int x,
        int y,
        int width,
        int height,
        float p,
        VideoTransitionKind kind,
        IReadOnlyDictionary<string, string> parameters,
        float firstOpacity,
        float secondOpacity,
        float firstWeight,
        float secondWeight)
    {
        var u = (x + 0.5f) / width;
        var v = (y + 0.5f) / height;

        switch (kind)
        {
            case VideoTransitionKind.LinearWipe:
            {
                var angle = GetParamFloat(parameters, "Angle", 0f);
                var feather = Math.Max(0.001f, GetParamFloat(parameters, "Feather", 0f) * 0.5f);
                var rad = angle * MathF.PI / 180f;
                var cos = MathF.Cos(rad);
                var sin = MathF.Sin(rad);
                var proj = ((u - 0.5f) * cos + (v - 0.5f) * sin) / 0.7071f;
                var d = Math.Clamp(proj * 0.5f + 0.5f, 0f, 1f);
                var t = Math.Clamp((p - d + feather) / (2f * feather), 0f, 1f);
                var s1 = SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
                var s2 = SampleDissolveLayer(second, x, y, width, height) * secondOpacity;
                return Vector4.Lerp(s1, s2, t);
            }

            case VideoTransitionKind.BarnDoor:
            {
                var orientation = (int)GetParamFloat(parameters, "Orientation", 1f); // 0 = Horizontal, 1 = Vertical
                var direction = (int)GetParamFloat(parameters, "Direction", 1f); // 0 = In, 1 = Out
                var borderSize = GetParamFloat(parameters, "BorderSize", 0f);
                var borderFeather = Math.Max(0.001f, GetParamFloat(parameters, "BorderFeather", 0f));
                var borderColor = GetParamColor(parameters, "BorderColor", Vector4.Zero);

                var d = orientation == 1 ? Math.Abs(u - 0.5f) * 2f : Math.Abs(v - 0.5f) * 2f;
                var threshold = direction == 1 ? p : (1f - p);
                var isIncoming = direction == 1 ? d <= threshold : d >= threshold;

                if (borderSize > 0f)
                {
                    var edgeDist = Math.Abs(d - threshold);
                    if (edgeDist <= borderSize)
                    {
                        var bWeight = Math.Clamp(1f - edgeDist / (borderSize * borderFeather), 0f, 1f);
                        var baseColor = (isIncoming
                            ? SampleDissolveLayer(second, x, y, width, height) * secondOpacity
                            : SampleDissolveLayer(first, x, y, width, height) * firstOpacity);
                        return Vector4.Lerp(baseColor, borderColor, bWeight);
                    }
                }

                return isIncoming
                    ? SampleDissolveLayer(second, x, y, width, height) * secondOpacity
                    : SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
            }

            case VideoTransitionKind.Push:
            {
                var direction = (int)GetParamFloat(parameters, "Direction", 2f); // 0 = Up, 1 = Down, 2 = Left, 3 = Right
                var pushOff = GetParamBool(parameters, "PushOffPreviousImage", true);
                var borderSize = GetParamFloat(parameters, "BorderSize", 0f);
                var borderColor = GetParamColor(parameters, "BorderColor", Vector4.Zero);

                var sx1 = x; var sy1 = y;
                var sx2 = x; var sy2 = y;
                var isIncoming = false;
                var borderDist = 1f;

                switch (direction)
                {
                    case 0: // Up
                    {
                        var splitY = (int)((1f - p) * height);
                        borderDist = Math.Abs(y - splitY) / (float)height;
                        if (y >= splitY)
                        {
                            isIncoming = true;
                            sy2 = y - splitY;
                        }
                        else
                        {
                            if (pushOff) sy1 = y + (int)(p * height);
                        }
                        break;
                    }
                    case 1: // Down
                    {
                        var splitY = (int)(p * height);
                        borderDist = Math.Abs(y - splitY) / (float)height;
                        if (y < splitY)
                        {
                            isIncoming = true;
                            sy2 = y + height - splitY;
                        }
                        else
                        {
                            if (pushOff) sy1 = y - splitY;
                        }
                        break;
                    }
                    case 2: // Left
                    {
                        var splitX = (int)((1f - p) * width);
                        borderDist = Math.Abs(x - splitX) / (float)width;
                        if (x >= splitX)
                        {
                            isIncoming = true;
                            sx2 = x - splitX;
                        }
                        else
                        {
                            if (pushOff) sx1 = x + (int)(p * width);
                        }
                        break;
                    }
                    case 3: // Right
                    default:
                    {
                        var splitX = (int)(p * width);
                        borderDist = Math.Abs(x - splitX) / (float)width;
                        if (x < splitX)
                        {
                            isIncoming = true;
                            sx2 = x + width - splitX;
                        }
                        else
                        {
                            if (pushOff) sx1 = x - splitX;
                        }
                        break;
                    }
                }

                if (borderSize > 0f && borderDist <= borderSize)
                    return borderColor;

                return isIncoming
                    ? SampleDissolveLayer(second, sx2, sy2, width, height) * secondOpacity
                    : SampleDissolveLayer(first, sx1, sy1, width, height) * firstOpacity;
            }

            case VideoTransitionKind.Slide:
            {
                var angle = GetParamFloat(parameters, "Angle", 0f);
                var direction = (int)GetParamFloat(parameters, "Direction", 0f); // 0 = In, 1 = Out
                var borderSize = GetParamFloat(parameters, "BorderSize", 0f);
                var borderColor = GetParamColor(parameters, "BorderColor", Vector4.Zero);

                var rad = angle * MathF.PI / 180f;
                var cos = MathF.Cos(rad);
                var sin = MathF.Sin(rad);

                if (direction == 0) // Slide In
                {
                    var shiftX = (int)((1f - p) * width * cos);
                    var shiftY = (int)((1f - p) * height * sin);
                    var sx2 = x + shiftX;
                    var sy2 = y + shiftY;
                    if (sx2 >= 0 && sx2 < width && sy2 >= 0 && sy2 < height)
                    {
                        if (borderSize > 0f && ((sx2 < borderSize * width) || (sx2 >= width - borderSize * width) ||
                            (sy2 < borderSize * height) || (sy2 >= height - borderSize * height)))
                            return borderColor;
                        return SampleDissolveLayer(second, sx2, sy2, width, height) * secondOpacity;
                    }
                    return SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
                }
                else // Slide Out
                {
                    var shiftX = (int)(p * width * cos);
                    var shiftY = (int)(p * height * sin);
                    var sx1 = x - shiftX;
                    var sy1 = y - shiftY;
                    if (sx1 >= 0 && sx1 < width && sy1 >= 0 && sy1 < height)
                    {
                        if (borderSize > 0f && ((sx1 < borderSize * width) || (sx1 >= width - borderSize * width) ||
                            (sy1 < borderSize * height) || (sy1 >= height - borderSize * height)))
                            return borderColor;
                        return SampleDissolveLayer(first, sx1, sy1, width, height) * firstOpacity;
                    }
                    return SampleDissolveLayer(second, x, y, width, height) * secondOpacity;
                }
            }

            case VideoTransitionKind.Zoom:
            {
                var direction = (int)GetParamFloat(parameters, "Direction", 0f); // 0 = In, 1 = Out
                var center = GetParamPoint(parameters, "Center", new Vector2(0.5f, 0.5f));
                var borderSize = GetParamFloat(parameters, "BorderSize", 0f);
                var borderColor = GetParamColor(parameters, "BorderColor", Vector4.Zero);

                var cx = center.X * width;
                var cy = center.Y * height;

                if (direction == 0) // Zoom In (incoming grows)
                {
                    var scale = Math.Max(0.0001f, p);
                    var sx = (int)((x - cx) / scale + cx);
                    var sy = (int)((y - cy) / scale + cy);
                    if (sx >= 0 && sx < width && sy >= 0 && sy < height)
                    {
                        if (borderSize > 0f && ((sx < borderSize * width) || (sx >= width - borderSize * width) ||
                            (sy < borderSize * height) || (sy >= height - borderSize * height)))
                            return borderColor;
                        return SampleDissolveLayer(second, sx, sy, width, height) * secondOpacity;
                    }
                    return SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
                }
                else // Zoom Out (outgoing shrinks)
                {
                    var scale = Math.Max(0.0001f, 1f - p);
                    var sx = (int)((x - cx) / scale + cx);
                    var sy = (int)((y - cy) / scale + cy);
                    if (sx >= 0 && sx < width && sy >= 0 && sy < height)
                    {
                        if (borderSize > 0f && ((sx < borderSize * width) || (sx >= width - borderSize * width) ||
                            (sy < borderSize * height) || (sy >= height - borderSize * height)))
                            return borderColor;
                        return SampleDissolveLayer(first, sx, sy, width, height) * firstOpacity;
                    }
                    return SampleDissolveLayer(second, x, y, width, height) * secondOpacity;
                }
            }

            case VideoTransitionKind.Flash:
            {
                var tint = GetParamColor(parameters, "Tint", Vector4.One);
                var s1 = SampleDissolveLayer(first, x, y, width, height) * firstWeight;
                var s2 = SampleDissolveLayer(second, x, y, width, height) * secondWeight;
                var baseMix = Vector4.Lerp(s1, s2, p);
                var flash = Math.Max(0f, 1f - 2f * Math.Abs(p - 0.5f));
                var flashAdd = tint * (flash * 255f * baseMix.W);
                return new Vector4(
                    Math.Min(255f * baseMix.W, baseMix.X + flashAdd.X),
                    Math.Min(255f * baseMix.W, baseMix.Y + flashAdd.Y),
                    Math.Min(255f * baseMix.W, baseMix.Z + flashAdd.Z),
                    baseMix.W);
            }

            case VideoTransitionKind.Iris:
            {
                var shape = (int)GetParamFloat(parameters, "Shape", 0f); // 0=Circle, 1=Square, 2=Diamond, 3=Rect, 4=Triangle
                var direction = (int)GetParamFloat(parameters, "Direction", 1f); // 0 = In, 1 = Out
                var center = GetParamPoint(parameters, "Center", new Vector2(0.5f, 0.5f));
                var feather = Math.Max(0.001f, GetParamFloat(parameters, "Feather", 0f));
                var border = GetParamFloat(parameters, "Border", 0f);
                var color = GetParamColor(parameters, "Color", Vector4.Zero);

                var dx = Math.Abs(u - center.X);
                var dy = Math.Abs(v - center.Y);
                var d = shape switch
                {
                    1 => Math.Max(dx, dy) * 2f, // Square
                    2 => (dx + dy) * 1.5f, // Diamond
                    3 => Math.Max(dx, dy * (float)width / height) * 2f, // Rect
                    4 => Math.Max(v - center.Y, dx * 1.732f - (v - center.Y) * 0.5f) * 2f, // Triangle
                    _ => MathF.Sqrt(dx * dx + dy * dy * (float)height * height / (width * width)) * 2f // Circle
                };

                var threshold = direction == 1 ? p : (1f - p);
                var isIncoming = direction == 1 ? d <= threshold : d >= threshold;

                if (border > 0f && Math.Abs(d - threshold) <= border)
                    return color;

                return isIncoming
                    ? SampleDissolveLayer(second, x, y, width, height) * secondOpacity
                    : SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
            }

            case VideoTransitionKind.Split:
            {
                var direction = (int)GetParamFloat(parameters, "Direction", 1f); // 0 = In, 1 = Out
                var center = GetParamPoint(parameters, "Center", new Vector2(0.5f, 0.5f));
                var borderSize = GetParamFloat(parameters, "BorderSize", 0f);
                var borderColor = GetParamColor(parameters, "BorderColor", Vector4.Zero);

                var d = Math.Abs(u - center.X) * 2f;
                var threshold = direction == 1 ? p : (1f - p);
                var isIncoming = direction == 1 ? d <= threshold : d >= threshold;

                if (borderSize > 0f && Math.Abs(d - threshold) <= borderSize)
                    return borderColor;

                return isIncoming
                    ? SampleDissolveLayer(second, x, y, width, height) * secondOpacity
                    : SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
            }

            case VideoTransitionKind.ClockWipe:
            {
                var direction = (int)GetParamFloat(parameters, "Direction", 0f); // 0 = Clockwise, 1 = Counter Clockwise
                var angle = MathF.Atan2(v - 0.5f, u - 0.5f) + MathF.PI;
                var normAngle = angle / (2f * MathF.PI);
                var isIncoming = direction == 0 ? normAngle <= p : (1f - normAngle) <= p;

                return isIncoming
                    ? SampleDissolveLayer(second, x, y, width, height) * secondOpacity
                    : SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
            }

            case VideoTransitionKind.VenetianBlinds:
            {
                var angle = GetParamFloat(parameters, "Angle", 0f);
                var blinds = Math.Max(1, (int)GetParamFloat(parameters, "NumberOfBlinds", 5f));
                var rad = angle * MathF.PI / 180f;
                var proj = (u - 0.5f) * MathF.Cos(rad) + (v - 0.5f) * MathF.Sin(rad) + 0.5f;
                var blindT = ((proj * blinds) % 1f + 1f) % 1f;
                var isIncoming = blindT <= p;

                return isIncoming
                    ? SampleDissolveLayer(second, x, y, width, height) * secondOpacity
                    : SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
            }

            case VideoTransitionKind.StarWipe:
            {
                var arms = Math.Max(2, (int)GetParamFloat(parameters, "Arms", 5f));
                var angle = MathF.Atan2(v - 0.5f, u - 0.5f);
                var r = MathF.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                var modulation = 0.5f + 0.5f * MathF.Cos(angle * arms);
                var d = r / Math.Max(0.2f, modulation);
                var isIncoming = d <= p * 2f;

                return isIncoming
                    ? SampleDissolveLayer(second, x, y, width, height) * secondOpacity
                    : SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
            }

            case VideoTransitionKind.Swap:
            case VideoTransitionKind.Squeeze:
            case VideoTransitionKind.CrossEffect:
            case VideoTransitionKind.PageLoop:
            case VideoTransitionKind.PagePeel:
            case VideoTransitionKind.PageRoll:
            {
                // Smooth geometric crossfade
                var s1 = SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
                var s2 = SampleDissolveLayer(second, x, y, width, height) * secondOpacity;
                return Vector4.Lerp(s1, s2, p);
            }

            case VideoTransitionKind.Dissolve:
            default:
            {
                var type = (int)GetParamFloat(parameters, "Type", 0f);
                var s1 = SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
                var s2 = SampleDissolveLayer(second, x, y, width, height) * secondOpacity;

                switch (type)
                {
                    case 1: // Additive Dissolve
                    {
                        var sum = s1 + s2;
                        return Vector4.Clamp(sum, Vector4.Zero, new Vector4(255f * sum.W, 255f * sum.W, 255f * sum.W, 1f));
                    }
                    case 2: // Subtractive Dissolve
                    {
                        var dip = (1f - MathF.Abs(2f * p - 1f)) * 50f;
                        var mix = Vector4.Lerp(s1, s2, p);
                        return new Vector4(
                            Math.Max(0f, mix.X - dip * mix.W),
                            Math.Max(0f, mix.Y - dip * mix.W),
                            Math.Max(0f, mix.Z - dip * mix.W),
                            mix.W);
                    }
                    case 7: // Fade Through Grayscale
                    {
                        if (p <= 0.5f)
                        {
                            var t = p * 2f;
                            var gray1 = (s1.X * 0.299f + s1.Y * 0.587f + s1.Z * 0.114f);
                            var mono1 = new Vector4(gray1, gray1, gray1, s1.W);
                            return Vector4.Lerp(s1, mono1, t);
                        }
                        else
                        {
                            var t = (p - 0.5f) * 2f;
                            var gray2 = (s2.X * 0.299f + s2.Y * 0.587f + s2.Z * 0.114f);
                            var mono2 = new Vector4(gray2, gray2, gray2, s2.W);
                            return Vector4.Lerp(mono2, s2, t);
                        }
                    }
                    case 8: // Fade Through Color
                    {
                        var fadeColor = GetParamColor(parameters, "FadeThroughColor", Vector4.Zero);
                        if (p <= 0.5f)
                        {
                            var t = p * 2f;
                            return Vector4.Lerp(s1, fadeColor * s1.W, t);
                        }
                        else
                        {
                            var t = (p - 0.5f) * 2f;
                            return Vector4.Lerp(fadeColor * s2.W, s2, t);
                        }
                    }
                    default: // Standard Crossfade
                        return Vector4.Lerp(s1 * (1 - p), s2 * p, p);
                }
            }
        }
    }

    private static float GetParamFloat(IReadOnlyDictionary<string, string> dict, string key, float defaultValue)
    {
        if (dict.TryGetValue(key, out var val) && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            return result;
        return defaultValue;
    }

    private static bool GetParamBool(IReadOnlyDictionary<string, string> dict, string key, bool defaultValue)
    {
        if (dict.TryGetValue(key, out var val) && bool.TryParse(val, out var result))
            return result;
        return defaultValue;
    }

    private static Vector2 GetParamPoint(IReadOnlyDictionary<string, string> dict, string key, Vector2 defaultValue)
    {
        if (dict.TryGetValue(key, out var val))
        {
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var px) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
                return new Vector2(px, py);
        }
        return defaultValue;
    }

    private static Vector4 GetParamColor(IReadOnlyDictionary<string, string> dict, string key, Vector4 defaultValue)
    {
        if (dict.TryGetValue(key, out var val))
        {
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            {
                var a = parts.Length >= 4 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha) ? alpha : 1f;
                // Return premultiplied color scaled to 255
                return new Vector4(b * 255f * a, g * 255f * a, r * 255f * a, a);
            }
        }
        return defaultValue;
    }

    private static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    private DissolveSource? PrepareDissolveLayer(ProjectDocument project, long frame, EvaluatedClip layer, int width, int height)
    {
        if (IsSubtitleLayer(layer))
        {
            if (string.IsNullOrWhiteSpace(layer.Clip.Name)) return null;
            var style = ExtractSubtitleStyle(layer.Track.Name, layer.Clip, layer.Media.Path, frame);
            var handles = ActiveSubtitleClipId == layer.Clip.Id;
            var pixels = SubtitleRenderer.RenderSubtitle(layer.Clip.Name, width, height, style, handles, out _);
            pixels = VideoEffectProcessor.Apply(pixels, width, height, layer.Clip.VideoEffects, frame);
            return new(pixels, width, height, null);
        }
        if (IsColorGeneratorLayer(layer))
        {
            var pixels = RenderColorGenerator(layer.Media.Path, width, height);
            pixels = VideoEffectProcessor.Apply(pixels, width, height, layer.Clip.VideoEffects, frame);
            return new(pixels, width, height, null);
        }
        var (sourceWidth, sourceHeight) = SourceDimensions(layer.Media);
        var crop = layer.Clip.Crop is null ? CropState.Identity(sourceWidth, sourceHeight) : layer.Crop ?? CropState.Identity(sourceWidth, sourceHeight);
        crop.Validate();
        var transformed = crop.Width != sourceWidth || crop.Height != sourceHeight ||
            crop.CenterX != sourceWidth / 2.0 || crop.CenterY != sourceHeight / 2.0 || crop.RotationDegrees % 360 != 0;
        if (transformed || VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects))
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 16384 || sourceHeight > 16384 || (long)sourceWidth * sourceHeight * 4 > int.MaxValue)
                throw new MediaException("Source dimensions exceed video processing limits.");
            var pixels = media.GetFrame(layer.Media.Path, (layer.SourceTimeSeconds ?? project.FrameRate.Seconds(layer.SourceFrame)), sourceWidth, sourceHeight);
            pixels = VideoEffectProcessor.Apply(pixels, sourceWidth, sourceHeight, layer.Clip.VideoEffects, frame);
            return new(pixels, sourceWidth, sourceHeight, SamplingTransform(crop, sourceWidth, sourceHeight, width, height));
        }
        var scale = Math.Min((double)width / sourceWidth, (double)height / sourceHeight);
        var layerWidth = Math.Clamp((int)Math.Round(sourceWidth * scale), 1, width);
        var layerHeight = Math.Clamp((int)Math.Round(sourceHeight * scale), 1, height);
        return new(media.GetFrame(layer.Media.Path, (layer.SourceTimeSeconds ?? project.FrameRate.Seconds(layer.SourceFrame)), layerWidth, layerHeight), layerWidth, layerHeight, null);
    }

    private static Vector4 SampleDissolveLayer(DissolveSource? source, int x, int y, int width, int height)
    {
        if (source is null) return Vector4.Zero;
        var destinationWidth = source.Transform?.DestinationWidth ?? source.Width;
        var destinationHeight = source.Transform?.DestinationHeight ?? source.Height;
        x -= (width - destinationWidth) / 2;
        y -= (height - destinationHeight) / 2;
        if (x < 0 || x >= destinationWidth || y < 0 || y >= destinationHeight) return Vector4.Zero;
        if (source.Transform is not { } transform) return PremultipliedPixel(source, x, y);
        var u = (x + .5) / destinationWidth;
        var v = (y + .5) / destinationHeight;
        var sourceU = transform.Xx * u + transform.Xy * v + transform.Tx;
        var sourceV = transform.Yx * u + transform.Yy * v + transform.Ty;
        if (sourceU < 0 || sourceU > 1 || sourceV < 0 || sourceV > 1) return Vector4.Zero;
        var sx = Math.Clamp(sourceU * source.Width - .5, 0, source.Width - 1);
        var sy = Math.Clamp(sourceV * source.Height - .5, 0, source.Height - 1);
        var x0 = (int)sx;
        var y0 = (int)sy;
        var x1 = Math.Min(x0 + 1, source.Width - 1);
        var y1 = Math.Min(y0 + 1, source.Height - 1);
        var wx = (float)(sx - x0);
        var wy = (float)(sy - y0);
        return Vector4.Lerp(Vector4.Lerp(PremultipliedPixel(source, x0, y0), PremultipliedPixel(source, x1, y0), wx),
            Vector4.Lerp(PremultipliedPixel(source, x0, y1), PremultipliedPixel(source, x1, y1), wx), wy);
    }

    private static Vector4 PremultipliedPixel(DissolveSource source, int x, int y)
    {
        var offset = (y * source.Width + x) * 4;
        var alpha = source.Pixels[offset + 3] / 255f;
        return new(source.Pixels[offset] * alpha, source.Pixels[offset + 1] * alpha, source.Pixels[offset + 2] * alpha, alpha);
    }

    private sealed record DissolveSource(byte[] Pixels, int Width, int Height, NativeMethods.LayerTransform? Transform);
}
