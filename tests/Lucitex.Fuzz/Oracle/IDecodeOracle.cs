namespace Lucitex.Fuzz;

internal interface IDecodeOracle : IDisposable
{
    bool Supports(ImageFormat format);

    DecodeOutcome Decode(ImageFormat format, byte[] data);
}
