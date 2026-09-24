using Lucitex.Conversion;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;

namespace Lucitex.Example.Shared;

public class Format(IImageCodec codec)
{
    internal IImageCodec Codec { get; } = codec ?? throw new ArgumentNullException(nameof(codec));

    public string Id => Codec.FormatId;
    public string Extension => Codec.Extensions[0];
    public IReadOnlyList<string> Extensions => Codec.Extensions;
    public virtual IReadOnlyList<Parameter> Parameters => [];

    public Conversion From(Format source, Stream input, ConversionPolicy policy = ConversionPolicy.Preview) => new(source, input, this, policy);

    public virtual Format Parse(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (id, _) in values) {
            throw UnknownParameter(id);
        }
        return this;
    }

    internal virtual IImageWriter OpenWriter(Stream stream, ImageAssetDescriptor descriptor) => Codec.CreateWriter(stream, descriptor);

    protected ArgumentException UnknownParameter(string id) =>
        new($"Unknown {Id} parameter '{id}'. Available parameters: {string.Join(", ", Parameters.Select(parameter => parameter.Id))}.");
}

public sealed class Format<T> : Format where T : notnull
{
    private readonly T _options;
    private readonly Field<T>[] _fields;
    private readonly Dictionary<string, Field<T>> _byId;
    private readonly Func<Stream, ImageAssetDescriptor, T, IImageWriter> _write;

    internal Format(IImageCodec codec, T options, Func<Stream, ImageAssetDescriptor, T, IImageWriter> write, params Field<T>[] fields) : base(codec)
    {
        _options = options;
        _fields = fields;
        _byId = fields.ToDictionary(field => field.Id, StringComparer.OrdinalIgnoreCase);
        _write = write;
    }

    private Format(Format<T> source, T options) : base(source.Codec)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _fields = source._fields;
        _byId = source._byId;
        _write = source._write;
    }

    public override IReadOnlyList<Parameter> Parameters => Array.AsReadOnly(_fields.Select(parameter => parameter.Describe(_options)).ToArray());

    public Format<T> With(Func<T, T> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return new(this, configure(_options));
    }

    public override Format<T> Parse(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var options = _options;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, value) in values) {
            if (!seen.Add(id)) {
                throw new ArgumentException($"Duplicate parameter '{id}'.");
            }
            if (!_byId.TryGetValue(id, out var field)) {
                throw UnknownParameter(id);
            }
            options = field.Apply(options, value);
        }
        return seen.Count == 0 ? this : new(this, options);
    }

    internal override IImageWriter OpenWriter(Stream stream, ImageAssetDescriptor descriptor) => _write(stream, descriptor, _options);
}
