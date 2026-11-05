using System;
using System.Text.Json.Serialization;

namespace OpenRend.Core;

/// <summary>
/// 1:1 Vegas Pro Audio Track and Bus Pan Modes matching ScriptPortal.Vegas.PanType.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AudioPanMode>))]
public enum AudioPanMode
{
    Balance = 0,         // Balance (0 dB Center) - Vegas Pro default
    ConstantPower = 1,   // Constant Power (-3 dB Center)
    AddChannels = 2,     // Add Channels (0 dB Center)
    Notch3Db = 3,        // Balance (-3 dB Center)
    Notch6Db = 4         // Balance (-6 dB Center)
}

/// <summary>
/// Mathematical pan laws evaluating stereo channel multipliers for any pan in [-1.0, +1.0].
/// </summary>
public static class AudioPanLaw
{
    public const double CenterNotch3Db = 0.7071067811865475; // -3.01 dB = 10^(-3.0103/20) = sqrt(0.5)
    public const double CenterNotch6Db = 0.5000000000000000; // -6.02 dB = 10^(-6.0206/20) = 0.5

    /// <summary>
    /// Calculates Left and Right channel linear gain factors for a given pan position [-1.0 .. +1.0]
    /// according to the specified Vegas Pro pan model.
    /// </summary>
    public static (double Left, double Right) CalculateGains(AudioPanMode mode, double pan)
    {
        pan = Math.Clamp(pan, -1.0, 1.0);

        return mode switch
        {
            AudioPanMode.ConstantPower => ConstantPowerGains(pan),
            AudioPanMode.AddChannels => (Math.Clamp(1.0 - pan, 0.0, 1.0), Math.Clamp(1.0 + pan, 0.0, 1.0)),
            AudioPanMode.Notch3Db => NotchGains(pan, CenterNotch3Db),
            AudioPanMode.Notch6Db => NotchGains(pan, CenterNotch6Db),
            AudioPanMode.Balance or _ => BalanceGains(pan)
        };
    }

    /// <summary>
    /// Standard Vegas Pro stereo balance: at center (0.0), both channels are 1.0 (0 dB).
    /// Panning left attenuates the right channel linearly; panning right attenuates the left channel.
    /// </summary>
    private static (double Left, double Right) BalanceGains(double pan)
    {
        return (pan <= 0 ? 1.0 : 1.0 - pan,
                pan >= 0 ? 1.0 : 1.0 + pan);
    }

    /// <summary>
    /// Sine/cosine equal power pan law: maintains total acoustic power L^2 + R^2 = 1.0 across the stereo field.
    /// Center gain is cos(pi/4) = sin(pi/4) = sqrt(2)/2 ≈ 0.7071 (-3 dB).
    /// </summary>
    private static (double Left, double Right) ConstantPowerGains(double pan)
    {
        var angle = (pan + 1.0) * (Math.PI / 4.0); // maps [-1, 1] to [0, pi/2]
        return (Math.Cos(angle), Math.Sin(angle));
    }

    /// <summary>
    /// Linear balance curve notched down at center to the specified center attenuation.
    /// Full left (-1.0) produces (1.0, 0.0); Center (0.0) produces (center, center); Full right (+1.0) produces (0.0, 1.0).
    /// </summary>
    private static (double Left, double Right) NotchGains(double pan, double center)
    {
        if (pan <= 0)
        {
            var t = pan + 1.0; // 0.0 (left) to 1.0 (center)
            return (1.0 - (1.0 - center) * t, center * t);
        }
        else
        {
            var t = pan; // 0.0 (center) to 1.0 (right)
            return (center * (1.0 - t), center + (1.0 - center) * t);
        }
    }
}
