using System.Text;
using Lucitex.Ktx2.Format;

namespace Lucitex.Tests.Ktx2.Format;

public class Ktx2KeyValueIoTests
{
    [Fact]
    public void WriteThenRead_SingleEntry_PreservesKeyAndValue()
    {
        List<Ktx2KeyValueEntry> entries = [new Ktx2KeyValueEntry("KTXwriter", Encoding.UTF8.GetBytes("Lucitex"))];

        var written = Ktx2KeyValueIo.Write(entries);
        var roundTripped = Ktx2KeyValueIo.Read(written);

        var entry = Assert.Single(roundTripped);
        Assert.Equal("KTXwriter", entry.Key);
        Assert.Equal("Lucitex"u8.ToArray(), entry.Value);
    }

    [Fact]
    public void WriteThenRead_MultipleEntries_PreservesOrderAndPadding()
    {
        List<Ktx2KeyValueEntry> entries = [
            new Ktx2KeyValueEntry("a", [1]),
            new Ktx2KeyValueEntry("bb", [2, 3, 4]),
            new Ktx2KeyValueEntry("ccc", [5, 6, 7, 8, 9]),
        ];

        var written = Ktx2KeyValueIo.Write(entries);
        var roundTripped = Ktx2KeyValueIo.Read(written);

        Assert.Equal(3, roundTripped.Count);
        Assert.Equal("a", roundTripped[0].Key);
        Assert.Equal("bb", roundTripped[1].Key);
        Assert.Equal("ccc", roundTripped[2].Key);
        Assert.Equal([2, 3, 4], roundTripped[1].Value);
    }

    [Fact]
    public void Read_EmptyData_ReturnsNoEntries()
    {
        var roundTripped = Ktx2KeyValueIo.Read([]);

        Assert.Empty(roundTripped);
    }

    [Fact]
    public void Read_TruncatedLength_ThrowsEndOfStream()
    {
        byte[] data = [1, 0];

        Assert.Throws<EndOfStreamException>(() => Ktx2KeyValueIo.Read(data));
    }

    [Fact]
    public void Read_LengthExceedsBuffer_ThrowsInvalidData()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0x7F];

        Assert.Throws<InvalidDataException>(() => Ktx2KeyValueIo.Read(data));
    }

    [Fact]
    public void Read_MissingKeyTerminator_ThrowsInvalidData()
    {
        byte[] data = [3, 0, 0, 0, (byte)'a', (byte)'b', (byte)'c'];

        Assert.Throws<InvalidDataException>(() => Ktx2KeyValueIo.Read(data));
    }
}
