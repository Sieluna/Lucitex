namespace Lucitex.Core.Execution;

public sealed class ImageFormatException : Exception
{
    public string Format { get; }

    public long? Offset { get; }

    public string Code { get; }

    public string? Context { get; }

    public ImageFormatException(
        string format,
        string code,
        string message,
        long? offset = null,
        string? context = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Format = format;
        Code = code;
        Offset = offset;
        Context = context;
    }
}
