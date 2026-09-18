using System.Text.Json;
using Recorder.Contracts;
using Recorder.Coordinator;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class SessionCoordinatorTests
{
    [Fact]
    public async Task FinalizesManifestAndRemovesRecordingMarker()
    {
        var outputRoot = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        var collector = new FakeCollector();

        await using (var coordinator = new SessionCoordinator(_ => [collector]))
        {
            var started = await coordinator.StartAsync(
                new RecordingOptions { OutputRoot = outputRoot },
                TestContext.Current.CancellationToken);
            Assert.Equal(RecordingSessionState.Recording, started.State);
            Assert.True(File.Exists(Path.Combine(
                started.SessionDirectory!,
                ".recording")));
            Assert.True(coordinator.AddMarker("Reached search results"));

            var stopped = await coordinator.StopAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal(RecordingSessionState.Completed, stopped.State);
            Assert.False(File.Exists(Path.Combine(
                stopped.SessionDirectory!,
                ".recording")));
            var validationPath = Path.Combine(
                stopped.SessionDirectory!,
                SessionArchiveValidator.ReportRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Assert.True(File.Exists(validationPath));
            using var validation = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    validationPath,
                    TestContext.Current.CancellationToken));
            Assert.True(validation.RootElement.GetProperty("isValid").GetBoolean());
            Assert.Equal(
                3,
                validation.RootElement.GetProperty("eventsValidated").GetInt64());

            using var manifest = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(stopped.SessionDirectory!, "manifest.json"),
                    TestContext.Current.CancellationToken));
            var root = manifest.RootElement;
            Assert.Equal("1.1", root.GetProperty("schemaVersion").GetString());
            Assert.Equal("completed", root.GetProperty("status").GetString());
            Assert.NotEqual(
                JsonValueKind.Null,
                root.GetProperty("endedUtc").ValueKind);
            Assert.True(root.GetProperty("durationNanoseconds").GetInt64() > 0);
            Assert.Equal(3, root.GetProperty("acceptedEventCount").GetInt64());
            Assert.Equal(0, root.GetProperty("droppedEventCount").GetInt64());

            var eventsArtifact = root.GetProperty("artifacts")
                .EnumerateArray()
                .Single(item =>
                    item.GetProperty("path").GetString() == "events.ndjson");
            Assert.True(eventsArtifact.GetProperty("sizeBytes").GetInt64() > 0);
            Assert.Equal(
                64,
                eventsArtifact.GetProperty("sha256").GetString()!.Length);

            var lines = await File.ReadAllLinesAsync(
                Path.Combine(stopped.SessionDirectory!, "events.ndjson"),
                TestContext.Current.CancellationToken);
            Assert.Contains(lines, line =>
                line.Contains(
                    "\"eventType\":\"session-marker\"",
                    StringComparison.Ordinal));
        }

        Directory.Delete(outputRoot, recursive: true);
    }

    private sealed class FakeCollector : ICaptureCollector
    {
        private CollectorInitializationContext? _context;
        private long _sequence = -1;

        public CollectorDescriptor Descriptor { get; } =
            CollectorDescriptor.Create(
                "test.fake",
                nameof(FakeCollector),
                "1.0",
                ["test.events"],
                "test");

        public CollectorLifecycleState LifecycleState { get; private set; } =
            CollectorLifecycleState.Created;

        public CollectorHealthState HealthState { get; private set; } =
            CollectorHealthState.Unknown;

        public ValueTask<CapabilityResult> InitializeAsync(
            CollectorInitializationContext context,
            CancellationToken cancellationToken)
        {
            _context = context;
            LifecycleState = CollectorLifecycleState.Ready;
            HealthState = CollectorHealthState.Healthy;
            return ValueTask.FromResult(CapabilityResult.Supported("test.events"));
        }

        public ValueTask<CollectorTransitionResult> StartAsync(
            SessionBoundary boundary,
            CancellationToken cancellationToken)
        {
            LifecycleState = CollectorLifecycleState.Running;
            Write("started", boundary.MonotonicNanoseconds);
            return ValueTask.FromResult(
                CollectorTransitionResult.Success(LifecycleState));
        }

        public ValueTask<CollectorTransitionResult> StopAsync(
            SessionBoundary boundary,
            CancellationToken cancellationToken)
        {
            Write("stopped", boundary.MonotonicNanoseconds);
            LifecycleState = CollectorLifecycleState.Stopped;
            return ValueTask.FromResult(
                CollectorTransitionResult.Success(LifecycleState));
        }

        public ValueTask DisposeAsync()
        {
            LifecycleState = CollectorLifecycleState.Disposed;
            return ValueTask.CompletedTask;
        }

        private void Write(string action, long monotonicNanoseconds)
        {
            var sequence = unchecked(
                (ulong)Interlocked.Increment(ref _sequence));
            Assert.True(_context!.EventSink.TryWrite(RecorderEventFactory.Create(
                _context.SessionId,
                Descriptor,
                "test.events",
                sequence,
                monotonicNanoseconds,
                "test-event",
                new { action })));
        }
    }
}
