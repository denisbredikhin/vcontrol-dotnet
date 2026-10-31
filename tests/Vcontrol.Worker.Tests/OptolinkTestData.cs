using System.Xml.Linq;

namespace Vcontrol.Worker.Tests;

internal static class OptolinkTestData
{
    public static OptolinkConfiguration Configuration(string deviceId = "20CB")
    {
        var control = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "OptolinkFixtures", "vcontrold.xml"));
        control.Root!.Element("unix")!.Element("config")!.Element("device")!.SetAttributeValue("ID", deviceId);
        var vito = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "OptolinkFixtures", "vito.xml"));
        return new OptolinkConfiguration(control, vito);
    }
}

internal sealed class OptolinkTranscriptStream(params (string Request, string Response)[] exchanges) : Stream
{
    private readonly Queue<(string Request, string Response)> _exchanges = new(exchanges);
    private readonly Queue<byte> _received = [];

    public List<string> Writes { get; } = [];
    public bool IsDisposed { get; private set; }
    public bool IsComplete => _exchanges.Count == 0 && _received.Count == 0;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        var request = Convert.ToHexString(buffer.AsSpan(offset, count));
        Writes.Add(request);
        if (_exchanges.Count == 0)
        {
            throw new InvalidOperationException($"Unexpected serial write {request}.");
        }
        var exchange = _exchanges.Dequeue();
        if (exchange.Request != request || _received.Count != 0)
        {
            throw new InvalidOperationException($"Expected write {exchange.Request} with no unread bytes; received {request}.");
        }
        foreach (var value in Convert.FromHexString(exchange.Response))
        {
            _received.Enqueue(value);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0)
        {
            return 0;
        }
        if (_received.Count == 0)
        {
            throw new IOException("Serial transcript disconnected.");
        }
        buffer[offset] = _received.Dequeue();
        return 1;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = new byte[buffer.Length];
        var count = Read(bytes, 0, bytes.Length);
        bytes.AsMemory(0, count).CopyTo(buffer);
        return ValueTask.FromResult(count);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.ToArray(), 0, buffer.Length);
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
