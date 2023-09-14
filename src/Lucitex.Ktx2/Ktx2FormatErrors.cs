using Lucitex.Core.Execution;

namespace Lucitex.Ktx2;

internal static class Ktx2FormatErrors
{
    public static bool IsMalformed(Exception exception) => exception is
        EndOfStreamException or InvalidDataException or OverflowException or IndexOutOfRangeException;

    public static ImageFormatException Wrap(Exception exception, Stream stream) => exception as ImageFormatException ?? new ImageFormatException(
        "ktx2",
        "MalformedData",
        "The KTX2 stream contains malformed or truncated data.",
        stream.CanSeek ? stream.Position : null,
        innerException: exception);
}
