// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO.Ports;

namespace Vcontrol.Worker;

internal sealed class OptolinkSerialStream : Stream
{
    private readonly SerialPort _port;

    public OptolinkSerialStream(string device)
    {
        _port = new SerialPort(device, 4800, Parity.Even, 8, StopBits.Two)
        {
            Handshake = Handshake.None,
            ReadTimeout = 100,
            WriteTimeout = 1000
        };
        try
        {
            _port.Open();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }
        catch
        {
            _port.Dispose();
            throw;
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => _port.Read(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }
        // SerialPort.BaseStream cancellation is not reliable on every target OS.
        // Poll with a bounded synchronous read and await the task before disposing.
        var bytes = new byte[buffer.Length];
        var count = await Task.Run(() =>
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return _port.Read(bytes, 0, bytes.Length);
                }
                catch (TimeoutException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }, cancellationToken);
        bytes.AsMemory(0, count).CopyTo(buffer);
        return count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytes = buffer.ToArray();
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _port.Write(bytes, 0, bytes.Length);
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _port.Dispose();
        }
        base.Dispose(disposing);
    }
}
