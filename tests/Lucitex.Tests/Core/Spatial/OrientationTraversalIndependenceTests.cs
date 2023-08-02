using Lucitex.Core.Spatial;

namespace Lucitex.Tests.Core.Spatial;

public class OrientationTraversalIndependenceTests
{
    public static TheoryData<LogicalOrientation, StorageTraversal> AllCombinations()
    {
        LogicalOrientation[] orientations =
        [
            LogicalOrientation.Identity,
            LogicalOrientation.FlipX,
            LogicalOrientation.FlipY,
            LogicalOrientation.Rotate90,
            LogicalOrientation.Rotate180,
            LogicalOrientation.Rotate270,
            LogicalOrientation.Transpose,
            LogicalOrientation.Transverse,
        ];

        StorageTraversal[] traversals =
        [
            StorageTraversal.IncreasingY,
            StorageTraversal.DecreasingY,
            StorageTraversal.Random,
            StorageTraversal.Sequential,
            StorageTraversal.CodecDefined,
        ];

        var data = new TheoryData<LogicalOrientation, StorageTraversal>();
        foreach (var orientation in orientations)
        {
            foreach (var traversal in traversals)
            {
                data.Add(orientation, traversal);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void SpatialDomain_AcceptsAnyOrientationTraversalCombination(
        LogicalOrientation orientation,
        StorageTraversal traversal)
    {
        var window = ImageBox.FromOrigin(4, 4);

        var domain = new SpatialDomain
        {
            DataWindow = window,
            DisplayWindow = window,
            Orientation = orientation,
            Traversal = traversal,
        };

        Assert.Equal(orientation, domain.Orientation);
        Assert.Equal(traversal, domain.Traversal);
    }

    [Fact]
    public void ChangingTraversal_DoesNotAlterOrientation()
    {
        var window = ImageBox.FromOrigin(4, 4);
        var domain = new SpatialDomain
        {
            DataWindow = window,
            DisplayWindow = window,
            Orientation = LogicalOrientation.Rotate90,
            Traversal = StorageTraversal.IncreasingY,
        };

        var reordered = domain with { Traversal = StorageTraversal.DecreasingY };

        Assert.Equal(LogicalOrientation.Rotate90, reordered.Orientation);
        Assert.Equal(StorageTraversal.DecreasingY, reordered.Traversal);
    }
}
