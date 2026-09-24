namespace Lucitex.Webp.Lossless;

internal static class Vp8LDecoder
{
    private readonly record struct Transform(int Type, int Width, int Bits, WebpBuffer<uint>? Data);

    private static ReadOnlySpan<sbyte> DistanceOffsets => [
        0,1, 1,0, 1,1, -1,1, 0,2, 2,0, 1,2, -1,2, 2,1, -2,1, 2,2, -2,2,
        0,3, 3,0, 1,3, -1,3, 3,1, -3,1, 2,3, -2,3, 3,2, -3,2, 0,4, 4,0,
        1,4, -1,4, 4,1, -4,1, 3,3, -3,3, 2,4, -2,4, 4,2, -4,2, 0,5, 3,4,
        -3,4, 4,3, -4,3, 5,0, 1,5, -1,5, 5,1, -5,1, 2,5, -2,5, 5,2, -5,2,
        4,4, -4,4, 3,5, -3,5, 5,3, -5,3, 0,6, 6,0, 1,6, -1,6, 6,1, -6,1,
        2,6, -2,6, 6,2, -6,2, 4,5, -4,5, 5,4, -5,4, 3,6, -3,6, 6,3, -6,3,
        0,7, 7,0, 1,7, -1,7, 5,5, -5,5, 7,1, -7,1, 4,6, -4,6, 6,4, -6,4,
        2,7, -2,7, 7,2, -7,2, 3,7, -3,7, 7,3, -7,3, 5,6, -5,6, 6,5, -6,5,
        8,0, 4,7, -4,7, 7,4, -7,4, 8,1, 8,2, 6,6, -6,6, 8,3, 5,7, -5,7,
        7,5, -7,5, 8,4, 6,7, -6,7, 7,6, -7,6, 8,5, 7,7, -7,7, 8,6, 8,7,
    ];

    public static WebpBuffer<uint> Decode(ReadOnlySpan<byte> data, int width, int height, WebpMemory memory)
    {
        var reader = new Vp8LBitReader(data);
        var transforms = new Transform[4];
        var count = 0;
        var seen = 0;
        var codedWidth = width;
        WebpBuffer<uint>? output = null;
        try {
            while (reader.Read(1) != 0) {
                var type = reader.Read(2);
                if ((seen & (1 << type)) != 0) {
                    throw new InvalidDataException("Duplicate VP8L transform.");
                }
                seen |= 1 << type;
                var bits = 0;
                WebpBuffer<uint>? transformData = null;
                var transformWidth = codedWidth;
                if (type is 0 or 1) {
                    bits = reader.Read(3) + 2;
                    transformData = DecodeSubimage(ref reader, Vp8LTransforms.Subsample(codedWidth, bits), Vp8LTransforms.Subsample(height, bits), memory);
                }
                else if (type == 3) {
                    var colors = reader.Read(8) + 1;
                    transformData = DecodeSubimage(ref reader, colors, 1, memory);
                    var palette = transformData.Span;
                    for (var i = 1; i < palette.Length; i++) {
                        palette[i] = Vp8LTransforms.Add(palette[i], palette[i - 1]);
                    }
                    bits = colors <= 2 ? 3 : colors <= 4 ? 2 : colors <= 16 ? 1 : 0;
                    codedWidth = Vp8LTransforms.Subsample(codedWidth, bits);
                }
                transforms[count++] = new Transform(type, transformWidth, bits, transformData);
            }
            output = memory.Rent<uint>(checked(width * height));
            DecodeEntropy(ref reader, output.Span[..(codedWidth * height)], codedWidth, height, true, memory);
            for (var i = count - 1; i >= 0; i--) {
                var transform = transforms[i];
                var pixels = output.Span[..(transform.Width * height)];
                switch (transform.Type) {
                    case 0:
                        Vp8LTransforms.InversePredictor(pixels, transform.Width, height, transform.Data!.Span, transform.Bits);
                        break;
                    case 1:
                        Vp8LTransforms.InverseColor(pixels, transform.Width, height, transform.Data!.Span, transform.Bits);
                        break;
                    case 2:
                        Vp8LTransforms.AddGreen(pixels);
                        break;
                    case 3:
                        Vp8LTransforms.ExpandPalette(pixels, transform.Width, height, transform.Data!.Span, transform.Bits);
                        break;
                }
            }
            return output;
        }
        catch {
            output?.Dispose();
            throw;
        }
        finally {
            foreach (var transform in transforms) {
                transform.Data?.Dispose();
            }
        }
    }

