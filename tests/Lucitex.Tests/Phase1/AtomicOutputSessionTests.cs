using Lucitex.Core.Execution.Io;

namespace Lucitex.Tests.Phase1;

public class AtomicOutputSessionTests
{
    [Fact]
    public void Commit_MovesTempFileToTargetAndRemovesTemp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucitex-atomic-{Guid.NewGuid():N}.bin");

        try
        {
            string tempPath;
            using (var session = new AtomicOutputSession(path))
            {
                tempPath = session.TempPath;
                session.Stream.Write([1, 2, 3, 4]);
                session.Commit();
            }

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(tempPath));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Dispose_WithoutCommit_DeletesTempFileAndLeavesNoTarget()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucitex-atomic-{Guid.NewGuid():N}.bin");
        string tempPath;

        using (var session = new AtomicOutputSession(path))
        {
            tempPath = session.TempPath;
            session.Stream.Write([9, 9, 9]);
        }

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void Commit_ReplacesExistingTargetAtomically()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucitex-atomic-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, [0, 0, 0]);

        try
        {
            using (var session = new AtomicOutputSession(path))
            {
                session.Stream.Write([5, 6, 7]);
                session.Commit();
            }

            Assert.Equal(new byte[] { 5, 6, 7 }, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
