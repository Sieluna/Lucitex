namespace Lucitex.Core.Execution.Codecs;

public interface IImageWriter
{
    WriterExecutionContract Contract { get; }

    void Write(WorkRegion region, ReadOnlySpan<byte> data);

    void Finish();
}
