using OpenRend.Media;

namespace OpenRend.App;

internal sealed record MasterMeterReading(double Left, double Right, double HeldLeft, double HeldRight,
    bool LeftClipped, bool RightClipped, double MomentaryLufs, double ShortTermLufs, double IntegratedLufs, double TruePeakDb)
{
    public static MasterMeterReading Empty { get; } = new(0, 0, 0, 0, false, false, double.NaN, double.NaN,
        double.NaN, double.NegativeInfinity);
}

internal sealed class MasterAudioMeter
{
    private const int MomentaryFrames = MediaService.SampleRate * 2 / 5;
    private const int ShortTermFrames = MediaService.SampleRate * 3;
    private const int GateHopFrames = MomentaryFrames / 4;
    private const int TapCount = 12;
    private static readonly double AbsoluteGatePower = Math.Pow(10, (-70 + .691) / 10);
    private readonly Channel left = new();
    private readonly Channel right = new();
    private readonly double[] energy = new double[ShortTermFrames];
    private readonly List<double> gatedBlocks = [];
    private double momentarySum, shortTermSum;
    private double absoluteGatedSum;
    private double integratedLufs = double.NaN;
    private long frames;
    private long nextGateFrame = MomentaryFrames;
    private long nextIntegratedReading = MomentaryFrames;
    private int energyIndex;
    private PeakHold leftHold, rightHold, truePeakHold;
    private bool leftClipped, rightClipped;

    public MasterMeterReading Measure(ReadOnlySpan<float> stereo)
    {
        double leftPeak = 0, rightPeak = 0, leftTruePeak = 0, rightTruePeak = 0;
        for (var i = 0; i < stereo.Length; i += 2)
        {
            var l = stereo[i];
            var r = stereo[i + 1];
            leftPeak = Math.Max(leftPeak, Math.Abs(l));
            rightPeak = Math.Max(rightPeak, Math.Abs(r));
            leftTruePeak = Math.Max(leftTruePeak, left.TruePeak(l));
            rightTruePeak = Math.Max(rightTruePeak, right.TruePeak(r));
            var power = left.WeightedPower(l) + right.WeightedPower(r);
            var momentaryExit = energyIndex >= MomentaryFrames ? energyIndex - MomentaryFrames : energyIndex + ShortTermFrames - MomentaryFrames;
            momentarySum += power - energy[momentaryExit];
            shortTermSum += power - energy[energyIndex];
            energy[energyIndex] = power;
            if (++energyIndex == ShortTermFrames) energyIndex = 0;
            if (++frames == nextGateFrame)
            {
                nextGateFrame += GateHopFrames;
                var blockPower = momentarySum / MomentaryFrames;
                if (double.IsFinite(blockPower) && blockPower > AbsoluteGatePower)
                {
                    gatedBlocks.Add(blockPower);
                    absoluteGatedSum += blockPower;
                }
            }
        }

        // Read the exact relative gate once per second rather than rescanning on every audio buffer.
        if (frames >= nextIntegratedReading)
        {
            integratedLufs = ReadIntegratedLoudness();
            nextIntegratedReading += ((frames - nextIntegratedReading) / MediaService.SampleRate + 1) * MediaService.SampleRate;
        }
        leftClipped |= Math.Max(leftPeak, leftTruePeak) > 1;
        rightClipped |= Math.Max(rightPeak, rightTruePeak) > 1;
        var heldTruePeak = truePeakHold.Update(Math.Max(leftTruePeak, rightTruePeak), frames);
        return new(leftPeak, rightPeak, leftHold.Update(leftPeak, frames), rightHold.Update(rightPeak, frames), leftClipped, rightClipped,
            Loudness(momentarySum, MomentaryFrames), Loudness(shortTermSum, ShortTermFrames), integratedLufs, 20 * Math.Log10(heldTruePeak));
    }

    internal void ClearClips() => leftClipped = rightClipped = false;

    internal double ReadIntegratedLoudness()
    {
        if (frames < MomentaryFrames) return double.NaN;
        if (gatedBlocks.Count == 0) return double.NegativeInfinity;
        var threshold = absoluteGatedSum / gatedBlocks.Count / 10;
        double sum = 0;
        var count = 0;
        foreach (var power in gatedBlocks)
        {
            if (power <= threshold) continue;
            sum += power;
            count++;
        }
        return ToLufs(sum / count);
    }

    private double Loudness(double sum, int count) => frames < count ? double.NaN : ToLufs(Math.Max(0, sum / count));
    private static double ToLufs(double power) => -.691 + 10 * Math.Log10(power);

    private struct PeakHold
    {
        private double held;
        private long until;

        public double Update(double peak, long frame)
        {
            if (peak >= held || frame >= until)
            {
                held = peak;
                until = frame + MediaService.SampleRate;
            }
            return held;
        }
    }

    private sealed class Channel
    {
        // ITU-R BS.1770-5 Annex 1, tables 1 and 2, at 48 kHz.
        private Biquad shelf = new(1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585);
        private Biquad highPass = new(1, -2, 1, -1.99004745483398, 0.99007225036621);
        private readonly float[] samples = new float[TapCount * 2];
        private int index;

        // Annex 2's four interpolation phases in filter order.
        private static readonly double[][] Phases =
        [
            [0.001708984375, 0.010986328125, -0.0196533203125, 0.033203125, -0.0594482421875, 0.1373291015625,
                0.97216796875, -0.102294921875, 0.047607421875, -0.026611328125, 0.014892578125, -0.00830078125],
            [-0.0291748046875, 0.029296875, -0.0517578125, 0.089111328125, -0.16650390625, 0.465087890625,
                0.77978515625, -0.2003173828125, 0.1015625, -0.0582275390625, 0.0330810546875, -0.0189208984375],
            [-0.0189208984375, 0.0330810546875, -0.0582275390625, 0.1015625, -0.2003173828125, 0.77978515625,
                0.465087890625, -0.16650390625, 0.089111328125, -0.0517578125, 0.029296875, -0.0291748046875],
            [-0.00830078125, 0.014892578125, -0.026611328125, 0.047607421875, -0.102294921875, 0.97216796875,
                0.1373291015625, -0.0594482421875, 0.033203125, -0.0196533203125, 0.010986328125, 0.001708984375]
        ];

        public double WeightedPower(double sample)
        {
            var filtered = highPass.Filter(shelf.Filter(sample));
            return filtered * filtered;
        }

        public double TruePeak(float sample)
        {
            if (--index < 0) index = TapCount - 1;
            samples[index] = samples[index + TapCount] = sample;
            var peak = (double)Math.Abs(sample);
            foreach (var phase in Phases)
            {
                double filtered = 0;
                for (var tap = 0; tap < TapCount; tap++) filtered += phase[tap] * samples[index + tap];
                peak = Math.Max(peak, Math.Abs(filtered));
            }
            return peak;
        }
    }

    private struct Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double z1, z2;

        public double Filter(double input)
        {
            var output = b0 * input + z1;
            z1 = b1 * input - a1 * output + z2;
            z2 = b2 * input - a2 * output;
            return output;
        }
    }
}
