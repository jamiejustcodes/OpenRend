using OpenRend.Core;

namespace OpenRend.Media;

internal static partial class VideoEffectProcessor
{
    internal static bool HasActiveEffects(IReadOnlyList<VideoEffect> effects) => effects.Any(effect => effect.IsEnabled && !IsIdentity(effect));

    private static bool IsIdentity(VideoEffect effect) => effect.Kind switch
    {
        VideoEffectKind.Lut3D => effect.Lut is null || effect.Amount == 0,
        VideoEffectKind.BrightnessContrast => effect.Amount == 0 && effect.SecondaryAmount == 0,
        VideoEffectKind.Saturation => effect.Amount == 1,
        VideoEffectKind.Invert or VideoEffectKind.BlackAndWhite or VideoEffectKind.Sepia or VideoEffectKind.BlackRestore or
        VideoEffectKind.GaussianBlur or VideoEffectKind.QuickBlur or VideoEffectKind.Median or VideoEffectKind.Sharpen or
        VideoEffectKind.UnsharpMask or VideoEffectKind.Pixelate or VideoEffectKind.Border or VideoEffectKind.Vignette or
        VideoEffectKind.SoftContrast or VideoEffectKind.Glow or VideoEffectKind.AddNoise or VideoEffectKind.FilmGrain or
        VideoEffectKind.Swirl or VideoEffectKind.Wave or VideoEffectKind.LensCorrection or VideoEffectKind.FillLight or
        VideoEffectKind.MinAndMax or VideoEffectKind.LinearBlur or VideoEffectKind.RadialBlur or VideoEffectKind.Spherize or
        VideoEffectKind.PinchPunch or VideoEffectKind.TvSimulator or VideoEffectKind.GradientMap or VideoEffectKind.Defocus or
        VideoEffectKind.LightRays or VideoEffectKind.Starburst => effect.Amount == 0,
        VideoEffectKind.ChromaBlur => effect.GetParameter("Horizontal") == 0 && effect.GetParameter("Vertical") == 0,
        VideoEffectKind.Crop => effect.GetParameter("XCrop") == 0 && effect.GetParameter("YCrop") == 0 && effect.GetParameter("Angle") == 0 && effect.GetParameter("Opacity") == 1,
        VideoEffectKind.PictureInPicture => effect.GetParameter("LocationX") == 0.5 && effect.GetParameter("LocationY") == 0.5 && effect.GetParameter("Scale") == 1 && effect.GetParameter("Angle") == 0 && effect.GetParameter("Opacity") == 1,
        VideoEffectKind.LabAdjust => effect.GetParameter("Lightness") == 1 && effect.GetParameter("ChannelA") == 0 && effect.GetParameter("ChannelB") == 0 && effect.GetParameter("InvertLuminance") == 0 && effect.GetParameter("Desaturate") == 0,
        VideoEffectKind.MaskGenerator => effect.GetParameter("Type") == 4 && effect.GetParameter("Invert") == 0 && effect.GetParameter("LowIn") == 0 && effect.GetParameter("HighIn") == 1 && effect.GetParameter("LowOut") == 0 && effect.GetParameter("HighOut") == 1,
        VideoEffectKind.TinyPlanet => effect.GetParameter("Latitude") == 0 && effect.GetParameter("Longitude") == 0 && effect.GetParameter("Z") == 20,
        VideoEffectKind.Newsprint => effect.Amount <= 1,
        VideoEffectKind.HslAdjust => effect.GetParameter("Hue") == 0 && effect.GetParameter("Saturation") == 1 && effect.GetParameter("Lightness") == 0,
        VideoEffectKind.ColorBalance => effect.GetParameter("Red") == 0 && effect.GetParameter("Green") == 0 && effect.GetParameter("Blue") == 0,
        VideoEffectKind.WhiteBalance => effect.GetParameter("Temperature") == 0 && effect.GetParameter("Tint") == 0,
        VideoEffectKind.Levels => effect.GetParameter("InputBlack") == 0 && effect.GetParameter("InputWhite") == 1 && effect.GetParameter("Gamma") == 1 && effect.GetParameter("OutputBlack") == 0 && effect.GetParameter("OutputWhite") == 1,
        VideoEffectKind.ChannelBlend => effect.GetParameter("Red") == 1 && effect.GetParameter("Green") == 1 && effect.GetParameter("Blue") == 1 && effect.GetParameter("Crossfeed") == 0,
        VideoEffectKind.Mirror or VideoEffectKind.OffsetAndWrap => effect.GetParameter("Horizontal") == 0 && effect.GetParameter("Vertical") == 0,
        VideoEffectKind.AiAutoReframe => effect.GetParameter("Preview") == 0 && effect.GetParameter("CenterX") == 0.5 && effect.GetParameter("CenterY") == 0.5,
        VideoEffectKind.AiColorization => effect.GetParameter("ColorShiftA") == 0 && effect.GetParameter("ColorShiftB") == 0 && effect.GetParameter("ColorMagnificationA") == 1 && effect.GetParameter("ColorMagnificationB") == 1 && effect.GetParameter("InvertColorA") == 0 && effect.GetParameter("InvertColorB") == 0,
        VideoEffectKind.AiDehaze or VideoEffectKind.AiSmooth or VideoEffectKind.AiSharpen => effect.Amount == 0 && effect.GetParameter("Gain") == 1 && effect.GetParameter("Offset") == 0,
        VideoEffectKind.AiStyleTransfer => effect.Amount == 0,
        VideoEffectKind.AiUpscale => effect.GetParameter("Scaling") == 1 && effect.GetParameter("ImproveSharpness") == 0,
        VideoEffectKind.AiZDepth => effect.GetParameter("Visualization") == 0 && effect.GetParameter("DepthMin") == 0 && effect.GetParameter("DepthMax") == 1 && effect.GetParameter("Invert") == 0,
        VideoEffectKind.AiSmartMask => effect.GetParameter("Visualization") == 0 && effect.GetParameter("Confidence") == 0 && effect.GetParameter("Invert") == 0,
        VideoEffectKind.ColorGrading =>
            effect.GetParameter("Lift_R") == 0 && effect.GetParameter("Lift_G") == 0 && effect.GetParameter("Lift_B") == 0 && effect.GetParameter("Lift_Y") == 0 &&
            effect.GetParameter("Gamma_R") == 1 && effect.GetParameter("Gamma_G") == 1 && effect.GetParameter("Gamma_B") == 1 && effect.GetParameter("Gamma_Y") == 1 &&
            effect.GetParameter("Gain_R") == 1 && effect.GetParameter("Gain_G") == 1 && effect.GetParameter("Gain_B") == 1 && effect.GetParameter("Gain_Y") == 1 &&
            effect.GetParameter("Offset_R") == 0 && effect.GetParameter("Offset_G") == 0 && effect.GetParameter("Offset_B") == 0 && effect.GetParameter("Offset_Y") == 0 &&
            effect.GetParameter("ExposureLnAdjust") == 0 && effect.GetParameter("BrightnessAdjust") == 0 && effect.GetParameter("ContrastAdjust") == 0 && effect.GetParameter("SaturationAdjust") == 1 &&
            effect.GetParameter("TemperatureAdjust") == 0 && effect.GetParameter("TintAdjust") == 0 &&
            effect.GetParameter("InMinAdjust") == 0 && effect.GetParameter("InMaxAdjust") == 1 && effect.GetParameter("OutMinAdjust") == 0 && effect.GetParameter("OutMaxAdjust") == 1 &&
            effect.GetParameter("HueAdjust") == 0 && effect.GetParameter("SatAdjust") == 1 && effect.GetParameter("ValAdjust") == 1 &&
            (effect.Lut is null || effect.GetParameter("LUTGain_0") == 0 || effect.GetParameter("LUTEnable_0") == 1) &&
            (effect.LookLut is null || effect.GetParameter("LUTGain_1") == 0 || effect.GetParameter("LUTEnable_1") == 1),
        _ => false
    };