    private static WebpBuffer<uint> DecodeSubimage(ref Vp8LBitReader reader, int width, int height, WebpMemory memory)
    {
        var output = memory.Rent<uint>(checked(width * height));
        try {
            DecodeEntropy(ref reader, output.Span, width, height, false, memory);
            return output;
        }
        catch {
            output.Dispose();
            throw;
        }
    }

    private static void DecodeEntropy(ref Vp8LBitReader reader, Span<uint> output, int width, int height, bool mainImage, WebpMemory memory)
    {
        var hasCache = reader.Read(1) != 0;
        var cacheBits = hasCache ? reader.Read(4) : 0;
        if (hasCache && (cacheBits < 1 || cacheBits > 11)) {
            throw new InvalidDataException("Invalid VP8L color cache size.");
        }
        using var cacheBuffer = cacheBits == 0 ? null : memory.Rent<uint>(1 << cacheBits);
        var cache = cacheBuffer is null ? Span<uint>.Empty : cacheBuffer.Span;
        cache.Clear();
        WebpBuffer<uint>? entropyImage = null;
        Vp8LHuffman?[]? trees = null;
        long groupBytes = 0;
        try {
            var groupCount = 1;
            var groupBits = 0;
            var groupWidth = 0;
            if (mainImage && reader.Read(1) != 0) {
                groupBits = reader.Read(3) + 2;
                groupWidth = Vp8LTransforms.Subsample(width, groupBits);
                entropyImage = DecodeSubimage(ref reader, groupWidth, Vp8LTransforms.Subsample(height, groupBits), memory);
                foreach (var pixel in entropyImage.Span) {
                    groupCount = Math.Max(groupCount, (int)((pixel >> 8) & 65535) + 1);
                }
            }
            memory.Reserve((long)groupCount * 5 * 96);
            groupBytes = (long)groupCount * 5 * 96;
            trees = new Vp8LHuffman[groupCount * 5];
            for (var group = 0; group < groupCount; group++) {
                for (var channel = 0; channel < 5; channel++) {
                    var alphabetSize = channel == 0 ? 280 + cache.Length : channel == 4 ? 40 : 256;
                    trees[(group * 5) + channel] = Vp8LHuffman.Read(ref reader, alphabetSize, memory);
                }
            }
            var position = 0;
            var groupMap = entropyImage is null ? ReadOnlySpan<uint>.Empty : entropyImage.Span;
            while (position < output.Length) {
                var group = groupMap.IsEmpty ? 0 : (int)((groupMap[((position / width >> groupBits) * groupWidth) + ((position % width) >> groupBits)] >> 8) & 65535);
                var tree = group * 5;
                var symbol = trees[tree]!.Decode(ref reader);
                var start = position;
                if (symbol < 256) {
                    var red = trees[tree + 1]!.Decode(ref reader);
                    var blue = trees[tree + 2]!.Decode(ref reader);
                    var alpha = trees[tree + 3]!.Decode(ref reader);
                    output[position++] = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)symbol << 8) | (uint)blue;
                }
                else if (symbol < 280) {
                    var length = reader.ReadPrefix(symbol - 256);
                    var distanceCode = reader.ReadPrefix(trees[tree + 4]!.Decode(ref reader));
                    var distance = distanceCode > 120 ? distanceCode - 120 : Math.Max(1, DistanceOffsets[(distanceCode - 1) * 2] + (DistanceOffsets[((distanceCode - 1) * 2) + 1] * width));
                    if (distance > position || length > output.Length - position) {
                        throw new InvalidDataException("VP8L backward reference exceeds the image bounds.");
                    }
                    var target = output.Slice(position, length);
                    if (distance == 1) {
                        target.Fill(output[position - 1]);
                    }
                    else if (distance >= length) {
                        output.Slice(position - distance, length).CopyTo(target);
                    }
                    else {
                        for (var i = 0; i < length; i++) {
                            output[position + i] = output[position + i - distance];
                        }
                    }
                    position += length;
                }
                else {
                    output[position++] = cache[symbol - 280];
                }
                if (cacheBits != 0) {
                    for (var i = start; i < position; i++) {
                        var pixel = output[i];
                        cache[(int)(unchecked(pixel * 0x1e35a7bd) >> (32 - cacheBits))] = pixel;
                    }
                }
            }
        }
        finally {
            if (trees is not null) {
                foreach (var tree in trees) {
                    tree?.Dispose();
                }
            }
            if (groupBytes != 0) {
                memory.Release(groupBytes);
            }
            entropyImage?.Dispose();
        }
    }
}
