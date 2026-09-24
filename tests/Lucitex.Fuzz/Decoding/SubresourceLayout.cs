using Lucitex.Core.Execution;
using Lucitex.Core.Representation;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Fuzz;

internal readonly record struct SubresourceRead(WorkRegion Region, int ByteCount);

internal static class SubresourceLayout
{
    public static IEnumerable<SubresourceRead> Enumerate(ImageAssetDescriptor descriptor, DecodeLimits limits)
    {
        long totalBytes = 0;
        var count = 0;
        for (var partIndex = 0; partIndex < descriptor.Parts.Count; partIndex++) {
            var part = descriptor.Parts[partIndex];
            foreach (var level in part.Topology.Levels) {
                var window = ImageBox.FromExclusive(part.Spatial.DataWindow.MinX, part.Spatial.DataWindow.MinY,
                    checked(part.Spatial.DataWindow.MinX + level.Extent.Width),
                    checked(part.Spatial.DataWindow.MinY + level.Extent.Height));
                var bytes = ByteCount(part, window, level.Extent.Depth);
                for (var array = 0; array < part.Topology.ArrayElementCount; array++) {
                    for (var face = 0; face < part.Topology.FaceCount; face++) {
                        totalBytes = checked(totalBytes + bytes);
                        if (++count > FuzzLimits.MaxSubresources || totalBytes > limits.MaxDecodedBytes || bytes > int.MaxValue) {
                            throw new ImageFormatException("harness", "LimitExceeded", "Subresource reads exceed the aggregate harness budget.");
                        }
                        yield return new SubresourceRead(new WorkRegion {
                            Subresource = new SubresourceId(partIndex, array, face, level.Key),
                            Region = window,
                        }, (int)bytes);
                    }
                }
            }
        }
    }

    private static long ByteCount(ImagePartDescriptor part, ImageBox window, long depth)
    {
        checked {
            if (part.Representation is EncodedElementRepresentation encoded) {
                var block = encoded.TexelExtentPerElement;
                var elements = DivideUp(window.Width, block.Width) * DivideUp(window.Height, block.Height) * DivideUp(depth, block.Depth);
                return DivideUp(elements * encoded.BitsPerElement, 8);
            }
            if (part.Representation is IndexedRepresentation indexed) {
                return DivideUp(window.Width * indexed.IndexType.Bits, 8) * window.Height * depth;
            }
            if (part.Representation is not PlainSampleRepresentation plain) {
                throw new NotSupportedException($"Harness layout: {part.Representation.GetType().Name}.");
            }

            long bytes = 0;
            foreach (var plane in plain.Planes) {
                var channels = plane.Channels.Select(path => part.Channels.Channels.Single(c => c.Name == path.FullName)).ToArray();
                if (channels.Length == 0) {
                    throw new InvalidOperationException("Descriptor contains an empty sample plane.");
                }
                var grid = channels[0].Sampling;
                if (grid.Step.Z != 1 || channels.Any(c => c.Sampling != grid)) {
                    throw new NotSupportedException("Harness layout: mixed grids in one plane or depth subsampling.");
                }
                var columns = grid.CountColumns(window.MinX, window.MaxXExclusive - 1);
                var rows = grid.CountRows(window.MinY, window.MaxYExclusive - 1);
                var rowBytes = plane.Layout == PlaneLayout.Planar
                    ? channels.Sum(c => DivideUp(columns * c.SampleType.Bits, 8))
                    : DivideUp(columns * channels.Sum(c => (long)c.SampleType.Bits), 8);
                bytes += rowBytes * rows * depth;
            }
            return bytes;
        }
    }

    private static long DivideUp(long value, long divisor) => checked((value + divisor - 1) / divisor);
}
