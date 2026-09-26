using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// A problem found in one event record.
/// </summary>
/// <param name="Code">A stable code for the problem.</param>
/// <param name="Path">
/// A JSON pointer into the record, such as <c>#/payload/target</c>.
/// </param>
public sealed record EventValidationIssue(
    string Code,
    string Path,
    string Message);

/// <summary>
/// Checks each event of one recording as it arrives: the record's envelope,
/// its provenance and analysis, its order within its stream, and, on the
/// channels the recorder defines, its payload. An event with any issue is
/// not stored.
/// </summary>
/// <remarks>
/// The checks are made on one record at a time and on the stream it belongs
/// to. Whether a cited related event exists in the recording is not
/// checked, because a record may cite an event that has not arrived yet.
/// Instances are not thread safe.
/// </remarks>
public sealed class EventRecordValidator
{
    private readonly string _sessionKey;
    private readonly Dictionary<(string Instance, string Channel), ulong> _lastSequence = [];
    private readonly Dictionary<(string Instance, string Channel, string Clock), long> _lastTime = [];

    public EventRecordValidator(string sessionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        _sessionKey = sessionKey;
    }

    /// <summary>
    /// Checks an event and, when it has no issues, records it as the latest
    /// event of its stream.
    /// </summary>
    public IReadOnlyList<EventValidationIssue> Validate(RecorderEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var issues = new List<EventValidationIssue>();
        ValidateEnvelope(record, issues);
        if (issues.Count != 0)
        {
            return issues;
        }

        ValidateProvenance(record, issues);
        var stream = (record.CollectorInstanceId, record.Channel);
        if (_lastSequence.TryGetValue(stream, out var previousSequence) &&
            record.Sequence <= previousSequence)
        {
            Add(
                issues,
                "event-sequence-not-increasing",
                "#/sequence",
                $"Sequence {record.Sequence} does not follow {previousSequence} in its stream.");
        }

        var clock = (record.CollectorInstanceId, record.Channel, record.ClockMappingId);
        if (_lastTime.TryGetValue(clock, out var previousTime) &&
            record.MonotonicNanoseconds < previousTime)
        {
            Add(
                issues,
                "event-time-regressed",
                "#/monotonicNanoseconds",
                "Monotonic time moved backward within one clock mapping.");
        }

        EventPayloadValidator.Validate(
            record.Channel,
            record.EventType,
            record.Payload,
            record.MonotonicNanoseconds,
            issues);
        if (issues.Count == 0)
        {
            _lastSequence[stream] = record.Sequence;
            _lastTime[clock] = record.MonotonicNanoseconds;
        }

        return issues;
    }

    // The checks the other checks depend on: the record's version, its
    // session, and the strings that identify its stream.
    private void ValidateEnvelope(RecorderEvent record, List<EventValidationIssue> issues)
    {
        if (!SessionSchemaVersions.IsSupportedEvent(record.SchemaVersion))
        {
            Add(
                issues,
                "event-version-unsupported",
                "#/schemaVersion",
                $"Event schema version '{record.SchemaVersion}' is not supported.");
            return;
        }

        if (!string.Equals(record.SessionId, _sessionKey, StringComparison.Ordinal))
        {
            Add(
                issues,
                "event-session-mismatch",
                "#/sessionId",
                "The event belongs to another session.");
            return;
        }

        foreach (var (property, value) in new[]
                 {
                     ("collectorType", record.CollectorType),
                     ("collectorInstanceId", record.CollectorInstanceId),
                     ("channel", record.Channel),
                     ("captureMethod", record.CaptureMethod),
                     ("eventType", record.EventType)
                 })
        {
            if (IsBlank(value))
            {
                Add(issues, "event-string-invalid", $"#/{property}", $"{property} must be a nonempty string.");
            }
        }

        foreach (var (property, value) in new[]
                 {
                     ("producerVersion", record.ProducerVersion),
                     ("clockMappingId", record.ClockMappingId)
                 })
        {
            if (IsBlank(value))
            {
                Add(issues, "event-provenance-invalid", $"#/{property}", $"{property} must be a nonempty string.");
            }
        }
    }

