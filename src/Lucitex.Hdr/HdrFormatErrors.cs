using Lucitex.Core.Execution;

namespace Lucitex.Hdr;

internal static class HdrFormatErrors
{
    public static bool IsMalformed(Exception exception) => exception is
        EndOfStreamException or InvalidDataException or OverflowException or FormatException;

    public static ImageFormatException Wrap(Exception exception, Stream stream)
    {
        long? offset = stream.CanSeek ? stream.Position : null;
        return new ImageFormatException("hdr", "MalformedData", exception.Message, offset, innerException: exception);
    }
}
