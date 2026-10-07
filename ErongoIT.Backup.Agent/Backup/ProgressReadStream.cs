namespace ErongoIT.Backup.Agent.Backup;

/// <summary>
/// Wraps a stream and reports how many bytes have been read so far.
/// Used to show live upload progress for large files.
/// </summary>
public sealed class ProgressReadStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long> _onProgress;
    private long _bytesRead;
    private long _lastReported;
    private DateTime _lastReportUtc = DateTime.MinValue;

    public ProgressReadStream(
        Stream inner,
        Action<long> onProgress)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onProgress = onProgress ?? throw new ArgumentNullException(nameof(onProgress));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set
        {
            _inner.Position = value;
            _bytesRead = value;
            Report(force: true);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Advance(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        Advance(read);
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return ReadAsync(
            buffer.AsMemory(offset, count),
            cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = _inner.Seek(offset, origin);
        _bytesRead = position;
        Report(force: true);
        return position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await base.DisposeAsync();
    }

    private void Advance(int read)
    {
        if (read <= 0)
        {
            Report(force: true);
            return;
        }

        _bytesRead += read;
        Report(force: false);
    }

    private void Report(bool force)
    {
        // Throttle to ~4 updates per second.
        var now = DateTime.UtcNow;

        if (!force &&
            (now - _lastReportUtc).TotalMilliseconds < 250)
        {
            return;
        }

        if (!force && _bytesRead == _lastReported)
            return;

        _lastReportUtc = now;
        _lastReported = _bytesRead;
        _onProgress(_bytesRead);
    }
}
