namespace Lucitex.Core.Execution.Codecs;

public sealed class CodecRegistry
{
    private readonly Dictionary<string, IImageCodec> _byFormatId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IImageCodec> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IImageCodec codec)
    {
        if (!_byFormatId.TryAdd(codec.FormatId, codec))
        {
            throw new InvalidOperationException($"A codec for format '{codec.FormatId}' is already registered.");
        }

        foreach (var extension in codec.Extensions)
        {
            _byExtension.TryAdd(extension, codec);
        }
    }

    public IImageCodec ResolveByFormatId(string formatId)
    {
        if (_byFormatId.TryGetValue(formatId, out var codec))
        {
            return codec;
        }

        throw new KeyNotFoundException($"No codec registered for format '{formatId}'.");
    }

    public IImageCodec Resolve(ReadOnlySpan<byte> header, string? extensionHint = null)
    {
        var probes = new List<(IImageCodec Codec, FormatProbeResult Result)>();

        foreach (var codec in _byFormatId.Values)
        {
            var result = codec.Probe(header);
            if (result.Confidence != ProbeConfidence.None)
            {
                probes.Add((codec, result));
            }
        }

        if (probes.Count == 0)
        {
            if (extensionHint != null && _byExtension.TryGetValue(extensionHint, out var byExtension))
            {
                return byExtension;
            }

            throw new InvalidOperationException("Could not determine image format from header or extension.");
        }

        var maxConfidence = probes.Max(probe => probe.Result.Confidence);
        var winners = probes.Where(probe => probe.Result.Confidence == maxConfidence).ToList();

        if (winners.Count == 1)
        {
            return winners[0].Codec;
        }

        if (extensionHint != null)
        {
            var byExtensionAmongWinners = winners.Find(w => _byExtension.TryGetValue(extensionHint, out var extCodec) && ReferenceEquals(extCodec, w.Codec));
            if (byExtensionAmongWinners.Codec != null)
            {
                return byExtensionAmongWinners.Codec;
            }
        }

        throw new AmbiguousFormatException(winners.Select(w => w.Result).ToList());
    }
}
