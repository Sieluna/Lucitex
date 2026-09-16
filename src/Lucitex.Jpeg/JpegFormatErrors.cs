using Lucitex.Core.Execution;

namespace Lucitex.Jpeg;

internal static class JpegFormatErrors
{
    public static bool IsMalformed(Exception exception) => exception is
        EndOfStreamException or InvalidDataException or OverflowException or ArgumentException or IndexOutOfRangeException;

    public static ImageFormatException Wrap(Exception exception, Stream stream) => exception as ImageFormatException ?? new ImageFormatException(
        "jpeg",
        "MalformedData",
        "The JPEG stream contains malformed or truncated data.",
        stream.CanSeek ? stream.Position : null,
        innerException: exception);
}
