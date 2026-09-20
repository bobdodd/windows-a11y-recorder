using System.IO.Pipes;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Collectors.Browser;

public sealed class BrowserEvidenceReceiver : ICaptureCollector
{
    private readonly BrowserEvidenceReceiverOptions _options;
    private readonly object _gate = new();
    private readonly object _eventWriteGate = new();
    private readonly List<Task> _connections = [];
    private readonly ChromiumLauncher _launcher = new();
    private CollectorInitializationContext? _context;
    private CancellationTokenSource? _runCancellation;
    private Task? _acceptTask;
    private string? _ownedProfileDirectory;
    private long _sequence = -1;

    public BrowserEvidenceReceiver(
        BrowserEvidenceReceiverOptions? options = null)
    {
        _options = options ?? new BrowserEvidenceReceiverOptions();
        Descriptor = CollectorDescriptor.Create(
            "browser.instrumented",
            nameof(BrowserEvidenceReceiver),
            typeof(BrowserEvidenceReceiver).Assembly
                .GetName()
                .Version?
                .ToString() ?? "0.0.0",
            [
                BrowserEvidenceChannels.Lifecycle,
                BrowserEvidenceChannels.Listener,
                BrowserEvidenceChannels.Dispatch,
                BrowserEvidenceChannels.Timer,
                BrowserEvidenceChannels.Scheduler,
                BrowserEvidenceChannels.Navigation,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceChannels.Cookie
            ],
            "instrumented-chromium-local-ipc");
        ConnectionInfo = new BrowserEvidenceConnectionInfo(
            _options.PipeName,
            _options.AuthenticationToken,
            BrowserEvidenceProtocol.CurrentVersion,
            _options.BrowserInstanceId,
            _options.MaximumMessageBytes);
    }

    public CollectorDescriptor Descriptor { get; }
    public BrowserEvidenceConnectionInfo ConnectionInfo { get; }
    public CollectorLifecycleState LifecycleState { get; private set; } =
        CollectorLifecycleState.Created;
    public CollectorHealthState HealthState { get; private set; } =
        CollectorHealthState.Unknown;

    public ValueTask<CapabilityResult> InitializeAsync(
        CollectorInitializationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (LifecycleState != CollectorLifecycleState.Created)
        {
            throw new InvalidOperationException(
                $"Cannot initialize from {LifecycleState}.");
        }

        LifecycleState = CollectorLifecycleState.Initializing;
        if (_options.ChromiumExecutablePath is not null &&
            !File.Exists(_options.ChromiumExecutablePath))
        {
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
            return ValueTask.FromResult(new CapabilityResult(
                CapabilityStatus.Unavailable,
                Descriptor.Channels,
                ["The bundled instrumented Chromium executable was not found."],
                false,
                true));
        }
        _context = context;
        LifecycleState = CollectorLifecycleState.Ready;
        HealthState = CollectorHealthState.Healthy;
        return ValueTask.FromResult(CapabilityResult.Supported(
            Descriptor.Channels.ToArray()));
    }

