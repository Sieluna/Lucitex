using Lucitex.Conversion.Kernels;
using Lucitex.Core.Spatial;

namespace Lucitex.Tests.Conversion.Kernels;

public class OrientationKernelTests
{
    [Fact]
    public void ApplyToIdentity_FlipX_MirrorsEachRowHorizontally()
    {
        // A B     B A
        // C D  -> D C
        float[] source = [1, 2, 3, 4];

        var destination = new float[4];
        var (width, height) = OrientationKernel.ApplyToIdentity(source, 2, 2, LogicalOrientation.FlipX, destination);

        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal([2, 1, 4, 3], destination);
    }

    [Fact]
    public void ApplyToIdentity_FlipY_ReversesRowOrder()
    {
        // A B     C D
        // C D  -> A B
        float[] source = [1, 2, 3, 4];

        var destination = new float[4];
        var (width, height) = OrientationKernel.ApplyToIdentity(source, 2, 2, LogicalOrientation.FlipY, destination);

        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal([3, 4, 1, 2], destination);
    }

    [Fact]
    public void ApplyToIdentity_Rotate90_RotatesClockwiseAndSwapsExtent()
    {
        // A B     C A
        // C D  -> D B
        float[] source = [1, 2, 3, 4];

        var destination = new float[4];
        var (width, height) = OrientationKernel.ApplyToIdentity(source, 2, 2, LogicalOrientation.Rotate90, destination);

        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal([3, 1, 4, 2], destination);
    }

    [Fact]
    public void ApplyToIdentity_Rotate90_NonSquare_SwapsWidthAndHeight()
    {
        // A B C
        float[] source = [1, 2, 3];

        var destination = new float[3];
        var (width, height) = OrientationKernel.ApplyToIdentity(source, 3, 1, LogicalOrientation.Rotate90, destination);

        Assert.Equal(1, width);
        Assert.Equal(3, height);
    }

    [Fact]
    public void ApplyToIdentity_Rotate180_ReversesBothAxes()
    {
        // A B     D C
        // C D  -> B A
        float[] source = [1, 2, 3, 4];

        var destination = new float[4];
        OrientationKernel.ApplyToIdentity(source, 2, 2, LogicalOrientation.Rotate180, destination);

        Assert.Equal([4, 3, 2, 1], destination);
    }

    [Fact]
    public void ApplyToIdentity_Identity_IsPassthrough()
    {
        float[] source = [1, 2, 3, 4, 5, 6];

        var destination = new float[6];
        var (width, height) = OrientationKernel.ApplyToIdentity(source, 3, 2, LogicalOrientation.Identity, destination);

        Assert.Equal(3, width);
        Assert.Equal(2, height);
        Assert.Equal(source, destination);
    }
}
