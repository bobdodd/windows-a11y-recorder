using System.Text.Json;

namespace Recorder.Contracts;

public sealed record RecorderEvent
{
    public const string CurrentSchemaVersion = SessionSchemaVersions.Event;

    public required string SchemaVersion { get; init; }
    public required string EventId { get; init; }
    public required string EvidenceClass { get; init; }
    public required string SessionId { get; init; }
    public required string CollectorType { get; init; }
    public required string CollectorInstanceId { get; init; }
    public required string ProducerVersion { get; init; }
    public required string Channel { get; init; }
    public required string CaptureMethod { get; init; }
    public required ulong Sequence { get; init; }
    public required long MonotonicNanoseconds { get; init; }
    public required string ClockMappingId { get; init; }
    public NativeTimestamp? NativeTimestamp { get; init; }
    public long? TimestampUncertaintyNanoseconds { get; init; }
    public required DateTimeOffset ObservedUtc { get; init; }
    public required string EventType { get; init; }
    public required JsonElement Payload { get; init; }
    public IReadOnlyList<string> QualityFlags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RelatedEvidenceIds { get; init; } = Array.Empty<string>();
    public AnalysisProvenance? Analysis { get; init; }
}

public static class EvidenceClasses
{
    public const string Observed = "observed";
    public const string Derived = "derived";
    public const string Inferred = "inferred";
    public const string Unknown = "unknown";
}

public sealed record NativeTimestamp(
    string Domain,
    long Value,
    string Unit);

public sealed record AnalysisProvenance(
    string Analyzer,
    string AnalyzerVersion,
    string Method,
    string? ConfidenceCategory,
    IReadOnlyList<string> CompetingInterpretations,
    string? InsufficientEvidenceReason);

public static class RecorderEventFactory
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static RecorderEvent Create<T>(
        string sessionId,
        CollectorDescriptor collector,
        string channel,
        ulong sequence,
        long monotonicNanoseconds,
        string eventType,
        T payload,
        params string[] qualityFlags) =>
        new()
        {
            SchemaVersion = RecorderEvent.CurrentSchemaVersion,
            EventId = CreateEventId(sessionId, collector.InstanceId, channel, sequence),
            EvidenceClass = EvidenceClasses.Observed,
            SessionId = sessionId,
            CollectorType = collector.CollectorType,
            CollectorInstanceId = collector.InstanceId,
            ProducerVersion = collector.ImplementationVersion,
            Channel = channel,
            CaptureMethod = collector.CaptureMethod,
            Sequence = sequence,
            MonotonicNanoseconds = monotonicNanoseconds,
            ClockMappingId = "session-stopwatch-origin",
            NativeTimestamp = null,
            TimestampUncertaintyNanoseconds = null,
            ObservedUtc = DateTimeOffset.UtcNow,
            EventType = eventType,
            Payload = JsonSerializer.SerializeToElement(payload, JsonOptions),
            QualityFlags = qualityFlags,
            RelatedEvidenceIds = [],
            Analysis = null
        };

    internal static string CreateEventId(
        string sessionId,
        string producerInstanceId,
        string channel,
        ulong sequence) =>
        $"{sessionId}:{producerInstanceId}:{channel}:{sequence}";

    internal static JsonElement CreatePayload<T>(T payload) =>
        JsonSerializer.SerializeToElement(payload, JsonOptions);
}

public static class AnalysisEventFactory
{
    public static RecorderEvent CreateDerived<T>(
        string sessionId,
        CollectorDescriptor analyzer,
        string channel,
        ulong sequence,
        long monotonicNanoseconds,
        string eventType,
        T payload,
        IReadOnlyList<string> relatedEvidenceIds,
        string method,
        params string[] qualityFlags) =>
        Create(
            EvidenceClasses.Derived,
            sessionId,
            analyzer,
            channel,
            sequence,
            monotonicNanoseconds,
            eventType,
            payload,
            relatedEvidenceIds,
            new AnalysisProvenance(
                analyzer.Implementation,
                analyzer.ImplementationVersion,
                method,
                null,
                [],
                null),
            qualityFlags);

    public static RecorderEvent CreateInferred<T>(
        string sessionId,
        CollectorDescriptor analyzer,
        string channel,
        ulong sequence,
        long monotonicNanoseconds,
        string eventType,
        T payload,
        IReadOnlyList<string> relatedEvidenceIds,
        string method,
        string confidenceCategory,
        IReadOnlyList<string>? competingInterpretations = null,
        params string[] qualityFlags) =>
        Create(
            EvidenceClasses.Inferred,
            sessionId,
            analyzer,
            channel,
            sequence,
            monotonicNanoseconds,
            eventType,
            payload,
            relatedEvidenceIds,
            new AnalysisProvenance(
                analyzer.Implementation,
                analyzer.ImplementationVersion,
                method,
                confidenceCategory,
                competingInterpretations ?? [],
                null),
            qualityFlags);

    public static RecorderEvent CreateUnknown<T>(
        string sessionId,
        CollectorDescriptor analyzer,
        string channel,
        ulong sequence,
        long monotonicNanoseconds,
        string eventType,
        T payload,
        IReadOnlyList<string> relatedEvidenceIds,
        string method,
        string insufficientEvidenceReason,
        params string[] qualityFlags) =>
        Create(
            EvidenceClasses.Unknown,
            sessionId,
            analyzer,
            channel,
            sequence,
            monotonicNanoseconds,
            eventType,
            payload,
            relatedEvidenceIds,
            new AnalysisProvenance(
                analyzer.Implementation,
                analyzer.ImplementationVersion,
                method,
                null,
                [],
                insufficientEvidenceReason),
            qualityFlags);

    private static RecorderEvent Create<T>(
        string evidenceClass,
        string sessionId,
        CollectorDescriptor analyzer,
        string channel,
        ulong sequence,
        long monotonicNanoseconds,
        string eventType,
        T payload,
        IReadOnlyList<string> relatedEvidenceIds,
        AnalysisProvenance analysis,
        IReadOnlyList<string> qualityFlags) =>
        new()
        {
            SchemaVersion = RecorderEvent.CurrentSchemaVersion,
            EventId = RecorderEventFactory.CreateEventId(
                sessionId,
                analyzer.InstanceId,
                channel,
                sequence),
            EvidenceClass = evidenceClass,
            SessionId = sessionId,
            CollectorType = analyzer.CollectorType,
            CollectorInstanceId = analyzer.InstanceId,
            ProducerVersion = analyzer.ImplementationVersion,
            Channel = channel,
            CaptureMethod = analyzer.CaptureMethod,
            Sequence = sequence,
            MonotonicNanoseconds = monotonicNanoseconds,
            ClockMappingId = "session-stopwatch-origin",
            NativeTimestamp = null,
            TimestampUncertaintyNanoseconds = null,
            ObservedUtc = DateTimeOffset.UtcNow,
            EventType = eventType,
            Payload = RecorderEventFactory.CreatePayload(payload),
            QualityFlags = qualityFlags,
            RelatedEvidenceIds = relatedEvidenceIds,
            Analysis = analysis
        };
}
