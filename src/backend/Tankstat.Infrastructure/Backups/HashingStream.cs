using System.Security.Cryptography;

namespace Tankstat.Infrastructure.Backups;

/// <summary>Passes writes through to another stream while counting the bytes and hashing them (SHA-256), so a file's size and checksum are known once it is written.</summary>
internal sealed class HashingStream(Stream inner, bool leaveOpen = false) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _closed;

    public long Written { get; private set; }

    /// <summary>The hash of everything written, in lower-case hex; final once the stream is disposed.</summary>
    public string Sha256Hex => Convert.ToHexStringLower(_hash.GetCurrentHash());

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        _hash.AppendData(buffer);
        Written += buffer.Length;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken);
        _hash.AppendData(buffer.Span);
        Written += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            if (!leaveOpen) inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_closed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
