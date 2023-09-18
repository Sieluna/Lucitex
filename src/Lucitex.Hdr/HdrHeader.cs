using System.Globalization;
using System.Text;
using Lucitex.Core.Execution;
using Lucitex.Core.Spatial;

namespace Lucitex.Hdr;

internal readonly record struct HdrHeader(int Width, int Height, LogicalOrientation Orientation);

internal static class HdrHeaderIo
{
    public static HdrHeader Read(Stream stream, DecodeLimits limits)
    {
        long bytesRead = 0;
        var signature = ReadLine(stream, limits, ref bytesRead);
        if (signature is not "#?RADIANCE" and not "#?RGBE") {
            throw new ImageFormatException("hdr", "BadMagic", "Stream does not start with a Radiance HDR signature.");
        }

        var foundFormat = false;
        while (true) {
            var line = ReadLine(stream, limits, ref bytesRead);
            if (line.Length == 0) {
                break;
            }

            if (line.StartsWith("FORMAT=", StringComparison.Ordinal)) {
                if (line != "FORMAT=32-bit_rle_rgbe") {
                    throw new ImageFormatException("hdr", "Unsupported.Hdr.Format", $"HDR pixel format '{line[7..]}' is not supported.");
                }

                foundFormat = true;
            }
        }

        if (!foundFormat) {
            throw new ImageFormatException("hdr", "MissingFormat", "HDR header does not declare FORMAT=32-bit_rle_rgbe.");
        }

        var resolution = ReadLine(stream, limits, ref bytesRead).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (resolution.Length != 4 || resolution[0] is not ("-Y" or "+Y") || resolution[2] is not ("+X" or "-X") ||
            !int.TryParse(resolution[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height) ||
            !int.TryParse(resolution[3], NumberStyles.None, CultureInfo.InvariantCulture, out var width) ||
            width <= 0 || height <= 0) {
            throw new ImageFormatException("hdr", "BadResolution", "HDR resolution must use the form '-Y height +X width'.");
        }

        var signX = resolution[2] == "+X" ? 1 : -1;
        var signY = resolution[0] == "-Y" ? 1 : -1;
        var orientation = new LogicalOrientation(AxisPermutation.Identity, new AxisSign(signX, signY, 1));
        return new HdrHeader(width, height, orientation);
    }

    public static void Write(Stream stream, int width, int height, LogicalOrientation orientation)
    {
        if (orientation.Permutation != AxisPermutation.Identity || orientation.Sign.Z != 1) {
            throw new NotSupportedException("HDR writing supports X/Y axis flips but not axis permutation.");
        }

        var y = orientation.Sign.Y >= 0 ? "-Y" : "+Y";
        var x = orientation.Sign.X >= 0 ? "+X" : "-X";
        var header = $"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n{y} {height.ToString(CultureInfo.InvariantCulture)} {x} {width.ToString(CultureInfo.InvariantCulture)}\n";
        stream.Write(Encoding.ASCII.GetBytes(header));
    }

    private static string ReadLine(Stream stream, DecodeLimits limits, ref long bytesRead)
    {
        using var line = new MemoryStream();
        while (true) {
            var value = stream.ReadByte();
            if (value < 0) {
                throw new EndOfStreamException("Unexpected end of HDR header.");
            }

            bytesRead++;
            if (bytesRead > limits.MaxMetadataBytes) {
                throw new ImageFormatException("hdr", "LimitExceeded", "HDR header exceeds MaxMetadataBytes.");
            }

            if (value == '\n') {
                break;
            }

            if (value != '\r') {
                line.WriteByte((byte)value);
            }
        }

        return Encoding.ASCII.GetString(line.GetBuffer(), 0, checked((int)line.Length));
    }
}