    internal static byte[] Apply(byte[] source, IReadOnlyList<VideoEffect> effects) => Apply(source, source.Length / 4, 1, effects);

    internal static byte[] Apply(byte[] source, int width, int height, IReadOnlyList<VideoEffect> effects, long projectFrame = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(effects);
        if (width <= 0 || height <= 0 || (long)width * height * 4 != source.Length) throw new ArgumentException("Effect image dimensions do not match its BGRA buffer.");
        foreach (var effect in effects) effect.Validate();
        if (!HasActiveEffects(effects)) return source;
        // Decoder frames remain immutable. Each chain stage owns its output before the next stage reads it.
        var output = (byte[])source.Clone();
        foreach (var effect in effects)
        {
            if (!effect.IsEnabled || IsIdentity(effect)) continue;
            double P(string key) => effect.GetParameter(key);
            var amount = effect.Amount;
            switch (effect.Kind)
            {
                case VideoEffectKind.ColorGrading: output = ApplyColorGrading(output, effect); break;
                case VideoEffectKind.Lut3D: output = ApplyLut(output, effect.Lut!, amount, P("Interpolation") == 1); break;
                case VideoEffectKind.GaussianBlur:
                case VideoEffectKind.QuickBlur:
                    output = Blur(output, width, height, Radius(amount, width, height), effect.Kind == VideoEffectKind.GaussianBlur); break;
                case VideoEffectKind.Sharpen:
                case VideoEffectKind.UnsharpMask:
                case VideoEffectKind.Glow:
                    var glow = effect.Kind == VideoEffectKind.Glow;
                    var blurredInput = output;
                    if (glow)
                    {
                        var threshold = P("Threshold");
                        blurredInput = Color(output, (r, g, b) => (Math.Max(0, r - threshold), Math.Max(0, g - threshold), Math.Max(0, b - threshold)));
                    }
                    var radius = effect.Kind == VideoEffectKind.Sharpen ? 1 : Radius(P("Radius"), width, height);
                    var blurred = Blur(blurredInput, width, height, radius, true);
                    var sharpenThreshold = effect.Kind == VideoEffectKind.UnsharpMask ? P("Threshold") * 255 : 0;
                    for (var i = 0; i < output.Length; i += 4)
                        for (var c = 0; c < 3; c++)
                        {
                            var detail = output[i + c] - blurred[i + c];
                            output[i + c] = Byte(glow ? output[i + c] + blurred[i + c] * amount : output[i + c] + (Math.Abs(detail) >= sharpenThreshold ? detail * amount : 0));
                        }
                    break;
                case VideoEffectKind.Median: output = Neighborhood(output, width, height, (int)Math.Round(amount), 0); break;
                case VideoEffectKind.MinAndMax: output = Neighborhood(output, width, height, 1, amount); break;
                case VideoEffectKind.Pixelate: output = Pixelate(output, width, height, Math.Max(1, Radius(amount, width, height))); break;
                case VideoEffectKind.ConvolutionKernel: output = Convolution(output, width, height, effect); break;
                case VideoEffectKind.Mirror:
                    var horizontal = P("Horizontal"); var vertical = P("Vertical");
                    output = Mirror(output, width, height, horizontal, vertical); break;
                case VideoEffectKind.OffsetAndWrap:
                    var offsetX = P("Horizontal") * width; var offsetY = P("Vertical") * height;
                    output = Resample(output, width, height, (x, y) => (Wrap(x - offsetX, width), Wrap(y - offsetY, height)), true); break;
                case VideoEffectKind.Swirl:
                    var swirlRadius = P("Radius");
                    output = Polar(output, width, height, (r, a) => (r, a + amount * Math.PI / 180 * Math.Max(0, 1 - r / swirlRadius))); break;
                case VideoEffectKind.RadialPixelate:
                    var sectors = P("Sectors");
                    output = Polar(output, width, height, (r, a) => ((Math.Floor(r * amount) + .5) / amount, (Math.Floor(a / (2 * Math.PI) * sectors) + .5) / sectors * 2 * Math.PI)); break;
                case VideoEffectKind.LensCorrection:
                    output = Polar(output, width, height, (r, a) => (r * (1 + amount * r * r), a)); break;
                case VideoEffectKind.Wave:
                    var frequency = P("Frequency"); var phase = P("Phase") * Math.PI / 180;
                    output = Resample(output, width, height, (x, y) => (x + amount * width * Math.Sin(2 * Math.PI * (y + .5) / height * frequency + phase), y)); break;
                case VideoEffectKind.Border:
                    var border = amount * Math.Min(width, height); var br = Channel(P("Red")); var bg = Channel(P("Green")); var bb = Channel(P("Blue"));
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) if (x < border || y < border || width - 1 - x < border || height - 1 - y < border)
                    { var i = (y * width + x) * 4; output[i] = bb; output[i + 1] = bg; output[i + 2] = br; }
                    break;
                case VideoEffectKind.Vignette:
                    var clear = P("Radius"); var softness = P("Softness");
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var r = Math.Sqrt(Math.Pow((x + .5) / width * 2 - 1, 2) + Math.Pow((y + .5) / height * 2 - 1, 2)) / Math.Sqrt(2);
                        var t = Math.Clamp((r - clear) / softness, 0, 1); var gain = 1 - amount * t * t * (3 - 2 * t); var i = (y * width + x) * 4;
                        for (var c = 0; c < 3; c++) output[i + c] = Byte(output[i + c] * gain);
                    }
                    break;
                case VideoEffectKind.CookieCutter:
                    var maskWidth = P("Width"); var maskHeight = P("Height"); var feather = P("Feather"); var invert = P("Invert");
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var r = Math.Sqrt(Math.Pow(((x + .5) / width * 2 - 1) / maskWidth, 2) + Math.Pow(((y + .5) / height * 2 - 1) / maskHeight, 2));
                        var alpha = Math.Clamp((1 - r) / feather, 0, 1); alpha = alpha * (1 - invert) + (1 - alpha) * invert;
                        var i = (y * width + x) * 4; output[i + 3] = Byte(output[i + 3] * alpha);
                    }
                    break;
                case VideoEffectKind.ChromaKeyer:
                    var kr = P("Red"); var kg = P("Green"); var kb = P("Blue"); var tolerance = P("Tolerance"); var keySoftness = P("Softness");
                    for (var i = 0; i < output.Length; i += 4)
                    {
                        var distance = Math.Sqrt(Math.Pow(output[i + 2] / 255.0 - kr, 2) + Math.Pow(output[i + 1] / 255.0 - kg, 2) + Math.Pow(output[i] / 255.0 - kb, 2)) / Math.Sqrt(3);
                        output[i + 3] = Byte(output[i + 3] * Math.Clamp((distance - tolerance) / keySoftness, 0, 1));
                    }
                    break;
                case VideoEffectKind.AddNoise:
                case VideoEffectKind.FilmGrain:
                    var grain = effect.Kind == VideoEffectKind.FilmGrain; var size = grain ? Math.Max(1, (int)Math.Round(P("Size"))) : 1;
                    var monochrome = grain ? 1 : P("Monochrome"); var seed = (uint)P("Seed");
                    // A frame-addressed hash makes seek, preview, export and retries reproduce the same grain.
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var i = (y * width + x) * 4; var key = unchecked(seed + (uint)(x / size) * 73856093u + (uint)(y / size) * 19349663u + (uint)projectFrame * 83492791u + (uint)(projectFrame >> 32));
                        var mono = Noise(key); var luma = (.2126 * output[i + 2] + .7152 * output[i + 1] + .0722 * output[i]) / 255;
                        for (var c = 0; c < 3; c++)
                        { var noise = mono * monochrome + Noise(unchecked(key + (uint)(c + 1) * 2654435761u)) * (1 - monochrome); output[i + c] = Byte(output[i + c] + noise * amount * 255 * (grain ? (.25 + .75 * Math.Sqrt(luma * (1 - luma) * 4)) : 1)); }
                    }
                    break;
                case VideoEffectKind.LinearBlur:
                    var angleRad = P("Angle") * Math.PI / 180.0;
                    var radiusPixels = amount * Math.Max(width, height) * 0.1;
                    if (radiusPixels >= 0.5)
                    {
                        var stepX = Math.Cos(angleRad); var stepY = Math.Sin(angleRad);
                        var steps = Math.Max(1, (int)Math.Ceiling(radiusPixels));
                        var linearOutput = (byte[])output.Clone();
                        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                        {
                            double rSum = 0, gSum = 0, bSum = 0, aSum = 0; int count = 0;
                            for (var s = -steps; s <= steps; s++)
                            {
                                var sampleX = (int)Math.Clamp(Math.Round(x + s * (radiusPixels / steps) * stepX), 0, width - 1);
                                var sampleY = (int)Math.Clamp(Math.Round(y + s * (radiusPixels / steps) * stepY), 0, height - 1);
                                var idx = (sampleY * width + sampleX) * 4;
                                bSum += output[idx]; gSum += output[idx + 1]; rSum += output[idx + 2]; aSum += output[idx + 3]; count++;
                            }
                            var outIdx = (y * width + x) * 4;
                            linearOutput[outIdx] = (byte)(bSum / count); linearOutput[outIdx + 1] = (byte)(gSum / count);
                            linearOutput[outIdx + 2] = (byte)(rSum / count); linearOutput[outIdx + 3] = (byte)(aSum / count);
                        }
                        output = linearOutput;
                    }
                    break;
                case VideoEffectKind.RadialBlur:
                    var radCx = P("CenterX") * width; var radCy = P("CenterY") * height;
                    var rType = (int)P("Type"); var radialSteps = 10;
                    var radialOutput = (byte[])output.Clone();
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var rdx = x - radCx; var rdy = y - radCy; var dist = Math.Sqrt(rdx * rdx + rdy * rdy);
                        double rSum = 0, gSum = 0, bSum = 0, aSum = 0; int count = 0;
                        for (var s = 0; s < radialSteps; s++)
                        {
                            var factor = rType switch { 1 => dist > 0 ? (dist - (s / (double)radialSteps) * amount * 30.0) / dist : 1.0, 2 => 1.0 - (s / (double)radialSteps) * amount * 0.15, _ => 1.0 - (s / (double)radialSteps) * amount * 0.25 };
                            var sampleX = (int)Math.Clamp(Math.Round(radCx + rdx * factor), 0, width - 1);
                            var sampleY = (int)Math.Clamp(Math.Round(radCy + rdy * factor), 0, height - 1);
                            var idx = (sampleY * width + sampleX) * 4;
                            bSum += output[idx]; gSum += output[idx + 1]; rSum += output[idx + 2]; aSum += output[idx + 3]; count++;
                        }
                        var outIdx = (y * width + x) * 4;
                        radialOutput[outIdx] = (byte)(bSum / count); radialOutput[outIdx + 1] = (byte)(gSum / count);
                        radialOutput[outIdx + 2] = (byte)(rSum / count); radialOutput[outIdx + 3] = (byte)(aSum / count);
                    }
                    output = radialOutput;
                    break;
                case VideoEffectKind.ChromaBlur:
                    var hPix = (int)Math.Round(P("Horizontal")); var vPix = (int)Math.Round(P("Vertical"));
                    if (hPix > 0 || vPix > 0)
                    {
                        var uCh = new double[width * height]; var vCh = new double[width * height]; var yCh = new double[width * height];
                        for (var i = 0; i < width * height; i++)
                        {
                            var b = output[i * 4]; var g = output[i * 4 + 1]; var r = output[i * 4 + 2];
                            yCh[i] = 0.299 * r + 0.587 * g + 0.114 * b;
                            uCh[i] = -0.14713 * r - 0.28886 * g + 0.436 * b;
                            vCh[i] = 0.615 * r - 0.51499 * g - 0.10001 * b;
                        }
                        var uBlurred = BoxBlur1D(BoxBlur1D(uCh, width, height, hPix, true), width, height, vPix, false);
                        var vBlurred = BoxBlur1D(BoxBlur1D(vCh, width, height, hPix, true), width, height, vPix, false);
                        for (var i = 0; i < width * height; i++)
                        {
                            var yVal = yCh[i]; var uVal = uBlurred[i]; var vVal = vBlurred[i];
                            output[i * 4] = Byte(yVal + 2.03211 * uVal);
                            output[i * 4 + 1] = Byte(yVal - 0.39465 * uVal - 0.58060 * vVal);
                            output[i * 4 + 2] = Byte(yVal + 1.13983 * vVal);
                        }
                    }
                    break;
                case VideoEffectKind.Spherize:
                    var sCenterX = P("CenterX"); var sCenterY = P("CenterY");
                    var sScaleH = P("Horizontal"); var sScaleV = P("Vertical");
                    output = Resample(output, width, height, (x, y) =>
                    {
                        var nx = ((x + .5) / width - sCenterX) / sScaleH * 2;
                        var ny = ((y + .5) / height - sCenterY) / sScaleV * 2;
                        var r = Math.Sqrt(nx * nx + ny * ny);
                        if (r >= 1 || r == 0) return (x, y);
                        var theta = Math.Atan2(ny, nx);
                        var rMapped = Math.Pow(r, 1 - amount * 0.75);
                        var mappedX = (sCenterX + (rMapped * Math.Cos(theta) * sScaleH) / 2) * width - .5;
                        var mappedY = (sCenterY + (rMapped * Math.Sin(theta) * sScaleV) / 2) * height - .5;
                        return (mappedX, mappedY);
                    }, true);
                    break;
                case VideoEffectKind.PinchPunch:
                    var ppCenterX = P("CenterX"); var ppCenterY = P("CenterY");
                    var ppScaleH = P("Horizontal"); var ppScaleV = P("Vertical");
                    output = Resample(output, width, height, (x, y) =>
                    {
                        var nx = ((x + .5) / width - ppCenterX) / ppScaleH * 2;
                        var ny = ((y + .5) / height - ppCenterY) / ppScaleV * 2;
                        var r = Math.Sqrt(nx * nx + ny * ny);
                        if (r >= 1 || r == 0) return (x, y);
                        var theta = Math.Atan2(ny, nx);
                        var factor = Math.Sin(Math.PI * 0.5 * r);
                        var rMapped = amount >= 0 ? r * (1 - amount * (1 - factor)) : r * (1 - amount * (1 - r));
                        var mappedX = (ppCenterX + (rMapped * Math.Cos(theta) * ppScaleH) / 2) * width - .5;
                        var mappedY = (ppCenterY + (rMapped * Math.Sin(theta) * ppScaleV) / 2) * height - .5;
                        return (mappedX, mappedY);
                    }, true);
                    break;
                case VideoEffectKind.TvSimulator:
                    var lineSync = P("LineSync"); var phos = P("Phosphorescence"); var stat = P("Static"); var interlacing = amount;
                    for (var y = 0; y < height; y++)
                    {
                        var scanlineDarkness = (y % 2 == 0) ? 1.0 : (1.0 - interlacing * 0.4);
                        var syncOffset = lineSync > 0 ? (int)(Math.Sin(y * 0.3 + projectFrame * 0.5) * lineSync * 15) : 0;
                        for (var x = 0; x < width; x++)
                        {
                            var sampleX = Math.Clamp(x + syncOffset, 0, width - 1);
                            var srcIdx = (y * width + sampleX) * 4; var outIdx = (y * width + x) * 4;
                            var col = x % 3; var grillR = col == 0 ? 1.15 : 0.92; var grillG = col == 1 ? 1.15 : 0.92; var grillB = col == 2 ? 1.15 : 0.92;
                            var noiseVal = stat > 0 ? (Noise(unchecked((uint)(y * 73856093 + x * 19349663 + (int)projectFrame * 83492791))) - 0.5) * stat * 255 : 0;
                            output[outIdx] = Byte((output[srcIdx] * grillB + phos * 20) * scanlineDarkness + noiseVal);
                            output[outIdx + 1] = Byte((output[srcIdx + 1] * grillG + phos * 20) * scanlineDarkness + noiseVal);
                            output[outIdx + 2] = Byte((output[srcIdx + 2] * grillR + phos * 20) * scanlineDarkness + noiseVal);
                        }
                    }
                    break;
                case VideoEffectKind.Crop:
                    var xCrop = P("XCrop") * width; var yCrop = P("YCrop") * height;
                    var cropAngle = P("Angle") * Math.PI / 180.0; var cropOpacity = P("Opacity");
                    var cropCos = Math.Cos(-cropAngle); var cropSin = Math.Sin(-cropAngle);
                    var cropCx = width * 0.5; var cropCy = height * 0.5;
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var cropDx = x - cropCx; var cropDy = y - cropCy;
                        var rotX = cropCx + cropDx * cropCos - cropDy * cropSin;
                        var rotY = cropCy + cropDx * cropSin + cropDy * cropCos;
                        var i = (y * width + x) * 4;
                        if (rotX < xCrop || rotX > width - xCrop || rotY < yCrop || rotY > height - yCrop)
                            output[i + 3] = (byte)(output[i + 3] * (1 - cropOpacity));
                    }
                    break;
                case VideoEffectKind.Newsprint:
                    var dotSize = Math.Max(2, (int)Math.Round(amount)); var npType = (int)P("Type");
                    for (var by = 0; by < height; by += dotSize) for (var bx = 0; bx < width; bx += dotSize)
                    {
                        var cellW = Math.Min(dotSize, width - bx); var cellH = Math.Min(dotSize, height - by);
                        double avgB = 0, avgG = 0, avgR = 0; int count = 0;
                        for (var cellY = 0; cellY < cellH; cellY++) for (var cellX = 0; cellX < cellW; cellX++)
                        {
                            var idx = ((by + cellY) * width + (bx + cellX)) * 4;
                            avgB += output[idx]; avgG += output[idx + 1]; avgR += output[idx + 2]; count++;
                        }
                        avgB /= count; avgG /= count; avgR /= count;
                        var avgLuma = (.299 * avgR + .587 * avgG + .114 * avgB) / 255.0;
                        var dotRadius = (1 - avgLuma) * (dotSize * 0.6);
                        var centerX = bx + cellW * 0.5; var centerY = by + cellH * 0.5;
                        for (var cellY = 0; cellY < cellH; cellY++) for (var cellX = 0; cellX < cellW; cellX++)
                        {
                            var px = bx + cellX; var py = by + cellY;
                            var dist = Math.Sqrt((px - centerX) * (px - centerX) + (py - centerY) * (py - centerY));
                            var isDot = dist <= dotRadius; var idx = (py * width + px) * 4;
                            if (npType == 1)
                            { var val = isDot ? (byte)0 : (byte)255; output[idx] = val; output[idx + 1] = val; output[idx + 2] = val; }
                            else
                            { var factor = isDot ? 0.3 : 1.1; output[idx] = Byte(avgB * factor); output[idx + 1] = Byte(avgG * factor); output[idx + 2] = Byte(avgR * factor); }
                        }
                    }
                    break;
                case VideoEffectKind.Defocus:
                {
                    var defRadius = (int)Math.Round(amount);
                    var bloomThresh = P("BloomThreshold") / 100.0;
                    var bloomStr = P("BloomStrength");
                    if (defRadius > 0)
                    {
                        var defOutput = (byte[])output.Clone();
                        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                        {
                            double bSum = 0, gSum = 0, rSum = 0, aSum = 0, weightSum = 0;
                            for (var defDy = -defRadius; defDy <= defRadius; defDy++)
                            {
                                var sy = Math.Clamp(y + defDy, 0, height - 1);
                                for (var defDx = -defRadius; defDx <= defRadius; defDx++)
                                {
                                    if (defDx * defDx + defDy * defDy > defRadius * defRadius) continue;
                                    var sx = Math.Clamp(x + defDx, 0, width - 1);
                                    var sIdx = (sy * width + sx) * 4;
                                    var bVal = output[sIdx]; var gVal = output[sIdx + 1]; var rVal = output[sIdx + 2];
                                    var luma = (0.2126 * rVal + 0.7152 * gVal + 0.0722 * bVal) / 255.0;
                                    var weight = 1.0 + (luma > bloomThresh ? (luma - bloomThresh) * bloomStr : 0);
                                    bSum += bVal * weight; gSum += gVal * weight; rSum += rVal * weight;
                                    aSum += output[sIdx + 3] * weight; weightSum += weight;
                                }
                            }
                            var outIdx = (y * width + x) * 4;
                            defOutput[outIdx] = Byte(bSum / weightSum); defOutput[outIdx + 1] = Byte(gSum / weightSum);
                            defOutput[outIdx + 2] = Byte(rSum / weightSum); defOutput[outIdx + 3] = Byte(aSum / weightSum);
                        }
                        output = defOutput;
                    }
                    break;
                }
                case VideoEffectKind.MaskGenerator:
                {
                    var mType = (int)P("Type"); var mInvert = (int)P("Invert");
                    var lowIn = P("LowIn"); var highIn = Math.Max(lowIn + 0.0001, P("HighIn"));
                    var lowOut = P("LowOut"); var highOut = P("HighOut");
                    for (var i = 0; i < output.Length; i += 4)
                    {
                        var b = output[i] / 255.0; var g = output[i + 1] / 255.0; var r = output[i + 2] / 255.0; var a = output[i + 3] / 255.0;
                        var val = mType switch
                        {
                            0 => 0.2126 * r + 0.7152 * g + 0.0722 * b,
                            1 => r,
                            2 => g,
                            3 => b,
                            _ => a
                        };
                        if (mInvert == 1) val = 1.0 - val;
                        var normalized = Math.Clamp((val - lowIn) / (highIn - lowIn), 0, 1);
                        var maskAlpha = Math.Clamp(lowOut + normalized * (highOut - lowOut), 0, 1);
                        output[i + 3] = Byte(output[i + 3] * maskAlpha);
                    }
                    break;
                }
                case VideoEffectKind.PictureInPicture:
                {
                    var locX = P("LocationX"); var locY = P("LocationY");
                    var pipScale = Math.Max(0.01, P("Scale"));
                    var pipAngle = P("Angle") * Math.PI / 180.0; var pipOpacity = P("Opacity");
                    var pipCos = Math.Cos(-pipAngle); var pipSin = Math.Sin(-pipAngle);
                    var pipCenterX = locX * width; var pipCenterY = locY * height;
                    var pipOutput = new byte[output.Length];
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var pdx = x - pipCenterX; var pdy = y - pipCenterY;
                        var rotX = pdx * pipCos - pdy * pipSin; var rotY = pdx * pipSin + pdy * pipCos;
                        var srcX = (rotX / pipScale) + width * 0.5; var srcY = (rotY / pipScale) + height * 0.5;
                        var outIdx = (y * width + x) * 4;
                        if (srcX >= 0 && srcX < width && srcY >= 0 && srcY < height)
                        {
                            var sx = Math.Clamp((int)Math.Round(srcX), 0, width - 1);
                            var sy = Math.Clamp((int)Math.Round(srcY), 0, height - 1);
                            var sIdx = (sy * width + sx) * 4;
                            pipOutput[outIdx] = output[sIdx]; pipOutput[outIdx + 1] = output[sIdx + 1];
                            pipOutput[outIdx + 2] = output[sIdx + 2]; pipOutput[outIdx + 3] = Byte(output[sIdx + 3] * pipOpacity);
                        }
                    }
                    output = pipOutput;
                    break;
                }
                case VideoEffectKind.LightRays:
                {
                    var lightX = P("LightSourceX") * width; var lightY = P("LightSourceY") * height;
                    var sens = P("Sensitivity"); var str = amount; var lrBlend = P("Blend");
                    if (str > 0)
                    {
                        var lrOutput = (byte[])output.Clone(); var raySteps = 16;
                        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                        {
                            var rdx = lightX - x; var rdy = lightY - y;
                            double rayB = 0, rayG = 0, rayR = 0;
                            for (var s = 0; s < raySteps; s++)
                            {
                                var t = s / (double)raySteps;
                                var sx = (int)Math.Clamp(Math.Round(x + rdx * t * str), 0, width - 1);
                                var sy = (int)Math.Clamp(Math.Round(y + rdy * t * str), 0, height - 1);
                                var sIdx = (sy * width + sx) * 4;
                                var b = output[sIdx] / 255.0; var g = output[sIdx + 1] / 255.0; var r = output[sIdx + 2] / 255.0;
                                var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                                if (luma > (1 - sens))
                                {
                                    var factor = (luma - (1 - sens)) / Math.Max(0.01, sens) * (1 - t);
                                    rayB += b * factor; rayG += g * factor; rayR += r * factor;
                                }
                            }
                            var outIdx = (y * width + x) * 4; var boost = str * 1.5;
                            lrOutput[outIdx] = Byte(output[outIdx] * (1 - lrBlend) + (output[outIdx] + rayB / raySteps * boost * 255) * lrBlend + rayB / raySteps * boost * 255 * (1 - lrBlend));
                            lrOutput[outIdx + 1] = Byte(output[outIdx + 1] * (1 - lrBlend) + (output[outIdx + 1] + rayG / raySteps * boost * 255) * lrBlend + rayG / raySteps * boost * 255 * (1 - lrBlend));
                            lrOutput[outIdx + 2] = Byte(output[outIdx + 2] * (1 - lrBlend) + (output[outIdx + 2] + rayR / raySteps * boost * 255) * lrBlend + rayR / raySteps * boost * 255 * (1 - lrBlend));
                        }
                        output = lrOutput;
                    }
                    break;
                }
                case VideoEffectKind.Starburst:
                {
                    var sbThresh = P("Threshold") / 100.0; var sbBrilliance = P("Brilliance") / 1000.0;
                    var sbStrength = amount / 100.0; var sbRadius = (int)Math.Round(P("Radius"));
                    var sbPoints = (int)Math.Round(P("Points"));
                    if (sbStrength > 0 && sbRadius > 0)
                    {
                        var sbOutput = (byte[])output.Clone();
                        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                        {
                            var idx = (y * width + x) * 4;
                            var luma = (0.2126 * output[idx + 2] + 0.7152 * output[idx + 1] + 0.0722 * output[idx]) / 255.0;
                            if (luma > sbThresh)
                            {
                                var starIntensity = (luma - sbThresh) * sbBrilliance * sbStrength;
                                for (var pt = 0; pt < sbPoints; pt++)
                                {
                                    var ang = pt * Math.PI / sbPoints;
                                    var dirX = Math.Cos(ang); var dirY = Math.Sin(ang);
                                    for (var r = 1; r <= sbRadius; r++)
                                    {
                                        var falloff = 1.0 - (double)r / sbRadius;
                                        for (var sign = -1; sign <= 1; sign += 2)
                                        {
                                            var tx = (int)Math.Clamp(Math.Round(x + dirX * r * sign), 0, width - 1);
                                            var ty = (int)Math.Clamp(Math.Round(y + dirY * r * sign), 0, height - 1);
                                            var targetIdx = (ty * width + tx) * 4;
                                            var addVal = starIntensity * falloff * 255;
                                            sbOutput[targetIdx] = Byte(sbOutput[targetIdx] + addVal);
                                            sbOutput[targetIdx + 1] = Byte(sbOutput[targetIdx + 1] + addVal);
                                            sbOutput[targetIdx + 2] = Byte(sbOutput[targetIdx + 2] + addVal);
                                        }
                                    }
                                }
                            }
                        }
                        output = sbOutput;
                    }
                    break;
                }
                case VideoEffectKind.TinyPlanet:
                {
                    var lat = P("Latitude") * Math.PI / 180.0; var lon = P("Longitude") * Math.PI / 180.0;
                    var zScale = Math.Max(0.1, P("Z"));
                    output = Resample(output, width, height, (x, y) =>
                    {
                        var nx = ((x + 0.5) / width * 2 - 1) * (zScale / 20.0);
                        var ny = ((y + 0.5) / height * 2 - 1) * (zScale / 20.0);
                        var r = Math.Sqrt(nx * nx + ny * ny);
                        if (r == 0) return (width * 0.5, height * 0.5);
                        var theta = Math.Atan2(ny, nx) + lon; var phi = 2 * Math.Atan(r) + lat;
                        var u = (theta / (2 * Math.PI) + 0.5) % 1.0; if (u < 0) u += 1.0;
                        var v = Math.Clamp(phi / Math.PI, 0, 1);
                        return (u * width - 0.5, v * height - 0.5);
                    }, true);
                    break;
                }
                case VideoEffectKind.Timecode:
                {
                    var tcScale = Math.Clamp(amount, 0.25, 2.0);
                    var hAlign = (int)P("HorizontalAlignment"); var vAlign = (int)P("VerticalAlignment");
                    var fps = 30.0; var totalSeconds = projectFrame / fps;
                    var hours = (int)(totalSeconds / 3600); var minutes = (int)((totalSeconds % 3600) / 60);
                    var seconds = (int)(totalSeconds % 60); var frames = (int)(projectFrame % (long)fps);
                    var tcText = $"{hours:D2}:{minutes:D2}:{seconds:D2}:{frames:D2}";
                    output = RenderTimecode(output, width, height, tcText, hAlign, vAlign, tcScale);
                    break;
                }
                case VideoEffectKind.AiAutoReframe:
                {
                    var previewMode = (int)P("Preview");
                    var cx = P("CenterX"); var cy = P("CenterY");
                    if (previewMode > 0)
                    {
                        var targetRatio = previewMode switch
                        {
                            1 => 16.0 / 9.0, // Landscape
                            2 => 9.0 / 16.0, // Portrait
                            3 => 4.0 / 3.0,  // Fullscreen
                            4 => 1.0,        // Square
                            5 => 2.4,        // Scope
                            _ => 9.0 / 16.0
                        };
                        var frameRatio = (double)width / height;
                        double boxW, boxH;
                        if (targetRatio < frameRatio)
                        {
                            boxH = height;
                            boxW = boxH * targetRatio;
                        }
                        else
                        {
                            boxW = width;
                            boxH = boxW / targetRatio;
                        }
                        var boxLeft = Math.Clamp(cx * width - boxW / 2.0, 0, width - boxW);
                        var boxTop = Math.Clamp(cy * height - boxH / 2.0, 0, height - boxH);
                        var boxRight = boxLeft + boxW; var boxBottom = boxTop + boxH;
                        var reframeOut = (byte[])output.Clone();
                        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                        {
                            var idx = (y * width + x) * 4;
                            var inside = x >= boxLeft && x <= boxRight && y >= boxTop && y <= boxBottom;
                            var isBorder = (Math.Abs(x - boxLeft) <= 1 || Math.Abs(x - boxRight) <= 1) && (y >= boxTop && y <= boxBottom) ||
                                           (Math.Abs(y - boxTop) <= 1 || Math.Abs(y - boxBottom) <= 1) && (x >= boxLeft && x <= boxRight);
                            if (isBorder)
                            {
                                reframeOut[idx] = 255; reframeOut[idx + 1] = 255; reframeOut[idx + 2] = 255;
                            }
                            else if (!inside)
                            {
                                reframeOut[idx] = Byte(output[idx] * 0.35);
                                reframeOut[idx + 1] = Byte(output[idx + 1] * 0.35);
                                reframeOut[idx + 2] = Byte(output[idx + 2] * 0.35);
                            }
                        }
                        output = reframeOut;
                    }
                    break;
                }
                case VideoEffectKind.AiColorization:
                {
                    var shiftA = P("ColorShiftA"); var shiftB = P("ColorShiftB");
                    var gainA = P("ColorMagnificationA"); var gainB = P("ColorMagnificationB");
                    var invA = P("InvertColorA") > 0.5; var invB = P("InvertColorB") > 0.5;
                    var colOut = (byte[])output.Clone();
                    for (var i = 0; i < output.Length; i += 4)
                    {
                        var b = output[i] / 255.0; var g = output[i + 1] / 255.0; var r = output[i + 2] / 255.0;
                        var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                        var ca = (luma - 0.5) * 0.25 + shiftA * 0.4; if (invA) ca = -ca;
                        var cb = (luma - 0.4) * 0.35 + shiftB * 0.4; if (invB) cb = -cb;
                        ca *= gainA; cb *= gainB;
                        colOut[i] = Byte((luma + cb * 1.5) * 255.0);
                        colOut[i + 1] = Byte((luma - (ca * 0.3 + cb * 0.2)) * 255.0);
                        colOut[i + 2] = Byte((luma + ca * 1.4) * 255.0);
                    }
                    output = colOut;
                    break;
                }
                case VideoEffectKind.AiDehaze:
                {
                    var gain = P("Gain"); var offset = P("Offset"); var str = amount;
                    if (str > 0 || gain != 1 || offset != 0)
                    {
                        var dehazeOut = (byte[])output.Clone();
                        for (var i = 0; i < output.Length; i += 4)
                        {
                            var b = output[i] / 255.0; var g = output[i + 1] / 255.0; var r = output[i + 2] / 255.0;
                            var dark = Math.Min(r, Math.Min(g, b));
                            var transmission = Math.Clamp(1.0 - 0.85 * str * dark, 0.15, 1.0);
                            const double atmospheric = 0.88;
                            var dr = ((r - atmospheric) / transmission + atmospheric) * gain + offset;
                            var dg = ((g - atmospheric) / transmission + atmospheric) * gain + offset;
                            var db = ((b - atmospheric) / transmission + atmospheric) * gain + offset;
                            dehazeOut[i] = Byte(db * 255.0);
                            dehazeOut[i + 1] = Byte(dg * 255.0);
                            dehazeOut[i + 2] = Byte(dr * 255.0);
                        }
                        output = dehazeOut;
                    }
                    break;
                }
                case VideoEffectKind.AiSharpen:
                {
                    var str = amount; var gain = P("Gain"); var offset = P("Offset");
                    if (str > 0 || gain != 1 || offset != 0)
                    {
                        var sharpOut = (byte[])output.Clone();
                        var sharpBlurred = Blur(output, width, height, 2, true);
                        for (var i = 0; i < output.Length; i += 4)
                        {
                            for (var c = 0; c < 3; c++)
                            {
                                var diff = output[i + c] - sharpBlurred[i + c];
                                sharpOut[i + c] = Byte((output[i + c] + diff * str * 1.8) * gain + offset * 255.0);
                            }
                        }
                        output = sharpOut;
                    }
                    break;
                }
                case VideoEffectKind.AiSmooth:
                {
                    var str = amount; var gain = P("Gain"); var offset = P("Offset");
                    if (str > 0 || gain != 1 || offset != 0)
                    {
                        var smoothOut = (byte[])output.Clone();
                        var smoothBlurred = Blur(output, width, height, (int)Math.Max(1, Math.Round(str * 3.0)), true);
                        for (var i = 0; i < output.Length; i += 4)
                        {
                            var b0 = output[i]; var g0 = output[i + 1]; var r0 = output[i + 2];
                            var b1 = smoothBlurred[i]; var g1 = smoothBlurred[i + 1]; var r1 = smoothBlurred[i + 2];
                            var diff = Math.Abs(r0 - r1) + Math.Abs(g0 - g1) + Math.Abs(b0 - b1);
                            var edgeWeight = Math.Clamp(1.0 - diff / 150.0, 0.0, 1.0);
                            var factor = str * edgeWeight;
                            smoothOut[i] = Byte((b0 * (1 - factor) + b1 * factor) * gain + offset * 255.0);
                            smoothOut[i + 1] = Byte((g0 * (1 - factor) + g1 * factor) * gain + offset * 255.0);
                            smoothOut[i + 2] = Byte((r0 * (1 - factor) + r1 * factor) * gain + offset * 255.0);
                        }
                        output = smoothOut;
                    }
                    break;
                }
                case VideoEffectKind.AiStyleTransfer:
                {
                    var str = amount;
                    if (str > 0)
                    {
                        var styleIdx = (int)P("Style");
                        var styleOut = (byte[])output.Clone();
                        for (var i = 0; i < output.Length; i += 4)
                        {
                            var b = output[i] / 255.0; var g = output[i + 1] / 255.0; var r = output[i + 2] / 255.0;
                            var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                            var posterized = Math.Floor(luma * 6.0 + 0.5) / 6.0;
                            double sr, sg, sb;
                            switch (styleIdx % 6)
                            {
                                case 0: sr = posterized * 1.2; sg = posterized * 0.8; sb = posterized * 0.6; break; // Warm Picasso
                                case 1: sr = posterized * 0.5; sg = posterized * 0.8; sb = posterized * 1.3; break; // Night Alley Blue
                                case 2: sr = posterized * 0.6; sg = posterized * 1.1; sb = posterized * 1.2; break; // Great Wave Cyan
                                case 3: sr = posterized * 1.1; sg = posterized * 0.9; sb = posterized * 0.4; break; // Starry Night Yellow/Blue
                                case 4: sr = posterized; sg = posterized; sb = posterized; break; // B&W
                                default: sr = posterized * 1.1; sg = posterized * 0.6; sb = posterized * 0.9; break; // Vibrant
                            }
                            styleOut[i] = Byte((b * (1 - str) + sb * str) * 255.0);
                            styleOut[i + 1] = Byte((g * (1 - str) + sg * str) * 255.0);
                            styleOut[i + 2] = Byte((r * (1 - str) + sr * str) * 255.0);
                        }
                        output = styleOut;
                    }
                    break;
                }
                case VideoEffectKind.AiUpscale:
                {
                    var scaling = P("Scaling"); var sharpen = P("ImproveSharpness") > 0.5;
                    var upOut = (byte[])output.Clone();
                    var factor = (scaling - 1.0) * 0.5 + (sharpen ? 0.3 : 0.0);
                    if (factor > 0)
                    {
                        var upBlurred = Blur(output, width, height, 1, true);
                        for (var i = 0; i < output.Length; i += 4)
                        {
                            for (var c = 0; c < 3; c++)
                            {
                                var diff = output[i + c] - upBlurred[i + c];
                                upOut[i + c] = Byte(output[i + c] + diff * factor);
                            }
                        }
                        output = upOut;
                    }
                    break;
                }
                case VideoEffectKind.AiZDepth:
                {
                    var vis = (int)P("Visualization"); var zInvert = P("Invert") > 0.5;
                    var dMin = P("DepthMin"); var dMax = Math.Max(dMin + 0.01, P("DepthMax"));
                    var zOut = (byte[])output.Clone();
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var idx = (y * width + x) * 4;
                        var b = output[idx] / 255.0; var g = output[idx + 1] / 255.0; var r = output[idx + 2] / 255.0;
                        var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                        var vGrad = 1.0 - (double)y / height;
                        var depth = Math.Clamp((luma * 0.4 + vGrad * 0.6 - dMin) / (dMax - dMin), 0.0, 1.0);
                        if (zInvert) depth = 1.0 - depth;
                        if (vis == 1) // Thresholded depth
                        {
                            var thresh = depth >= 0.5 ? 255 : 0;
                            zOut[idx] = (byte)thresh; zOut[idx + 1] = (byte)thresh; zOut[idx + 2] = (byte)thresh;
                        }
                        else if (vis == 2) // Depth
                        {
                            var dByte = Byte(depth * 255.0);
                            zOut[idx] = dByte; zOut[idx + 1] = dByte; zOut[idx + 2] = dByte;
                        }
                        else if (vis == 3) // Opacity
                        {
                            zOut[idx + 3] = Byte(output[idx + 3] * depth);
                        }
                    }
                    output = zOut;
                    break;
                }
                case VideoEffectKind.AiSmartMask:
                {
                    var vis = (int)P("Visualization"); var smInvert = P("Invert") > 0.5;
                    var conf = P("Confidence"); var maskColorIdx = (int)P("MaskChooser");
                    var smOut = (byte[])output.Clone();
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var idx = (y * width + x) * 4;
                        var nx = (x + 0.5) / width - 0.5; var ny = (y + 0.5) / height - 0.5;
                        var dist = Math.Sqrt(nx * nx + ny * ny) * 2.0;
                        var b = output[idx] / 255.0; var g = output[idx + 1] / 255.0; var r = output[idx + 2] / 255.0;
                        var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                        var maskVal = Math.Clamp((1.0 - dist) * 0.7 + luma * 0.3 + conf * 0.3, 0.0, 1.0);
                        if (smInvert) maskVal = 1.0 - maskVal;
                        if (vis == 1) // View Matte
                        {
                            var mByte = Byte(maskVal * 255.0);
                            smOut[idx] = mByte; smOut[idx + 1] = mByte; smOut[idx + 2] = mByte;
                        }
                        else if (vis == 2) // Mask Overlay
                        {
                            (double cr, double cg, double cb) = (maskColorIdx % 4) switch
                            {
                                0 => (0.1, 0.4, 0.9), // blue
                                1 => (0.1, 0.8, 0.2), // green
                                2 => (0.9, 0.2, 0.1), // red
                                _ => (0.9, 0.8, 0.1)  // yellow
                            };
                            var alpha = maskVal * 0.4;
                            smOut[idx] = Byte(output[idx] * (1 - alpha) + cb * 255 * alpha);
                            smOut[idx + 1] = Byte(output[idx + 1] * (1 - alpha) + cg * 255 * alpha);
                            smOut[idx + 2] = Byte(output[idx + 2] * (1 - alpha) + cr * 255 * alpha);
                        }
                        else
                        {
                            smOut[idx + 3] = Byte(output[idx + 3] * maskVal);
                        }
                    }
                    output = smOut;
                    break;
                }
                default: output = ApplyColor(output, effect); break;
            }
        }
        return output;
    }

    private static byte[] ApplyColor(byte[] source, VideoEffect effect)
    {
        double P(string key) => effect.GetParameter(key);
        var a = effect.Amount; var secondary = effect.SecondaryAmount;
        Func<double, double, double, (double R, double G, double B)> operation;
        switch (effect.Kind)
        {
            case VideoEffectKind.BrightnessContrast: operation = (r, g, b) => ((r - .5) * (1 + secondary) + .5 + a, (g - .5) * (1 + secondary) + .5 + a, (b - .5) * (1 + secondary) + .5 + a); break;
            case VideoEffectKind.Invert:
            case VideoEffectKind.BlackAndWhite:
                return ApplyBoundedBlend(source, effect.Kind, a);
            case VideoEffectKind.Saturation:
                var saturation = a;
                operation = (r, g, b) => { var l = .2126 * r + .7152 * g + .0722 * b; return (l + (r - l) * saturation, l + (g - l) * saturation, l + (b - l) * saturation); }; break;
            case VideoEffectKind.Sepia: operation = (r, g, b) => (r * (1 - a) + (.393 * r + .769 * g + .189 * b) * a, g * (1 - a) + (.349 * r + .686 * g + .168 * b) * a, b * (1 - a) + (.272 * r + .534 * g + .131 * b) * a); break;
            case VideoEffectKind.ColorBalance:
                var red = P("Red"); var green = P("Green"); var blue = P("Blue"); operation = (r, g, b) => (r + red, g + green, b + blue); break;
            case VideoEffectKind.WhiteBalance:
                var temperature = P("Temperature") * .25; var tint = P("Tint") * .25; operation = (r, g, b) => (r * (1 + temperature), g * (1 - tint), b * (1 - temperature)); break;
            case VideoEffectKind.Levels:
                return ApplyLevels(source, effect);
            case VideoEffectKind.Threshold: operation = (r, g, b) => { var l = .2126 * r + .7152 * g + .0722 * b >= a ? 1.0 : 0; return (l, l, l); }; break;
            case VideoEffectKind.BroadcastColors: operation = (r, g, b) => (r * (1 - a) + (16 + 219 * r) / 255 * a, g * (1 - a) + (16 + 219 * g) / 255 * a, b * (1 - a) + (16 + 219 * b) / 255 * a); break;
            case VideoEffectKind.BlackRestore: operation = (r, g, b) => ((r - a) / (1 - a), (g - a) / (1 - a), (b - a) / (1 - a)); break;
            case VideoEffectKind.ChannelBlend:
                var rg = P("Red"); var gg = P("Green"); var bGain = P("Blue"); var cross = P("Crossfeed");
                operation = (r, g, b) => ((r * (1 - cross) + g * cross) * rg, (g * (1 - cross) + b * cross) * gg, (b * (1 - cross) + r * cross) * bGain); break;
            case VideoEffectKind.SoftContrast:
                double Curve(double value) => a >= 0 ? value + (value * value * (3 - 2 * value) - value) * a : value + (Math.Sqrt(value) * .5 + value * .5 - value) * -a;
                operation = (r, g, b) => (Curve(r), Curve(g), Curve(b)); break;
            case VideoEffectKind.FillLight: operation = (r, g, b) => (r + a * (1 - r) * (1 - r), g + a * (1 - g) * (1 - g), b + a * (1 - b) * (1 - b)); break;
            case VideoEffectKind.HslAdjust:
                var hue = P("Hue") / 360; var hslSaturation = P("Saturation"); var lightness = P("Lightness");
                operation = (r, g, b) => AdjustHsl(r, g, b, hue, hslSaturation, lightness); break;
            case VideoEffectKind.GradientMap:
                var gMode = (int)P("Mode");
                operation = (r, g, b) =>
                {
                    var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    var mapR = 0.1 + luma * 0.9; var mapG = 0.05 + luma * 0.85; var mapB = 0.25 + luma * 0.45;
                    return gMode == 1 ? (r + mapR * a, g + mapG * a, b + mapB * a) : (r * (1 - a) + mapR * a, g * (1 - a) + mapG * a, b * (1 - a) + mapB * a);
                }; break;
            case VideoEffectKind.LabAdjust:
                var lScale = P("Lightness"); var chA = P("ChannelA") * 100.0; var chB = P("ChannelB") * 100.0;
                var invL = (int)P("InvertLuminance"); var desat = P("Desaturate");
                operation = (r, g, b) =>
                {
                    var x = r * 0.4124 + g * 0.3576 + b * 0.1805;
                    var y = r * 0.2126 + g * 0.7152 + b * 0.0722;
                    var z = r * 0.0193 + g * 0.1192 + b * 0.9505;
                    double F(double t) => t > 0.008856 ? Math.Cbrt(t) : (7.787 * t + 16.0 / 116.0);
                    var fx = F(x / 0.95047); var fy = F(y / 1.00000); var fz = F(z / 1.08883);
                    var lVal = (116.0 * fy - 16.0) * lScale;
                    if (invL == 1) lVal = 100.0 - lVal;
                    var aVal = (500.0 * (fx - fy) + chA) * (1 - desat);
                    var bVal = (200.0 * (fy - fz) + chB) * (1 - desat);
                    var fy2 = (lVal + 16.0) / 116.0; var fx2 = aVal / 500.0 + fy2; var fz2 = fy2 - bVal / 200.0;
                    double InvF(double t) => t * t * t > 0.008856 ? t * t * t : (t - 16.0 / 116.0) / 7.787;
                    var x2 = InvF(fx2) * 0.95047; var y2 = InvF(fy2) * 1.00000; var z2 = InvF(fz2) * 1.08883;
                    var rOut = x2 * 3.2406 - y2 * 1.5372 - z2 * 0.4986;
                    var gOut = -x2 * 0.9689 + y2 * 1.8758 + z2 * 0.0415;
                    var bOut = x2 * 0.0557 - y2 * 0.2040 + z2 * 1.0570;
                    return (Math.Clamp(rOut, 0, 1), Math.Clamp(gOut, 0, 1), Math.Clamp(bOut, 0, 1));
                }; break;
            default: throw new InvalidDataException("Video effect has no renderer.");
        }
        return Color(source, operation);
    }

    private static byte[] ApplyLevels(byte[] source, VideoEffect effect)
    {
        var black = effect.GetParameter("InputBlack"); var white = effect.GetParameter("InputWhite");
        var gamma = effect.GetParameter("Gamma"); var outputBlack = effect.GetParameter("OutputBlack"); var outputWhite = effect.GetParameter("OutputWhite");
        var selection = (int)effect.GetParameter("Channel");
        var channel = selection switch { 1 => 2, 2 => 1, 3 => 0, 4 => 3, _ => -1 };
        var output = (byte[])source.Clone();
        for (var i = 0; i < source.Length; i += 4)
        {
            // All adjusts RGB; Alpha adjusts coverage alone, so lower timeline layers remain visible.
            var first = channel < 0 ? 0 : channel;
            var last = channel < 0 ? 2 : channel;
            for (var c = first; c <= last; c++)
            {
                var normalized = Math.Clamp((source[i + c] / 255.0 - black) / (white - black), 0, 1);
                var value = outputBlack + Math.Pow(normalized, 1 / gamma) * (outputWhite - outputBlack);
                output[i + c] = (byte)(Math.Clamp(value, 0, 1) * 255);
            }
        }
        return output;
    }

    private static byte[] ApplyBoundedBlend(byte[] source, VideoEffectKind kind, double amount)
    {
        var output = (byte[])source.Clone();
        for (var i = 0; i < source.Length; i += 4)
        {
            var gray = (source[i] + source[i + 1] + source[i + 2]) / 3.0;
            for (var channel = 0; channel < 3; channel++)
            {
                var value = source[i + channel];
                var target = kind == VideoEffectKind.Invert ? 255 - value : gray;
                // Blend in byte space, then truncate once. Alpha remains the original source coverage.
                output[i + channel] = (byte)(value + (target - value) * amount);
            }
        }
        return output;
    }

    private static byte[] Color(byte[] source, Func<double, double, double, (double R, double G, double B)> operation)
    {
        var output = (byte[])source.Clone();
        for (var i = 0; i < output.Length; i += 4)
        { var (r, g, b) = operation(source[i + 2] / 255.0, source[i + 1] / 255.0, source[i] / 255.0); output[i] = Channel(b); output[i + 1] = Channel(g); output[i + 2] = Channel(r); }
        return output;
    }

    private static (double R, double G, double B) AdjustHsl(double r, double g, double b, double rotation, double saturation, double lightness)
    {
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min; var l = (max + min) / 2; var s = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * l - 1));
        var h = delta == 0 ? 0 : max == r ? ((g - b) / delta) % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        h = Wrap(h / 6 + rotation, 1); s = Math.Clamp(s * saturation, 0, 1); l = Math.Clamp(l + lightness, 0, 1);
        var c = (1 - Math.Abs(2 * l - 1)) * s; var x = c * (1 - Math.Abs((h * 6) % 2 - 1)); var m = l - c / 2;
        return ((int)(h * 6)) switch { 0 => (c + m, x + m, m), 1 => (x + m, c + m, m), 2 => (m, c + m, x + m), 3 => (m, x + m, c + m), 4 => (x + m, m, c + m), _ => (c + m, m, x + m) };
    }

    private static int Radius(double percent, int width, int height) => percent <= 0 ? 0 : Math.Max(1, (int)Math.Round(Math.Min(width, height) * percent / 100));
    private static double Wrap(double value, double size) => (value % size + size) % size;
    private static double Noise(uint value)
    { value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15; value *= 0x846ca68b; value ^= value >> 16; return value / (double)uint.MaxValue - .5; }
    private static byte Channel(double value) => Byte(value * 255);
    private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static double[] BoxBlur1D(double[] input, int width, int height, int radius, bool horizontal)
    {
        if (radius <= 0) return input;
        var output = new double[input.Length];
        if (horizontal)
        {
            for (var y = 0; y < height; y++)
            {
                var rowStart = y * width;
                for (var x = 0; x < width; x++)
                {
                    double sum = 0; int count = 0;
                    for (var r = -radius; r <= radius; r++)
                    {
                        var sx = Math.Clamp(x + r, 0, width - 1);
                        sum += input[rowStart + sx]; count++;
                    }
                    output[rowStart + x] = sum / count;
                }
            }
        }
        else
        {
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    double sum = 0; int count = 0;
                    for (var r = -radius; r <= radius; r++)
                    {
                        var sy = Math.Clamp(y + r, 0, height - 1);
                        sum += input[sy * width + x]; count++;
                    }
                    output[y * width + x] = sum / count;
                }
            }
        }
        return output;
    }

    private static byte[] RenderTimecode(byte[] source, int width, int height, string text, int hAlign, int vAlign, double scale)
    {
        var output = (byte[])source.Clone();
        var charW = (int)Math.Max(3, Math.Round(5 * scale));
        var charH = (int)Math.Max(5, Math.Round(7 * scale));
        var spacing = (int)Math.Max(1, Math.Round(2 * scale));
        var totalW = text.Length * (charW + spacing) + spacing;
        var totalH = charH + spacing * 2;
        var startX = hAlign switch { 0 => 10, 1 => (width - totalW) / 2, _ => width - totalW - 10 };
        var startY = vAlign switch { 0 => 10, 1 => (height - totalH) / 2, _ => height - totalH - 10 };
        startX = Math.Clamp(startX, 0, Math.Max(0, width - totalW));
        startY = Math.Clamp(startY, 0, Math.Max(0, height - totalH));

        for (var y = startY; y < Math.Min(height, startY + totalH); y++)
        for (var x = startX; x < Math.Min(width, startX + totalW); x++)
        {
            var idx = (y * width + x) * 4;
            output[idx] = (byte)(output[idx] * 0.2);
            output[idx + 1] = (byte)(output[idx + 1] * 0.2);
            output[idx + 2] = (byte)(output[idx + 2] * 0.2);
        }

        for (var ci = 0; ci < text.Length; ci++)
        {
            var ch = text[ci];
            var glyph = GetGlyph(ch);
            var gx = startX + spacing + ci * (charW + spacing);
            for (var row = 0; row < 7; row++)
            for (var col = 0; col < 5; col++)
            {
                if ((glyph[row] & (1 << (4 - col))) != 0)
                {
                    var step = Math.Max(1, (int)Math.Ceiling(scale));
                    for (var sy = 0; sy < step; sy++)
                    for (var sx = 0; sx < step; sx++)
                    {
                        var px = gx + (int)(col * scale) + sx;
                        var py = startY + spacing + (int)(row * scale) + sy;
                        if (px >= 0 && px < width && py >= 0 && py < height)
                        {
                            var idx = (py * width + px) * 4;
                            output[idx] = 255; output[idx + 1] = 255; output[idx + 2] = 255; output[idx + 3] = 255;
                        }
                    }
                }
            }
        }
        return output;
    }

    private static byte[] GetGlyph(char c) => c switch
    {
        '0' => [0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110],
        '1' => [0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110],
        '2' => [0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111],
        '3' => [0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110],
        '4' => [0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010],
        '5' => [0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110],
        '6' => [0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110],
        '7' => [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000],
        '8' => [0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110],
        '9' => [0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100],
        ':' => [0b00000, 0b01100, 0b01100, 0b00000, 0b01100, 0b01100, 0b00000],
        _   => [0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000]
    };
}
