namespace Lucitex.Dds;

internal sealed class DdsOperationState
{
    private int _state; // 0 idle, 1 active, 2 disposed
    private bool _faulted;

    public void Enter()
    {
        var previous = Interlocked.CompareExchange(ref _state, 1, 0);
        ObjectDisposedException.ThrowIf(previous == 2, this);
        if (previous != 0) {
            throw new InvalidOperationException("Another operation is already active on this DDS instance.");
        }
        if (_faulted) {
            Exit();
            throw new InvalidOperationException("The DDS instance failed during I/O and must be disposed.");
        }
    }

    public void Fault() => _faulted = true;

    public void Exit() => Volatile.Write(ref _state, 0);

    public void Dispose()
    {
        var previous = Interlocked.CompareExchange(ref _state, 2, 0);
        if (previous == 1) {
            throw new InvalidOperationException("Await the active DDS operation before disposing it.");
        }
    }
}
