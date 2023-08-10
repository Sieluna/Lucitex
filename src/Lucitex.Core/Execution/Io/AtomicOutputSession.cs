namespace Lucitex.Core.Execution.Io;

public sealed class AtomicOutputSession : IDisposable
{
    private readonly FileStream _stream;
    private bool _committed;
    private bool _disposed;

    public AtomicOutputSession(string targetPath)
    {
        TargetPath = Path.GetFullPath(targetPath);
        TempPath = TargetPath + $".{Guid.NewGuid():N}.tmp";

        var directory = Path.GetDirectoryName(TargetPath);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        _stream = new FileStream(TempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    public string TargetPath { get; }

    public string TempPath { get; }

    public Stream Stream => _stream;

    public void Commit()
    {
        if (_committed) {
            throw new InvalidOperationException("The session has already been committed.");
        }

        _stream.Flush(flushToDisk: true);
        _stream.Dispose();

        if (File.Exists(TargetPath)) {
            File.Replace(TempPath, TargetPath, destinationBackupFileName: null);
        }
        else {
            File.Move(TempPath, TargetPath);
        }

        _committed = true;
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        if (!_committed) {
            _stream.Dispose();
            if (File.Exists(TempPath)) {
                File.Delete(TempPath);
            }
        }

        _disposed = true;
    }
}
