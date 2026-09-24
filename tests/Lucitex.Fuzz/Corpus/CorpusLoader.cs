namespace Lucitex.Fuzz;

internal static class CorpusLoader
{
    public static IEnumerable<SeedInput> Load(string? directory)
    {
        if (directory is null) {
            yield break;
        }
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)) {
            if (!ImageFormatExtensions.TryParse(Path.GetExtension(path).TrimStart('.'), out var format)) {
                continue;
            }
            var bytes = ReadInput(path);
            total += bytes.Length;
            if (total > 128 * 1024 * 1024) {
                throw new ArgumentException("External corpus exceeds the 128 MiB in-memory budget.");
            }
            yield return new SeedInput(Path.GetRelativePath(directory, path), format, bytes);
        }
    }

    public static byte[] ReadInput(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > FuzzLimits.MaxInputBytes) {
            throw new ArgumentException($"Input exceeds {FuzzLimits.MaxInputBytes} bytes: {path}");
        }
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
