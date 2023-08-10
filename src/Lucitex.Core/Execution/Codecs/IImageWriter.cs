namespace Lucitex.Core.Execution.Codecs;

public interface IImageWriter
{
    public WriterExecutionContract Contract { get; }

    public void Write(WorkRegion region, ReadOnlySpan<byte> data);

    public void Finish();
}