    private static void ValidateProvenance(RecorderEvent record, List<EventValidationIssue> issues)
    {
        if (!string.Equals(
                record.EventId,
                RecorderEventFactory.CreateEventId(
                    record.SessionId,
                    record.CollectorInstanceId,
                    record.Channel,
                    record.Sequence),
                StringComparison.Ordinal))
        {
            Add(
                issues,
                "event-id-invalid",
                "#/eventId",
                "The event ID must be formed from the session, collector instance, channel, and sequence.");
        }

        if (record.Sequence > long.MaxValue)
        {
            Add(issues, "event-sequence-invalid", "#/sequence", "Sequence is outside the stored range.");
        }

        if (record.MonotonicNanoseconds < 0)
        {
            Add(
                issues,
                "event-time-invalid",
                "#/monotonicNanoseconds",
                "Monotonic time must be a nonnegative integer.");
        }

        if (record.TimestampUncertaintyNanoseconds < 0)
        {
            Add(
                issues,
                "timestamp-uncertainty-invalid",
                "#/timestampUncertaintyNanoseconds",
                "Timestamp uncertainty must be a nonnegative integer or null.");
        }

        if (record.NativeTimestamp is { } native &&
            (IsBlank(native.Domain) || IsBlank(native.Unit)))
        {
            Add(
                issues,
                "native-timestamp-invalid",
                "#/nativeTimestamp",
                "A native timestamp must name its domain and unit.");
        }

        if (record.QualityFlags is null || record.QualityFlags.Any(IsBlank))
        {
            Add(
                issues,
                "event-quality-flags-invalid",
                "#/qualityFlags",
                "Quality flags must be nonempty strings.");
        }

        var evidenceClass = record.EvidenceClass;
        if (evidenceClass is not (
            EvidenceClasses.Observed or
            EvidenceClasses.Derived or
            EvidenceClasses.Inferred or
            EvidenceClasses.Unknown))
        {
            Add(
                issues,
                "evidence-class-invalid",
                "#/evidenceClass",
                "Evidence class must be observed, derived, inferred, or unknown.");
            return;
        }

        var related = record.RelatedEvidenceIds;
        if (related is null ||
            related.Any(IsBlank) ||
            related.Distinct(StringComparer.Ordinal).Count() != related.Count)
        {
            Add(
                issues,
                "related-evidence-invalid",
                "#/relatedEvidenceIds",
                "relatedEvidenceIds must be an array of unique, nonempty strings.");
            return;
        }

        if (related.Contains(record.EventId, StringComparer.Ordinal))
        {
            Add(
                issues,
                "related-evidence-self-reference",
                "#/relatedEvidenceIds",
                "An event cannot cite itself as related evidence.");
        }

        ValidateAnalysis(record, evidenceClass, related, issues);
    }

    private static void ValidateAnalysis(
        RecorderEvent record,
        string evidenceClass,
        IReadOnlyList<string> related,
        List<EventValidationIssue> issues)
    {
        var analysis = record.Analysis;
        if (evidenceClass == EvidenceClasses.Observed)
        {
            if (related.Count != 0)
            {
                Add(
                    issues,
                    "observed-event-cites-evidence",
                    "#/relatedEvidenceIds",
                    "Observed evidence cannot cite supporting evidence.");
            }

            if (analysis is not null)
            {
                Add(
                    issues,
                    "observed-event-has-analysis",
                    "#/analysis",
                    "Observed evidence must not contain analysis provenance.");
            }

            return;
        }

        if (analysis is null)
        {
            Add(
                issues,
                "analysis-provenance-missing",
                "#/analysis",
                "Derived, inferred, and unknown records require analysis provenance.");
            return;
        }

        foreach (var (property, value) in new[]
                 {
                     ("analyzer", analysis.Analyzer),
                     ("analyzerVersion", analysis.AnalyzerVersion),
                     ("method", analysis.Method)
                 })
        {
            if (IsBlank(value))
            {
                Add(
                    issues,
                    "analysis-provenance-invalid",
                    $"#/analysis/{property}",
                    $"{property} must be a nonempty string.");
            }
        }

        if (related.Count == 0)
        {
            Add(
                issues,
                "analysis-evidence-missing",
                "#/relatedEvidenceIds",
                "Derived, inferred, and unknown records must cite supporting evidence.");
        }

        var competing = analysis.CompetingInterpretations;
        if (competing is null ||
            competing.Any(IsBlank) ||
            competing.Distinct(StringComparer.Ordinal).Count() != competing.Count)
        {
            Add(
                issues,
                "competing-interpretations-invalid",
                "#/analysis/competingInterpretations",
                "Competing interpretations must be an array of unique, nonempty strings.");
        }

        if (evidenceClass == EvidenceClasses.Inferred &&
            analysis.ConfidenceCategory is not ("low" or "medium" or "high"))
        {
            Add(
                issues,
                "inference-confidence-invalid",
                "#/analysis/confidenceCategory",
                "Inferred records require low, medium, or high confidence.");
        }
        else if (evidenceClass != EvidenceClasses.Inferred &&
                 analysis.ConfidenceCategory is not null)
        {
            Add(
                issues,
                "analysis-confidence-not-applicable",
                "#/analysis/confidenceCategory",
                "Only inferred records carry a confidence category.");
        }

        if (evidenceClass == EvidenceClasses.Unknown &&
            IsBlank(analysis.InsufficientEvidenceReason))
        {
            Add(
                issues,
                "unknown-reason-missing",
                "#/analysis/insufficientEvidenceReason",
                "Unknown records require an insufficient-evidence reason.");
        }
        else if (evidenceClass != EvidenceClasses.Unknown &&
                 analysis.InsufficientEvidenceReason is not null)
        {
            Add(
                issues,
                "unknown-reason-not-applicable",
                "#/analysis/insufficientEvidenceReason",
                "Only unknown records carry an insufficient-evidence reason.");
        }
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static void Add(
        List<EventValidationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(new EventValidationIssue(code, path, message));
}
