using System.Security.Cryptography;
using System.Text;
using Tankstat.Domain;
using Tankstat.Domain.Backups;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// age v1 files for one X25519 recipient, streamed: <see cref="Encrypt"/> gives a stream to write the plaintext to (the header goes out at
/// once, the payload in 64 KiB chunks, the final one when the stream is disposed), <see cref="Decrypt"/> a stream to read it back from (the
/// header and its MAC are checked first, every chunk as it is read). The format is the real tool's, so <c>age -d -i identity</c> opens what
/// is written here, and a file age wrote is read here. age's own passphrase mode (an scrypt stanza) is refused: a backup is always
/// encrypted for a recipient. Failures are <see cref="DomainException"/>s: <c>backup.wrongKey</c>, <c>backup.passphraseEncrypted</c> and
/// <c>backup.corrupt</c>.
/// </summary>
internal static class AgeStream
{
    private static readonly byte[] HeaderInfo = Encoding.ASCII.GetBytes(AgeFormat.HeaderInfo);
    private static readonly byte[] PayloadInfo = Encoding.ASCII.GetBytes(AgeFormat.PayloadInfo);

    public static Stream Encrypt(Stream output, ReadOnlySpan<byte> recipient, bool leaveOpen = false) => new Writer(output, recipient, leaveOpen);

    public static Stream Decrypt(Stream input, ReadOnlySpan<byte> identity, bool leaveOpen = false) => new Reader(input, identity, leaveOpen);

    public static DomainException Corrupt() => new("backup.corrupt", "The file is damaged or is not an age file.");

    private static byte[] HeaderMac(byte[] fileKey, byte[] macInput) =>
        HMACSHA256.HashData(HKDF.DeriveKey(HashAlgorithmName.SHA256, fileKey, AgeFormat.MacLength, [], HeaderInfo), macInput);

    private static ChaCha20Poly1305 PayloadCipher(byte[] fileKey, byte[] nonce) =>
        new(HKDF.DeriveKey(HashAlgorithmName.SHA256, fileKey, 32, nonce, PayloadInfo));

    private sealed class Writer : Stream
    {
        private readonly Stream _output;
        private readonly bool _leaveOpen;
        private readonly ChaCha20Poly1305 _cipher;
        private readonly byte[] _chunk = new byte[AgeFormat.ChunkSize];
        private readonly byte[] _sealed = new byte[AgeFormat.ChunkSize + AgeFormat.TagLength];
        private int _filled;
        private long _counter;
        private bool _closed;

