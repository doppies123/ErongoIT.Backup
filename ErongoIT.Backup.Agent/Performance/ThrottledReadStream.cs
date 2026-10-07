namespace ErongoIT.Backup.Agent.Performance;

/// <summary>
/// Read-only stream wrapper that slows reads down to the speed allowed
/// by an <see cref="IoThrottle"/>.
/// </summary>
public sealed class ThrottledReadStream : Stream
{
    private readonly Stream _inner;
    private readonly IoThrottle _throttle;

    public ThrottledReadStream(
        Stream inner,
        IoThrottle throttle)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        _throttle.Wait(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        _throttle.Wait(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        await _throttle.WaitAsync(read, cancellationToken);
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

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
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
}
