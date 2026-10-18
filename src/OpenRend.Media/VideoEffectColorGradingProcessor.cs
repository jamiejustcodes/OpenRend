using OpenRend.Core;

namespace OpenRend.Media;

internal static partial class VideoEffectProcessor
{
    private static byte[] ApplyColorGrading(byte[] source, VideoEffect effect)
    {
        var output = source;
        double P(string key) => effect.GetParameter(key);

        // 1. Camera LUT (Slot 0)
        if (effect.Lut is not null && P("LUTEnable_0") == 0 && P("LUTGain_0") > 0)
        {
            output = ApplyLut(output, effect.Lut, P("LUTGain_0"), P("LUTInterpolation_0") == 1);
        }

        // 2. Precompute Levels parameters
        var inMin = P("InMinAdjust");
        var inMax = P("InMaxAdjust");
        var outMin = P("OutMinAdjust");
        var outMax = P("OutMaxAdjust");
        var hasLevels = inMin != 0 || inMax != 1 || outMin != 0 || outMax != 1;
        var inRange = Math.Max(0.0001, inMax - inMin);
        var outRange = outMax - outMin;

        // 3. Precompute White Balance parameters
        var temp = P("TemperatureAdjust");
        var tint = P("TintAdjust");
        var hasWB = temp != 0 || tint != 0;
        var deltaT = temp / 4000.0;
        var deltaTint = tint / 100.0;

        // 4. Precompute Tone parameters
        var exposure = P("ExposureLnAdjust");
        var hasExposure = exposure != 0;
        var expScale = Math.Pow(2, exposure);
        var brightness = P("BrightnessAdjust");
        var contrast = P("ContrastAdjust");
        var contrastCenter = P("ContrastCtrAdjust");
        var hasContrast = contrast != 0;
        var contrastFactor = contrast >= 0 ? 1.0 + contrast : 1.0 / (1.0 - contrast);
        var saturation = P("SaturationAdjust");
        var hasSaturation = saturation != 1;

        // 5. Precompute Color Wheels parameters
        var liftR = P("Lift_R") + P("Lift_Y");
        var liftG = P("Lift_G") + P("Lift_Y");
        var liftB = P("Lift_B") + P("Lift_Y");
        var hasLift = liftR != 0 || liftG != 0 || liftB != 0;

        var gammaR = P("Gamma_R") * P("Gamma_Y");
        var gammaG = P("Gamma_G") * P("Gamma_Y");
        var gammaB = P("Gamma_B") * P("Gamma_Y");
        var hasGamma = gammaR != 1 || gammaG != 1 || gammaB != 1;
        var invGammaR = gammaR > 0 ? 1.0 / gammaR : 1.0;
        var invGammaG = gammaG > 0 ? 1.0 / gammaG : 1.0;
        var invGammaB = gammaB > 0 ? 1.0 / gammaB : 1.0;

        var gainR = P("Gain_R") * P("Gain_Y");
        var gainG = P("Gain_G") * P("Gain_Y");
        var gainB = P("Gain_B") * P("Gain_Y");
        var hasGain = gainR != 1 || gainG != 1 || gainB != 1;

        var offsetR = P("Offset_R") + P("Offset_Y");
        var offsetG = P("Offset_G") + P("Offset_Y");
        var offsetB = P("Offset_B") + P("Offset_Y");
        var hasOffset = offsetR != 0 || offsetG != 0 || offsetB != 0;

        // 6. Precompute HSL parameters
        var hue = P("HueAdjust");
        var satHsl = P("SatAdjust");
        var valHsl = P("ValAdjust");
        var hasHsl = hue != 0 || satHsl != 1 || valHsl != 1;

        // 7. Precompute Curves LUTs
        var curves = effect.Curves;
        var hasCurves = curves is not null && curves.HasAdjustments;
        var masterLut = hasCurves && !curves!.Master.IsIdentity ? curves.Master.BuildLut() : null;
        var redLut = hasCurves && !curves!.Red.IsIdentity ? curves.Red.BuildLut() : null;
        var greenLut = hasCurves && !curves!.Green.IsIdentity ? curves.Green.BuildLut() : null;
        var blueLut = hasCurves && !curves!.Blue.IsIdentity ? curves.Blue.BuildLut() : null;

        // Apply primary grading operations in single color transform pass
        output = Color(output, (red, green, blue) =>
        {
            // Levels
            if (hasLevels)
            {
                red = outMin + Math.Clamp((red - inMin) / inRange, 0.0, 1.0) * outRange;
                green = outMin + Math.Clamp((green - inMin) / inRange, 0.0, 1.0) * outRange;
                blue = outMin + Math.Clamp((blue - inMin) / inRange, 0.0, 1.0) * outRange;
            }

            // White Balance
            if (hasWB)
            {
                red += deltaT * 0.2 + deltaTint * 0.075;
                green -= deltaTint * 0.15;
                blue += -deltaT * 0.2 + deltaTint * 0.075;
            }

            // Exposure
            if (hasExposure)
            {
                red *= expScale;
                green *= expScale;
                blue *= expScale;
            }

            // Brightness
            if (brightness != 0)
            {
                red += brightness;
                green += brightness;
                blue += brightness;
            }

            // Contrast
            if (hasContrast)
            {
                red = contrastCenter + (red - contrastCenter) * contrastFactor;
                green = contrastCenter + (green - contrastCenter) * contrastFactor;
                blue = contrastCenter + (blue - contrastCenter) * contrastFactor;
            }

            // Saturation
            if (hasSaturation)
            {
                var luma = 0.2126 * red + 0.7152 * green + 0.0722 * blue;
                red = luma + (red - luma) * saturation;
                green = luma + (green - luma) * saturation;
                blue = luma + (blue - luma) * saturation;
            }

            // Color Wheels: Lift
            if (hasLift)
            {
                red += liftR * (1.0 - Math.Clamp(red, 0.0, 1.0));
                green += liftG * (1.0 - Math.Clamp(green, 0.0, 1.0));
                blue += liftB * (1.0 - Math.Clamp(blue, 0.0, 1.0));
            }

            // Color Wheels: Gamma
            if (hasGamma)
            {
                if (red > 0) red = Math.Pow(red, invGammaR);
                if (green > 0) green = Math.Pow(green, invGammaG);
                if (blue > 0) blue = Math.Pow(blue, invGammaB);
            }

            // Color Wheels: Gain
            if (hasGain)
            {
                red *= gainR;
                green *= gainG;
                blue *= gainB;
            }

            // Color Wheels: Offset
            if (hasOffset)
            {
                red += offsetR;
                green += offsetG;
                blue += offsetB;
            }

            // Curves (RGB / Master)
            if (hasCurves)
            {
                var rByte = (byte)Math.Clamp((int)Math.Round(Math.Clamp(red, 0.0, 1.0) * 255.0), 0, 255);
                var gByte = (byte)Math.Clamp((int)Math.Round(Math.Clamp(green, 0.0, 1.0) * 255.0), 0, 255);
                var bByte = (byte)Math.Clamp((int)Math.Round(Math.Clamp(blue, 0.0, 1.0) * 255.0), 0, 255);

                if (masterLut is not null)
                {
                    rByte = masterLut[rByte];
                    gByte = masterLut[gByte];
                    bByte = masterLut[bByte];
                }
                if (redLut is not null) rByte = redLut[rByte];
                if (greenLut is not null) gByte = greenLut[gByte];
                if (blueLut is not null) bByte = blueLut[bByte];

                red = rByte / 255.0;
                green = gByte / 255.0;
                blue = bByte / 255.0;
            }

            // HSL
            if (hasHsl)
            {
                var (rh, gh, bh) = AdjustHsl(Math.Clamp(red, 0.0, 1.0), Math.Clamp(green, 0.0, 1.0), Math.Clamp(blue, 0.0, 1.0), hue, satHsl, (valHsl - 1.0) * 0.5);
                red = rh;
                green = gh;
                blue = bh;
            }

            return (red, green, blue);
        });

        // 7. Look LUT (Slot 1)
        if (effect.LookLut is not null && P("LUTEnable_1") == 0 && P("LUTGain_1") > 0)
        {
            output = ApplyLut(output, effect.LookLut, P("LUTGain_1"), P("LUTInterpolation_1") == 1);
        }

        return output;
    }
}
