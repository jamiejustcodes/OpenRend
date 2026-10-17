using OpenRend.Core;

namespace OpenRend.Media;

internal static partial class VideoEffectProcessor
{
    private static byte[] ApplyLut(byte[] source, CubeLut lut, double strength, bool tetrahedral)
    {
        var size = lut.Size;
        var samples = lut.Samples;
        return Color(source, (red, green, blue) =>
        {
            var x = Coordinate(red, 0); var y = Coordinate(green, 1); var z = Coordinate(blue, 2);
            var ix = Math.Min((int)x, size - 2); var iy = Math.Min((int)y, size - 2); var iz = Math.Min((int)z, size - 2);
            var fx = x - ix; var fy = y - iy; var fz = z - iz;
            // Cube rows advance red first, then green, then blue. Buffers remain BGRA.
            var origin = (ix + size * (iy + size * iz)) * 3;
            var sx = 3; var sy = size * 3; var sz = size * size * 3;
            double Sample(int channel)
            {
                if (!tetrahedral)
                {
                    double Plane(int offset) =>
                        (1 - fy) * ((1 - fx) * samples[origin + offset + channel] + fx * samples[origin + offset + sx + channel]) +
                        fy * ((1 - fx) * samples[origin + offset + sy + channel] + fx * samples[origin + offset + sy + sx + channel]);
                    return (1 - fz) * Plane(0) + fz * Plane(sz);
                }
                var first = sx; var second = sy; var a = fx; var b = fy; var c = fz;
                if (fx >= fy)
                {
                    if (fy >= fz) { }
                    else if (fx >= fz) { second = sz; b = fz; c = fy; }
                    else { first = sz; second = sx; a = fz; b = fx; c = fy; }
                }
                else
                {
                    if (fx >= fz) { first = sy; second = sx; a = fy; b = fx; }
                    else if (fy >= fz) { first = sy; second = sz; a = fy; b = fz; c = fx; }
                    else { first = sz; second = sy; a = fz; b = fy; c = fx; }
                }
                // Four barycentric weights select one of the six tetrahedra sharing the diagonal.
                return (1 - a) * samples[origin + channel] + (a - b) * samples[origin + first + channel] +
                    (b - c) * samples[origin + first + second + channel] + c * samples[origin + sx + sy + sz + channel];
            }
            return ((1 - strength) * red + strength * Sample(0),
                (1 - strength) * green + strength * Sample(1), (1 - strength) * blue + strength * Sample(2));
        });

        double Coordinate(double value, int channel) =>
            Math.Clamp((value - lut.DomainMinimum[channel]) / (lut.DomainMaximum[channel] - lut.DomainMinimum[channel]), 0, 1) * (size - 1);
    }
}
