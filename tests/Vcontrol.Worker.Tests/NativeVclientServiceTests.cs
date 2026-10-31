using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Vcontrol.Worker.Tests;

public sealed class NativeVclientServiceTests
{
    [Fact]
    public async Task Native_query_returns_readings_without_a_daemon()
    {
        using var stream = new OptolinkTranscriptStream(("04", "05"), ("160000", "06"),
            ("4105000108000210", "0641070101080002D700EA"), ("06", ""));
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => stream);

        var result = await service.QueryAsync(["getTempA"], "timer", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        result.Stderr.Should().BeEmpty();
        result.Readings.Should().ContainSingle();
        result.Readings[0].Command.Should().Be("getTempA");
        result.Readings[0].Value.Should().Be(21.5);
        result.Readings[0].Raw.Should().Be("21.500000 Grad Celsius");
        result.Readings[0].Error.Should().BeNull();
        stream.IsComplete.Should().BeTrue();
        stream.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Native_write_returns_success_only_after_device_confirmation()
    {
        using var stream = new OptolinkTranscriptStream(("04", "05"), ("160000", "06"),
            ("410600026300012D99", "06410501026300016C"), ("06", ""));
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => stream);

        var result = await service.QueryAsync(["setTempWWsoll 45"], "command", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        result.Stderr.Should().BeEmpty();
        result.Readings.Should().ContainSingle();
        result.Readings[0].Raw.Should().Be("OK");
        result.Readings[0].Error.Should().BeNull();
        stream.IsComplete.Should().BeTrue();
    }

    [Theory]
    [InlineData("missing", "20CB", "*Unknown*")]
    [InlineData("setTempWWsoll 256", "20CB", "*register range*")]
    [InlineData("setTempWWsoll 45", "2053", "*SETADDR*")]
    [InlineData("get 0800", "20CB", "*empty definition*")]
    public async Task Invalid_or_unsupported_commands_do_not_open_hardware(string input, string id, string error)
    {
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => throw new InvalidOperationException("Hardware must not be opened."), id);

        var result = await service.QueryAsync([input], "command", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Match(error);
        result.Readings.Should().ContainSingle();
        result.Readings[0].Error.Should().Match(error);
    }

    [Fact]
    public async Task Batch_preserves_successes_and_reports_command_errors()
    {
        using var stream = new OptolinkTranscriptStream(("04", "05"), ("01F7080002", "CBFF"));
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => stream, "2098");

        var result = await service.QueryAsync(["missing", "getTempA"], "timer", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(1);
        result.Readings.Should().HaveCount(2);
        result.Readings[0].Error.Should().Contain("Unknown");
        result.Readings[1].Value.Should().Be(-5.3);
        result.Readings[1].Error.Should().BeNull();
        stream.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_query_does_not_open_hardware()
    {
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => throw new InvalidOperationException("Hardware must not be opened."));

        var result = await service.QueryAsync(["", " "], "timer", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        result.Readings.Should().BeEmpty();
        result.Stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task Read_retries_a_disconnected_serial_link()
    {
        using var failed = new OptolinkTranscriptStream(("04", ""));
        using var successful = new OptolinkTranscriptStream(("04", "05"), ("01F7080002", "CBFF"));
        var streams = new Queue<Stream>([failed, successful]);
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => streams.Dequeue(), "2098");

        var result = await service.QueryAsync(["getTempA"], "timer", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        result.Readings[0].Value.Should().Be(-5.3);
        streams.Should().BeEmpty();
        failed.IsDisposed.Should().BeTrue();
        successful.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Lost_write_acknowledgement_does_not_replay_the_write()
    {
        using var stream = new OptolinkTranscriptStream(("04", "05"), ("01F46300012D", ""));
        var opens = 0;
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => { opens++; return stream; }, "2098");

        var result = await service.QueryAsync(["setTempWWsoll 45"], "command", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(-1);
        result.Readings[0].Error.Should().Contain("disconnected");
        opens.Should().Be(1);
        stream.Writes.Should().Equal("04", "01F46300012D");
        stream.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Rejected_write_acknowledgement_is_an_error_not_a_reading()
    {
        using var stream = new OptolinkTranscriptStream(("04", "05"), ("01F46300012D", "05"));
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => stream, "2098");

        var result = await service.QueryAsync(["setTempWWsoll 45"], "command", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(-1);
        result.Readings[0].Error.Should().Contain("not acknowledged");
        stream.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_and_serial_stream_is_closed()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var stream = new CancelledStream(cancellation.Cancel);
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => stream, "2098");

        var act = () => service.QueryAsync(["getTempA"], "timer", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stream.IsDisposed.Should().BeTrue();
    }

    private static NativeVclientService Create(VcontrolMetrics metrics, Func<Stream> open, string id = "20CB") =>
        new(NullLogger<NativeVclientService>.Instance, OptolinkTestData.Configuration(id), metrics, open, TimeProvider.System);

    [Fact]
    public async Task Operation_timeout_is_reported_and_serial_stream_is_closed()
    {
        using var stream = new CancelledStream(() => { });
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => stream, "2098");

        var result = await service.QueryAsync(["getTempA"], "timer", TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(-3);
        result.Stderr.Should().Contain("timed out");
        result.Readings[0].Error.Should().Contain("timed out");
        stream.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Timer_and_command_queries_do_not_interleave_serial_transactions()
    {
        using var first = new GatedStream(new OptolinkTranscriptStream(("04", "05"), ("01F7080002", "D700")));
        using var second = new OptolinkTranscriptStream(("04", "05"), ("01F7080002", "CBFF"));
        var streams = new Queue<Stream>([first, second]);
        var opened = 0;
        using var metrics = new VcontrolMetrics(false);
        using var service = Create(metrics, () => { opened++; return streams.Dequeue(); }, "2098");

        var results = await RunConcurrentQueriesAsync(service, first, () => opened, TestContext.Current.CancellationToken);

        results.OpenedWhileFirstBlocked.Should().Be(1);
        opened.Should().Be(2);
        results.Timer.Readings[0].Value.Should().Be(21.5);
        results.Command.Readings[0].Value.Should().Be(-5.3);
        results.Timer.ExitCode.Should().Be(0);
        results.Command.ExitCode.Should().Be(0);
        second.IsComplete.Should().BeTrue();
    }

    private static async Task<(VclientQueryResult Timer, VclientQueryResult Command, int OpenedWhileFirstBlocked)>
        RunConcurrentQueriesAsync(NativeVclientService service, GatedStream first, Func<int> opened, CancellationToken ct)
    {
        var timer = service.QueryAsync(["getTempA"], "timer", ct);
        await first.Entered.Task.WaitAsync(ct);
        var command = service.QueryAsync(["getTempA"], "command", ct);
        var count = opened();
        first.Continue.SetResult();
        return (await timer, await command, count);
    }

    private sealed class GatedStream(OptolinkTranscriptStream inner) : Stream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
            return await inner.ReadAsync(buffer, cancellationToken);
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class CancelledStream(Action cancel) : Stream
    {
        public bool IsDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new OperationCanceledException();
        public override void Write(byte[] buffer, int offset, int count) => throw new OperationCanceledException();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancel();
            return ValueTask.FromException(new OperationCanceledException(cancellationToken));
        }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
