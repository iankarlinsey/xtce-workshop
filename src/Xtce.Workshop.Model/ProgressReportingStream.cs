namespace Xtce.Workshop.Model;

/// <summary>
/// Read-only decorator that reports consumption progress (0..1 of Length) and observes a
/// cancellation token on every read — which gives the forward-only reader and the schema
/// validator real progress and real cancellation with zero changes to their own code.
/// </summary>
public sealed class ProgressReportingStream : Stream
{
    private readonly Stream _inner;
    private readonly IProgress<double>? _progress;
    private readonly CancellationToken _cancellationToken;

    public ProgressReportingStream(Stream inner, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        _inner = inner;
        _progress = progress;
        _cancellationToken = cancellationToken;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var read = _inner.Read(buffer, offset, count);
        if (_inner.Length > 0)
        {
            _progress?.Report((double)_inner.Position / _inner.Length);
        }
        return read;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