    public async ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LifecycleState != CollectorLifecycleState.Ready ||
            _context is null)
        {
            return CollectorTransitionResult.Reject(
                LifecycleState,
                "browser-receiver-not-ready",
                "Browser evidence receiver is not ready.");
        }

        LifecycleState = CollectorLifecycleState.Starting;
        _runCancellation = new CancellationTokenSource();
        _acceptTask = AcceptConnectionsAsync(_runCancellation.Token);
        if (_options.ChromiumExecutablePath is not null)
        {
            var profileDirectory = _options.ProfileDirectory;
            if (profileDirectory is null)
            {
                profileDirectory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Windows A11y Recorder",
                    "BrowserProfiles",
                    _context.SessionId);
                _ownedProfileDirectory = profileDirectory;
            }
            try
            {
                await _launcher.LaunchAsync(
                    _options.ChromiumExecutablePath,
                    profileDirectory,
                    ConnectionInfo,
                    _options.StartUrl,
                    _options.RemoteDebuggingPort,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _runCancellation.Cancel();
                LifecycleState = CollectorLifecycleState.Failed;
                HealthState = CollectorHealthState.Failed;
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "instrumented-browser-launch-failed",
                    exception.Message);
            }
        }
        LifecycleState = CollectorLifecycleState.Running;
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        if (LifecycleState is CollectorLifecycleState.Stopped or
            CollectorLifecycleState.Disposed)
        {
            return CollectorTransitionResult.Success(LifecycleState);
        }
        if (LifecycleState is not (CollectorLifecycleState.Running or
            CollectorLifecycleState.Failed))
        {
            return CollectorTransitionResult.Reject(
                LifecycleState,
                "browser-receiver-not-running",
                "Browser evidence receiver is not running.");
        }

        LifecycleState = CollectorLifecycleState.Stopping;
        await _launcher.StopAsync(cancellationToken).ConfigureAwait(false);
        DeleteOwnedProfile();
        await _runCancellation!.CancelAsync().ConfigureAwait(false);

        var tasks = new List<Task>();
        if (_acceptTask is not null)
        {
            tasks.Add(_acceptTask);
        }
        lock (_gate)
        {
            tasks.AddRange(_connections);
        }

        try
        {
            await Task.WhenAll(tasks)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            _runCancellation.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
        }
        catch (TimeoutException)
        {
            HealthState = CollectorHealthState.Degraded;
        }

        LifecycleState = CollectorLifecycleState.Stopped;
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask DisposeAsync()
    {
        if (LifecycleState is CollectorLifecycleState.Running or
            CollectorLifecycleState.Failed)
        {
            await StopAsync(
                new SessionBoundary(0, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }
        _runCancellation?.Dispose();
        await _launcher.DisposeAsync().ConfigureAwait(false);
        DeleteOwnedProfile();
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pipe = BrowserEvidencePipeFactory.Create(
                    _options.PipeName);
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                var connection = HandleConnectionAsync(pipe, cancellationToken);
                lock (_gate)
                {
                    _connections.RemoveAll(task => task.IsCompleted);
                    _connections.Add(connection);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
            throw;
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                var helloDocument = await BrowserProtocol.ReadFrameAsync(
                    pipe,
                    _options.MaximumMessageBytes,
                    cancellationToken).ConfigureAwait(false) ??
                    throw new EndOfStreamException(
                        "Browser disconnected before authentication.");
                using (helloDocument)
                {
                    var hello = BrowserProtocol.Deserialize<BrowserHelloMessage>(
                        helloDocument.RootElement);
                    ValidateHello(hello);
                    EmitLifecycle(
                        BrowserEvidenceEventTypes.Connected,
                        new
                        {
                            protocolVersion = hello.ProtocolVersion,
                            browserInstanceId = hello.BrowserInstanceId,
                            processId = hello.ProcessId,
                            processType = hello.ProcessType,
                            chromiumVersion = hello.ChromiumVersion,
                            parentProcessId = hello.ParentProcessId,
                            childProcessId = hello.ChildProcessId
                        });
                    var mapper = await SynchronizeClockAsync(
                        pipe,
                        hello,
                        cancellationToken).ConfigureAwait(false);
                    EmitLifecycle(
                        BrowserEvidenceEventTypes.ClockSynchronized,
                        new
                        {
                            protocolVersion = hello.ProtocolVersion,
                            browserInstanceId = hello.BrowserInstanceId,
                            processId = hello.ProcessId,
                            processType = hello.ProcessType,
                            parentProcessId = hello.ParentProcessId,
                            childProcessId = hello.ChildProcessId,
                            clockMappingId =
                                $"chromium:{hello.BrowserInstanceId}:" +
                                $"{hello.ProcessId}",
                            monotonicFrequency = hello.MonotonicFrequency,
                            uncertaintyNanoseconds =
                                mapper.UncertaintyNanoseconds
                        });
                    await ReceiveEvidenceAsync(
                        pipe,
                        hello,
                        mapper,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                HealthState = CollectorHealthState.Degraded;
                EmitOmission("browser-connection-rejected");
            }
        }
    }

    private void ValidateHello(BrowserHelloMessage hello)
    {
        if (hello.Kind != "hello" ||
            hello.ProtocolVersion != BrowserEvidenceProtocol.CurrentVersion ||
            string.IsNullOrWhiteSpace(hello.BrowserInstanceId) ||
            hello.BrowserInstanceId != _options.BrowserInstanceId ||
            hello.ProcessId <= 0 ||
            hello.ProcessType is not ("browser" or "renderer") ||
            (hello.ProcessType == "browser" &&
                (hello.ParentProcessId is not null ||
                    hello.ChildProcessId is not null)) ||
            (hello.ProcessType != "browser" &&
                (hello.ParentProcessId is null or <= 0 ||
                    hello.ChildProcessId is null or <= 0)) ||
            !TryReadPositiveInt64(hello.MonotonicFrequency, out _))
        {
            throw new InvalidDataException("Invalid browser hello message.");
        }

        var supplied = Encoding.UTF8.GetBytes(hello.AuthenticationToken);
        var expected = Encoding.UTF8.GetBytes(_options.AuthenticationToken);
        if (supplied.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(supplied, expected))
        {
            throw new UnauthorizedAccessException(
                "Browser evidence authentication failed.");
        }
    }

    private async Task<BrowserClockMapper> SynchronizeClockAsync(
        NamedPipeServerStream pipe,
        BrowserHelloMessage hello,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var recorderSend = _context!.Clock.GetElapsedNanoseconds();
        await BrowserProtocol.WriteFrameAsync(
            pipe,
            new
            {
                kind = "clock-sync-request",
                protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                requestId,
                recorderSendNanoseconds = recorderSend
            },
            cancellationToken).ConfigureAwait(false);

        var responseDocument = await BrowserProtocol.ReadFrameAsync(
            pipe,
            _options.MaximumMessageBytes,
            cancellationToken).ConfigureAwait(false) ??
            throw new EndOfStreamException(
                "Browser disconnected during clock synchronization.");
        using (responseDocument)
        {
            var recorderReceive = _context.Clock.GetElapsedNanoseconds();
            var response =
                BrowserProtocol.Deserialize<BrowserClockSyncResponse>(
                    responseDocument.RootElement);
            if (response.Kind != "clock-sync-response" ||
                response.RequestId != requestId)
            {
                throw new InvalidDataException(
                    "Invalid browser clock synchronization response.");
            }

            var mapper = BrowserClockMapper.Create(
                recorderSend,
                ReadInt64(response.BrowserReceiveTicks, "browserReceiveTicks"),
                ReadInt64(response.BrowserSendTicks, "browserSendTicks"),
                recorderReceive,
                ReadPositiveInt64(
                    hello.MonotonicFrequency,
                    "monotonicFrequency"));
            await BrowserProtocol.WriteFrameAsync(
                pipe,
                new
                {
                    kind = "ready",
                    protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                    requestId,
                    uncertaintyNanoseconds = mapper.UncertaintyNanoseconds
                },
                cancellationToken).ConfigureAwait(false);
            return mapper;
        }
    }

    private async Task ReceiveEvidenceAsync(
        NamedPipeServerStream pipe,
        BrowserHelloMessage hello,
        BrowserClockMapper mapper,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var document = await BrowserProtocol.ReadFrameAsync(
                pipe,
                _options.MaximumMessageBytes,
                cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return;
            }

            using (document)
            {
                var message = BrowserProtocol.Deserialize<BrowserEvidenceMessage>(
                    document.RootElement);
                if (message.Kind != "evidence" ||
                    message.ProtocolVersion !=
                        BrowserEvidenceProtocol.CurrentVersion)
                {
                    throw new InvalidDataException(
                        "Invalid browser evidence message.");
                }

                var payload = BrowserProtocol.ValidateEvidencePayload(
                    message.Channel,
                    message.EventType,
                    message.Payload);
                var sessionNanoseconds = mapper.MapToSessionNanoseconds(
                    ReadInt64(
                        message.BrowserTimestampTicks,
                        "browserTimestampTicks"));
                if (sessionNanoseconds < 0)
                {
                    throw new InvalidDataException(
                        "Browser event maps before the session clock origin.");
                }

                if (!WriteSequencedRecord(sequence => new RecorderEvent
                {
                    SchemaVersion = RecorderEvent.CurrentSchemaVersion,
                    EventId =
                        $"{_context!.SessionId}:{Descriptor.InstanceId}:" +
                        $"{message.Channel}:{sequence}",
                    EvidenceClass = EvidenceClasses.Observed,
                    SessionId = _context.SessionId,
                    CollectorType = Descriptor.CollectorType,
                    CollectorInstanceId = Descriptor.InstanceId,
                    ProducerVersion = Descriptor.ImplementationVersion,
                    Channel = message.Channel,
                    CaptureMethod = Descriptor.CaptureMethod,
                    Sequence = sequence,
                    MonotonicNanoseconds = sessionNanoseconds,
                    ClockMappingId =
                        $"chromium:{hello.BrowserInstanceId}:{hello.ProcessId}",
                    NativeTimestamp = new NativeTimestamp(
                        "chromium-monotonic",
                        ReadInt64(
                            message.BrowserTimestampTicks,
                            "browserTimestampTicks"),
                        "ticks"),
                    TimestampUncertaintyNanoseconds =
                        mapper.UncertaintyNanoseconds,
                    ObservedUtc = _context.Clock.OriginUtc.AddTicks(
                        sessionNanoseconds / 100),
                    EventType = message.EventType,
                    Payload = payload,
                    QualityFlags = message.QualityFlags ?? [],
                    RelatedEvidenceIds = [],
                    Analysis = null
                }))
                {
                    HealthState = CollectorHealthState.Degraded;
                }
            }
        }
    }

    private void EmitLifecycle(string eventType, object payload)
    {
        if (_context is null)
        {
            return;
        }

        if (!WriteSequencedRecord(sequence => RecorderEventFactory.Create(
                _context.SessionId,
                Descriptor,
                BrowserEvidenceChannels.Lifecycle,
                sequence,
                _context.Clock.GetElapsedNanoseconds(),
                eventType,
                payload)))
        {
            HealthState = CollectorHealthState.Degraded;
        }
    }

    private void EmitOmission(string reason)
    {
        if (_context is null)
        {
            return;
        }

        WriteSequencedRecord(sequence => RecorderEventFactory.Create(
            _context.SessionId,
            Descriptor,
            BrowserEvidenceChannels.Listener,
            sequence,
            _context.Clock.GetElapsedNanoseconds(),
            BrowserEvidenceEventTypes.Omission,
            new { reason, count = 1 }));
    }

    internal bool WriteSequencedRecord(
        Func<ulong, RecorderEvent> createRecord)
    {
        ArgumentNullException.ThrowIfNull(createRecord);
        if (_context is null)
        {
            return false;
        }

        lock (_eventWriteGate)
        {
            var sequence = unchecked(
                (ulong)Interlocked.Increment(ref _sequence));
            return _context.EventSink.TryWrite(createRecord(sequence));
        }
    }

    private static long ReadInt64(string value, string property)
    {
        if (!long.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var result))
        {
            throw new InvalidDataException(
                $"Browser protocol property {property} was not an integer.");
        }
        return result;
    }

    private static long ReadPositiveInt64(string value, string property)
    {
        var result = ReadInt64(value, property);
        if (result <= 0)
        {
            throw new InvalidDataException(
                $"Browser protocol property {property} was not positive.");
        }
        return result;
    }

    private static bool TryReadPositiveInt64(string value, out long result) =>
        long.TryParse(
            value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out result) &&
        result > 0;

    private void DeleteOwnedProfile()
    {
        var directory = _ownedProfileDirectory;
        _ownedProfileDirectory = null;
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            HealthState = CollectorHealthState.Degraded;
            EmitOmission("ephemeral-browser-profile-delete-failed");
        }
    }
}