        public Writer(Stream output, ReadOnlySpan<byte> recipient, bool leaveOpen)
        {
            _output = output;
            _leaveOpen = leaveOpen;
            var fileKey = RandomNumberGenerator.GetBytes(AgeFormat.FileKeyLength);
            var stanzas = new[] { AgeX25519.Wrap(recipient, fileKey) };
            _output.Write(AgeFormat.Header(stanzas, HeaderMac(fileKey, AgeFormat.MacInput(stanzas))));
            var nonce = RandomNumberGenerator.GetBytes(AgeFormat.PayloadNonceLength);
            _output.Write(nonce);
            _cipher = PayloadCipher(fileKey, nonce);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            while (buffer.Length > 0)
            {
                // A full chunk waits until the next byte comes: only then is it known not to be the final one.
                if (_filled == _chunk.Length) Seal(last: false);
                var take = Math.Min(buffer.Length, _chunk.Length - _filled);
                buffer[..take].CopyTo(_chunk.AsSpan(_filled));
                _filled += take;
                buffer = buffer[take..];
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        // The chunks are sealed in memory; only full ones reach the output, through its own Write.
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void Seal(bool last)
        {
            _cipher.Encrypt(AgeFormat.ChunkNonce(_counter++, last), _chunk.AsSpan(0, _filled), _sealed.AsSpan(0, _filled), _sealed.AsSpan(_filled, AgeFormat.TagLength));
            _output.Write(_sealed, 0, _filled + AgeFormat.TagLength);
            _filled = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                Seal(last: true); // the final chunk, empty when nothing at all was written
                _cipher.Dispose();
                _output.Flush();
                if (!_leaveOpen) _output.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() => _output.Flush(); // what is sealed so far; an unfinished chunk waits for the rest or the end
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_closed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class Reader : Stream
    {
        private readonly Stream _input;
        private readonly bool _leaveOpen;
        private readonly ChaCha20Poly1305 _cipher;
        private readonly byte[] _next = new byte[AgeFormat.ChunkSize + AgeFormat.TagLength];
        private int _nextLength;
        private readonly byte[] _plain = new byte[AgeFormat.ChunkSize];
        private int _plainLength;
        private int _plainRead;
        private long _counter;
        private bool _finished;
        private bool _closed;

        public Reader(Stream input, ReadOnlySpan<byte> identity, bool leaveOpen)
        {
            _input = input;
            _leaveOpen = leaveOpen;
            IReadOnlyList<AgeStanza> stanzas;
            byte[] mac;
            try
            {
                (stanzas, mac) = AgeFormat.ReadHeader(input);
            }
            catch (FormatException)
            {
                throw Corrupt();
            }
            if (stanzas.Any(s => s.Type == AgeFormat.Scrypt))
                throw new DomainException("backup.passphraseEncrypted", "The file is encrypted with age's own passphrase mode, not for a recipient.");
            byte[]? fileKey = null;
            foreach (var stanza in stanzas) fileKey ??= AgeX25519.Unwrap(stanza, identity);
            if (fileKey is null) throw new DomainException("backup.wrongKey", "The identity does not open this file.");
            if (!CryptographicOperations.FixedTimeEquals(mac, HeaderMac(fileKey, AgeFormat.MacInput(stanzas)))) throw Corrupt();
            var nonce = new byte[AgeFormat.PayloadNonceLength];
            if (Fill(nonce) != nonce.Length) throw Corrupt();
            _cipher = PayloadCipher(fileKey, nonce);
            _nextLength = Fill(_next);
        }

        /// <summary>Reads as much as fits, until the buffer is full or the input ends.</summary>
        private int Fill(byte[] buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = _input.Read(buffer, total, buffer.Length - total);
                if (read == 0) break;
                total += read;
            }
            return total;
        }

        private void NextChunk()
        {
            if (_finished) return;
            var sealedLength = _nextLength;
            if (sealedLength < AgeFormat.TagLength) throw Corrupt(); // cut short inside a tag (or the payload ended with a non-final chunk)
            var chunk = _next.AsSpan(0, sealedLength).ToArray();
            _nextLength = Fill(_next);
            var last = _nextLength == 0;
            // Only the final chunk may be short, and it is empty only when the whole payload is.
            if (!last && sealedLength < _next.Length) throw Corrupt();
            if (last && sealedLength == AgeFormat.TagLength && _counter > 0) throw Corrupt();
            _plainLength = sealedLength - AgeFormat.TagLength;
            try
            {
                _cipher.Decrypt(AgeFormat.ChunkNonce(_counter++, last), chunk.AsSpan(0, _plainLength), chunk.AsSpan(_plainLength), _plain.AsSpan(0, _plainLength));
            }
            catch (AuthenticationTagMismatchException)
            {
                throw Corrupt();
            }
            _plainRead = 0;
            _finished = last;
        }

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var total = 0;
            while (buffer.Length > 0)
            {
                if (_plainRead == _plainLength)
                {
                    if (_finished) break;
                    NextChunk();
                    continue;
                }
                var take = Math.Min(buffer.Length, _plainLength - _plainRead);
                _plain.AsSpan(_plainRead, take).CopyTo(buffer);
                _plainRead += take;
                total += take;
                buffer = buffer[take..];
            }
            return total;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                _cipher.Dispose();
                if (!_leaveOpen) _input.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override bool CanRead => !_closed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
