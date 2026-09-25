using System.Security.Cryptography;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

public enum ArchiveValidationSeverity
{
    Error,
    Warning
}

public sealed record ArchiveValidationIssue(
    string Code,
    ArchiveValidationSeverity Severity,
    string Path,
    string Message,
    long? Line = null);

public sealed record ArchiveValidationResult(
    string ValidatorVersion,
    DateTimeOffset ValidatedUtc,
    bool IsValid,
    string? SessionId,
    long EventsValidated,
    long ArtifactsValidated,
    IReadOnlyList<ArchiveValidationIssue> Issues,
    bool ArtifactHashesVerified = true);

/// <summary>
/// Controls how much of an archive the validator rereads.
/// </summary>
/// <param name="VerifyArtifactHashes">
/// When true, every declared artifact is reread and its SHA-256 compared with
/// the manifest. When false, the validator still checks each artifact's path,
/// presence, size, and hash format, but does not reread the file to recompute
/// its hash. The recorder uses false only at finalization, where it computed
/// the manifest hashes from the same files moments earlier.
/// </param>
public sealed record ArchiveValidationOptions(bool VerifyArtifactHashes = true)
{
    public static ArchiveValidationOptions Default { get; } = new();
}

public static class SessionArchiveValidator
{
    public const string ValidatorVersion = "1.3";
    public const string ReportRelativePath = "diagnostics/archive-validation.json";

    private static readonly string[] ManifestProperties =
    [
        "schemaVersion",
        "sessionId",
        "status",
        "startedUtc",
        "endedUtc",
        "durationNanoseconds",
        "clockFrequency",
        "clockOriginTimestamp",
        "operatingSystem",
        "runtime",
        "architecture",
        "configuration",
        "collectors",
        "artifacts",
        "acceptedEventCount",
        "droppedEventCount",
        "failure"
    ];

    private static readonly string[] EventProperties =
    [
        "schemaVersion",
        "sessionId",
        "collectorType",
        "collectorInstanceId",
        "channel",
        "captureMethod",
        "sequence",
        "monotonicNanoseconds",
        "observedUtc",
        "eventType",
        "payload",
        "qualityFlags"
    ];

    private static readonly string[] EventVersion11Properties =
    [
        "eventId",
        "evidenceClass",
        "producerVersion",
        "clockMappingId",
        "nativeTimestamp",
        "timestampUncertaintyNanoseconds",
        "relatedEvidenceIds",
        "analysis"
    ];

    public static Task<ArchiveValidationResult> ValidateAsync(
        string sessionDirectory,
        CancellationToken cancellationToken = default) =>
        ValidateAsync(
            sessionDirectory,
            ArchiveValidationOptions.Default,
            cancellationToken);

    public static async Task<ArchiveValidationResult> ValidateAsync(
        string sessionDirectory,
        ArchiveValidationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentNullException.ThrowIfNull(options);

        var issues = new List<ArchiveValidationIssue>();
        var rootPath = Path.GetFullPath(sessionDirectory);
        if (!Directory.Exists(rootPath))
        {
            AddError(issues, "archive-directory-missing", ".", "Session directory does not exist.");
            return CreateResult(null, 0, 0, issues);
        }

        var manifestPath = Path.Combine(rootPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            AddError(issues, "manifest-missing", "manifest.json", "Session manifest is missing.");
            return CreateResult(null, 0, 0, issues);
        }

        JsonDocument manifestDocument;
        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);
            manifestDocument = await JsonDocument.ParseAsync(
                manifestStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            AddError(
                issues,
                "manifest-unreadable",
                "manifest.json",
                $"Session manifest could not be read: {exception.Message}");
            return CreateResult(null, 0, 0, issues);
        }

        using (manifestDocument)
        {
            var manifest = manifestDocument.RootElement;
            if (manifest.ValueKind != JsonValueKind.Object)
            {
                AddError(issues, "manifest-not-object", "manifest.json", "Manifest root must be an object.");
                return CreateResult(null, 0, 0, issues);
            }

            RequireProperties(manifest, ManifestProperties, "manifest.json", issues);
            var schemaVersion = ReadString(manifest, "schemaVersion");
            if (schemaVersion != SessionSchemaVersions.Manifest)
            {
                AddError(
                    issues,
                    "manifest-version-unsupported",
                    "manifest.json#/schemaVersion",
                    $"Expected manifest schema {SessionSchemaVersions.Manifest}, found " +
                    $"{schemaVersion ?? "null"}.");
            }

            var sessionId = ReadString(manifest, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                AddError(
                    issues,
                    "session-id-invalid",
                    "manifest.json#/sessionId",
                    "Session ID must be a nonempty string.");
            }

            ValidateTerminalState(manifest, rootPath, issues);

            var acceptedEventCount = ReadInt64(manifest, "acceptedEventCount");
            if (acceptedEventCount is null or < 0)
            {
                AddError(
                    issues,
                    "accepted-event-count-invalid",
                    "manifest.json#/acceptedEventCount",
                    "Accepted event count must be a nonnegative integer.");
            }

            var artifactResult = await ValidateArtifactsAsync(
                manifest,
                rootPath,
                options.VerifyArtifactHashes,
                issues,
                cancellationToken).ConfigureAwait(false);
            var eventCount = await ValidateEventsAsync(
                rootPath,
                sessionId,
                issues,
                cancellationToken).ConfigureAwait(false);
            if (acceptedEventCount is not null && acceptedEventCount != eventCount)
            {
                AddError(
                    issues,
                    "event-count-mismatch",
                    "events.ndjson",
                    $"Manifest declares {acceptedEventCount} accepted events, but " +
                    $"{eventCount} records were found.");
            }

            return CreateResult(
                sessionId,
                eventCount,
                artifactResult,
                issues,
                options.VerifyArtifactHashes);
        }
    }

    public static async Task WriteReportAsync(
        string sessionDirectory,
        ArchiveValidationResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentNullException.ThrowIfNull(result);

        var reportPath = Path.Combine(
            Path.GetFullPath(sessionDirectory),
            ReportRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var temporaryPath = $"{reportPath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.Read,
                         4096,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                result,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                },
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, reportPath, overwrite: true);
    }

    private static void ValidateTerminalState(
        JsonElement manifest,
        string rootPath,
        ICollection<ArchiveValidationIssue> issues)
    {
        var status = ReadString(manifest, "status");
        if (status is not ("starting" or "recording" or "completed" or "failed"))
        {
            AddError(
                issues,
                "status-invalid",
                "manifest.json#/status",
                "Status must be starting, recording, completed, or failed.");
            return;
        }

        if (status is "completed" or "failed")
        {
            if (!ReadDateTimeOffset(manifest, "endedUtc").HasValue)
            {
                AddError(
                    issues,
                    "ended-time-invalid",
                    "manifest.json#/endedUtc",
                    "A terminal session requires a valid endedUtc value.");
            }

            if (ReadInt64(manifest, "durationNanoseconds") is not { } duration ||
                duration < 0)
            {
                AddError(
                    issues,
                    "duration-invalid",
                    "manifest.json#/durationNanoseconds",
                    "A terminal session requires a nonnegative duration.");
            }

            if (File.Exists(Path.Combine(rootPath, ".recording")))
            {
                AddError(
                    issues,
                    "incomplete-marker-present",
                    ".recording",
                    "A terminal session must not retain the recording marker.");
            }
        }

        var failure = ReadString(manifest, "failure");
        if (status == "completed" && failure is not null)
        {
            AddError(
                issues,
                "completed-session-has-failure",
                "manifest.json#/failure",
                "A completed session cannot contain a failure.");
        }

        if (status == "failed" && string.IsNullOrWhiteSpace(failure))
        {
            AddError(
                issues,
                "failed-session-missing-failure",
                "manifest.json#/failure",
                "A failed session must explain its failure.");
        }
    }

    private static async Task<long> ValidateArtifactsAsync(
        JsonElement manifest,
        string rootPath,
        bool verifyHashes,
        ICollection<ArchiveValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        if (!manifest.TryGetProperty("artifacts", out var artifacts) ||
            artifacts.ValueKind != JsonValueKind.Array)
        {
            AddError(
                issues,
                "artifacts-invalid",
                "manifest.json#/artifacts",
                "Artifacts must be an array.");
            return 0;
        }

        var declaredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long validatedCount = 0;
        foreach (var artifact in artifacts.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (artifact.ValueKind != JsonValueKind.Object)
            {
                AddError(
                    issues,
                    "artifact-invalid",
                    "manifest.json#/artifacts",
                    "Each artifact must be an object.");
                continue;
            }

            RequireProperties(
                artifact,
                ["path", "sizeBytes", "sha256"],
                "manifest.json#/artifacts",
                issues);
            var relativePath = ReadString(artifact, "path");
            if (!TryResolveArtifactPath(rootPath, relativePath, out var absolutePath))
            {
                AddError(
                    issues,
                    "artifact-path-unsafe",
                    "manifest.json#/artifacts/path",
                    $"Artifact path '{relativePath ?? "null"}' is not a safe canonical relative path.");
                continue;
            }

            if (!declaredPaths.Add(relativePath!))
            {
                AddError(
                    issues,
                    "artifact-path-duplicate",
                    relativePath!,
                    "Artifact path is declared more than once.");
                continue;
            }

            if (!File.Exists(absolutePath))
            {
                AddError(issues, "artifact-missing", relativePath!, "Declared artifact is missing.");
                continue;
            }

            var expectedSize = ReadInt64(artifact, "sizeBytes");
            var actualSize = new FileInfo(absolutePath).Length;
            if (expectedSize != actualSize)
            {
                AddError(
                    issues,
                    "artifact-size-mismatch",
                    relativePath!,
                    $"Manifest size is {expectedSize?.ToString() ?? "invalid"} bytes; " +
                    $"file size is {actualSize} bytes.");
            }

            var expectedHash = ReadString(artifact, "sha256");
            if (!verifyHashes)
            {
                if (!IsSha256Hex(expectedHash))
                {
                    AddError(
                        issues,
                        "artifact-hash-invalid",
                        relativePath!,
                        "Artifact SHA-256 must be 64 lowercase hexadecimal characters.");
                }

                validatedCount++;
                continue;
            }

            await using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            var actualHash = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
            {
                AddError(
                    issues,
                    "artifact-hash-mismatch",
                    relativePath!,
                    "Artifact SHA-256 does not match the manifest.");
            }

            validatedCount++;
        }

        if (!declaredPaths.Contains("events.ndjson"))
        {
            AddError(
                issues,
                "event-log-not-declared",
                "manifest.json#/artifacts",
                "The event log must be declared as an artifact.");
        }

        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
            if (IsArchiveMetadata(relativePath) || declaredPaths.Contains(relativePath))
            {
                continue;
            }

            AddError(
                issues,
                "artifact-not-declared",
                relativePath,
                "File exists in the archive but is not declared by the manifest.");
        }

        return validatedCount;
    }

    private static async Task<long> ValidateEventsAsync(
        string rootPath,
        string? expectedSessionId,
        ICollection<ArchiveValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var eventPath = Path.Combine(rootPath, "events.ndjson");
        if (!File.Exists(eventPath))
        {
            AddError(issues, "event-log-missing", "events.ndjson", "Event log is missing.");
            return 0;
        }

        var sequenceStates = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var timestampStates = new Dictionary<string, long>(StringComparer.Ordinal);
        var eventIds = new Dictionary<string, long>(StringComparer.Ordinal);
        var references = new List<EventReference>();
        long lineNumber = 0;
        long eventCount = 0;
        using var reader = new StreamReader(eventPath);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                AddError(
                    issues,
                    "event-line-empty",
                    "events.ndjson",
                    "Blank lines are not allowed in the event stream.",
                    lineNumber);
                continue;
            }

            JsonDocument eventDocument;
            try
            {
                eventDocument = JsonDocument.Parse(line);
            }
            catch (JsonException exception)
            {
                AddError(
                    issues,
                    "event-json-invalid",
                    "events.ndjson",
                    $"Event is not valid JSON: {exception.Message}",
                    lineNumber);
                continue;
            }

            using (eventDocument)
            {
                eventCount++;
                var record = eventDocument.RootElement;
                if (record.ValueKind != JsonValueKind.Object)
                {
                    AddError(
                        issues,
                        "event-not-object",
                        "events.ndjson",
                        "Event must be a JSON object.",
                        lineNumber);
                    continue;
                }

                RequireProperties(record, EventProperties, "events.ndjson", issues, lineNumber);
                var schemaVersion = ReadString(record, "schemaVersion");
                if (!SessionSchemaVersions.IsSupportedEvent(schemaVersion))
                {
                    AddError(
                        issues,
                        "event-version-unsupported",
                        "events.ndjson#/schemaVersion",
                        $"Supported event schemas are {SessionSchemaVersions.LegacyEvent} and " +
                        $"{SessionSchemaVersions.Event}; found " +
                        $"{schemaVersion ?? "null"}.",
                        lineNumber);
                }
                else if (schemaVersion == SessionSchemaVersions.Event)
                {
                    RequireProperties(
                        record,
                        EventVersion11Properties,
                        "events.ndjson",
                        issues,
                        lineNumber);
                    ValidateVersion11Event(
                        record,
                        eventIds,
                        references,
                        issues,
                        lineNumber);
                }

                var eventSessionId = ReadString(record, "sessionId");
                if (!string.Equals(eventSessionId, expectedSessionId, StringComparison.Ordinal))
                {
                    AddError(
                        issues,
                        "event-session-mismatch",
                        "events.ndjson#/sessionId",
                        "Event session ID does not match the manifest.",
                        lineNumber);
                }

                ValidateEventOrdering(
                    record,
                    sequenceStates,
                    timestampStates,
                    issues,
                    lineNumber);
                ValidateEventFields(record, issues, lineNumber);
                EventPayloadValidator.Validate(record, issues, lineNumber);
            }
        }

        foreach (var reference in references)
        {
            if (!eventIds.ContainsKey(reference.TargetEventId))
            {
                AddError(
                    issues,
                    "related-evidence-not-found",
                    "events.ndjson#/relatedEvidenceIds",
                    $"Related evidence event '{reference.TargetEventId}' was not found.",
                    reference.Line);
            }
        }

        return eventCount;
    }

    private static void ValidateVersion11Event(
        JsonElement record,
        IDictionary<string, long> eventIds,
        ICollection<EventReference> references,
        ICollection<ArchiveValidationIssue> issues,
        long lineNumber)
    {
        var eventId = ReadString(record, "eventId");
        if (string.IsNullOrWhiteSpace(eventId))
        {
            AddError(
                issues,
                "event-id-invalid",
                "events.ndjson#/eventId",
                "Event ID must be a nonempty string.",
                lineNumber);
        }
        else if (!eventIds.TryAdd(eventId, lineNumber))
        {
            AddError(
                issues,
                "event-id-duplicate",
                "events.ndjson#/eventId",
                $"Event ID '{eventId}' was already used on line {eventIds[eventId]}.",
                lineNumber);
        }

        foreach (var property in new[] { "producerVersion", "clockMappingId" })
        {
            if (string.IsNullOrWhiteSpace(ReadString(record, property)))
            {
                AddError(
                    issues,
                    "event-provenance-invalid",
                    $"events.ndjson#/{property}",
                    $"{property} must be a nonempty string.",
                    lineNumber);
            }
        }

        var evidenceClass = ReadString(record, "evidenceClass");
        if (evidenceClass is not (
            EvidenceClasses.Observed or
            EvidenceClasses.Derived or
            EvidenceClasses.Inferred or
            EvidenceClasses.Unknown))
        {
            AddError(
                issues,
                "evidence-class-invalid",
                "events.ndjson#/evidenceClass",
                "Evidence class must be observed, derived, inferred, or unknown.",
                lineNumber);
        }

        ValidateNativeTimestamp(record, issues, lineNumber);
        if (record.TryGetProperty("timestampUncertaintyNanoseconds", out var uncertainty) &&
            uncertainty.ValueKind != JsonValueKind.Null &&
            (uncertainty.ValueKind != JsonValueKind.Number ||
             !uncertainty.TryGetInt64(out var uncertaintyValue) ||
             uncertaintyValue < 0))
        {
            AddError(
                issues,
                "timestamp-uncertainty-invalid",
                "events.ndjson#/timestampUncertaintyNanoseconds",
                "Timestamp uncertainty must be a nonnegative integer or null.",
                lineNumber);
        }

        var relatedEvidenceIds = ReadStringArray(
            record,
            "relatedEvidenceIds",
            issues,
            lineNumber);
        if (relatedEvidenceIds is not null)
        {
            foreach (var relatedEventId in relatedEvidenceIds)
            {
                if (string.Equals(eventId, relatedEventId, StringComparison.Ordinal))
                {
                    AddError(
                        issues,
                        "related-evidence-self-reference",
                        "events.ndjson#/relatedEvidenceIds",
                        "An event cannot cite itself as related evidence.",
                        lineNumber);
                }

                references.Add(new EventReference(relatedEventId, lineNumber));
            }
        }

        ValidateAnalysis(
            record,
            evidenceClass,
            relatedEvidenceIds ?? [],
            issues,
            lineNumber);
    }

    private static void ValidateNativeTimestamp(
        JsonElement record,
        ICollection<ArchiveValidationIssue> issues,
        long lineNumber)
    {
        if (!record.TryGetProperty("nativeTimestamp", out var nativeTimestamp) ||
            nativeTimestamp.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (nativeTimestamp.ValueKind != JsonValueKind.Object)
        {
            AddError(
                issues,
                "native-timestamp-invalid",
                "events.ndjson#/nativeTimestamp",
                "Native timestamp must be an object or null.",
                lineNumber);
            return;
        }

        RequireProperties(
            nativeTimestamp,
            ["domain", "value", "unit"],
            "events.ndjson#/nativeTimestamp",
            issues,
            lineNumber);
        foreach (var property in new[] { "domain", "unit" })
        {
            if (string.IsNullOrWhiteSpace(ReadString(nativeTimestamp, property)))
            {
                AddError(
                    issues,
                    "native-timestamp-invalid",
                    $"events.ndjson#/nativeTimestamp/{property}",
                    $"{property} must be a nonempty string.",
                    lineNumber);
            }
        }

        if (ReadInt64(nativeTimestamp, "value") is null)
        {
            AddError(
                issues,
                "native-timestamp-invalid",
                "events.ndjson#/nativeTimestamp/value",
                "Native timestamp value must be an integer.",
                lineNumber);
        }
    }

    private static IReadOnlyList<string>? ReadStringArray(
        JsonElement record,
        string property,
        ICollection<ArchiveValidationIssue> issues,
        long lineNumber)
    {
        if (!record.TryGetProperty(property, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            AddError(
                issues,
                "related-evidence-invalid",
                $"events.ndjson#/{property}",
                $"{property} must be an array of unique, nonempty strings.",
                lineNumber);
            return null;
        }

        var values = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(value) || !unique.Add(value))
            {
                AddError(
                    issues,
                    "related-evidence-invalid",
                    $"events.ndjson#/{property}",
                    $"{property} must be an array of unique, nonempty strings.",
                    lineNumber);
                return null;
            }

            values.Add(value);
        }

        return values;
    }

    private static void ValidateAnalysis(
        JsonElement record,
        string? evidenceClass,
        IReadOnlyList<string> relatedEvidenceIds,
        ICollection<ArchiveValidationIssue> issues,
        long lineNumber)
    {
        if (!record.TryGetProperty("analysis", out var analysis))
        {
            return;
        }

        if (evidenceClass == EvidenceClasses.Observed)
        {
            if (relatedEvidenceIds.Count != 0)
            {
                AddError(
                    issues,
                    "observed-event-cites-evidence",
                    "events.ndjson#/relatedEvidenceIds",
                    "Observed evidence cannot cite supporting evidence.",
                    lineNumber);
            }

            if (analysis.ValueKind != JsonValueKind.Null)
            {
                AddError(
                    issues,
                    "observed-event-has-analysis",
                    "events.ndjson#/analysis",
                    "Observed evidence must not contain analysis provenance.",
                    lineNumber);
            }

            return;
        }

        if (evidenceClass is not (
                EvidenceClasses.Derived or
                EvidenceClasses.Inferred or
                EvidenceClasses.Unknown))
        {
            return;
        }

        if (analysis.ValueKind != JsonValueKind.Object)
        {
            AddError(
                issues,
                "analysis-provenance-missing",
                "events.ndjson#/analysis",
                "Derived, inferred, and unknown records require analysis provenance.",
                lineNumber);
            return;
        }

        RequireProperties(
            analysis,
            [
                "analyzer",
                "analyzerVersion",
                "method",
                "confidenceCategory",
                "competingInterpretations",
                "insufficientEvidenceReason"
            ],
            "events.ndjson#/analysis",
            issues,
            lineNumber);
        foreach (var property in new[] { "analyzer", "analyzerVersion", "method" })
        {
            if (string.IsNullOrWhiteSpace(ReadString(analysis, property)))
            {
                AddError(
                    issues,
                    "analysis-provenance-invalid",
                    $"events.ndjson#/analysis/{property}",
                    $"{property} must be a nonempty string.",
                    lineNumber);
            }
        }

        if (relatedEvidenceIds.Count == 0)
        {
            AddError(
                issues,
                "analysis-evidence-missing",
                "events.ndjson#/relatedEvidenceIds",
                "Derived, inferred, and unknown records must cite supporting evidence.",
                lineNumber);
        }

        if (!analysis.TryGetProperty("competingInterpretations", out var competing) ||
            competing.ValueKind != JsonValueKind.Array)
        {
            AddError(
                issues,
                "competing-interpretations-invalid",
                "events.ndjson#/analysis/competingInterpretations",
                "Competing interpretations must be an array of nonempty strings.",
                lineNumber);
        }
        else
        {
            var interpretations = new HashSet<string>(StringComparer.Ordinal);
            if (competing.EnumerateArray().Any(item =>
                    item.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(item.GetString()) ||
                    !interpretations.Add(item.GetString()!)))
            {
                AddError(
                    issues,
                    "competing-interpretations-invalid",
                    "events.ndjson#/analysis/competingInterpretations",
                    "Competing interpretations must be an array of unique, nonempty strings.",
                    lineNumber);
            }
        }

        var confidence = ReadString(analysis, "confidenceCategory");
        if (evidenceClass == EvidenceClasses.Inferred &&
            confidence is not ("low" or "medium" or "high"))
        {
            AddError(
                issues,
                "inference-confidence-invalid",
                "events.ndjson#/analysis/confidenceCategory",
                "Inferred records require low, medium, or high confidence.",
                lineNumber);
        }
        else if (evidenceClass != EvidenceClasses.Inferred &&
                 analysis.TryGetProperty("confidenceCategory", out var confidenceElement) &&
                 confidenceElement.ValueKind != JsonValueKind.Null)
        {
            AddError(
                issues,
                "analysis-confidence-not-applicable",
                "events.ndjson#/analysis/confidenceCategory",
                "Only inferred records carry a confidence category.",
                lineNumber);
        }

        var reason = ReadString(analysis, "insufficientEvidenceReason");
        if (evidenceClass == EvidenceClasses.Unknown &&
            string.IsNullOrWhiteSpace(reason))
        {
            AddError(
                issues,
                "unknown-reason-missing",
                "events.ndjson#/analysis/insufficientEvidenceReason",
                "Unknown records require an insufficient-evidence reason.",
                lineNumber);
        }
        else if (evidenceClass != EvidenceClasses.Unknown &&
                 analysis.TryGetProperty(
                     "insufficientEvidenceReason",
                     out var insufficientEvidenceReason) &&
                 insufficientEvidenceReason.ValueKind != JsonValueKind.Null)
        {
            AddError(
                issues,
                "unknown-reason-not-applicable",
                "events.ndjson#/analysis/insufficientEvidenceReason",
                "Only unknown records carry an insufficient-evidence reason.",
                lineNumber);
        }
    }

    private static void ValidateEventOrdering(
        JsonElement record,
        IDictionary<string, ulong> sequenceStates,
        IDictionary<string, long> timestampStates,
        ICollection<ArchiveValidationIssue> issues,
        long lineNumber)
    {
        var collectorInstanceId = ReadString(record, "collectorInstanceId");
        var channel = ReadString(record, "channel");
        var sequence = ReadUInt64(record, "sequence");
        var timestamp = ReadInt64(record, "monotonicNanoseconds");
        var clockMappingId = ReadString(record, "clockMappingId");
        if (string.IsNullOrWhiteSpace(collectorInstanceId) ||
            string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        var streamKey = $"{collectorInstanceId}\u001f{channel}";
        if (sequence is not null)
        {
            if (sequenceStates.TryGetValue(streamKey, out var previousSequence) &&
                sequence <= previousSequence)
            {
                AddError(
                    issues,
                    "event-sequence-not-increasing",
                    "events.ndjson#/sequence",
                    $"Sequence {sequence} does not follow {previousSequence} for this stream.",
                    lineNumber);
            }

            sequenceStates[streamKey] = sequence.Value;
        }

        if (timestamp is not null && !string.IsNullOrWhiteSpace(clockMappingId))
        {
            var timestampKey = $"{streamKey}\u001f{clockMappingId}";
            if (timestampStates.TryGetValue(
                    timestampKey,
                    out var previousTimestamp) &&
                timestamp < previousTimestamp)
            {
                AddError(
                    issues,
                    "event-time-regressed",
                    "events.ndjson#/monotonicNanoseconds",
                    "Monotonic time regressed within a collector channel and clock mapping.",
                    lineNumber);
            }

            timestampStates[timestampKey] = timestamp.Value;
        }
    }

    private static void ValidateEventFields(
        JsonElement record,
        ICollection<ArchiveValidationIssue> issues,
        long lineNumber)
    {
        foreach (var name in new[]
                 {
                     "sessionId",
                     "collectorType",
                     "collectorInstanceId",
                     "channel",
                     "captureMethod",
                     "eventType"
                 })
        {
            if (string.IsNullOrWhiteSpace(ReadString(record, name)))
            {
                AddError(
                    issues,
                    "event-string-invalid",
                    $"events.ndjson#/{name}",
                    $"{name} must be a nonempty string.",
                    lineNumber);
            }
        }

        if (ReadUInt64(record, "sequence") is null)
        {
            AddError(
                issues,
                "event-sequence-invalid",
                "events.ndjson#/sequence",
                "Sequence must be a nonnegative integer.",
                lineNumber);
        }

        if (ReadInt64(record, "monotonicNanoseconds") is not { } timestamp ||
            timestamp < 0)
        {
            AddError(
                issues,
                "event-time-invalid",
                "events.ndjson#/monotonicNanoseconds",
                "Monotonic time must be a nonnegative integer.",
                lineNumber);
        }

        if (!ReadDateTimeOffset(record, "observedUtc").HasValue)
        {
            AddError(
                issues,
                "event-observed-time-invalid",
                "events.ndjson#/observedUtc",
                "Observed UTC must be a valid date-time string.",
                lineNumber);
        }

        if (!record.TryGetProperty("qualityFlags", out var flags) ||
            flags.ValueKind != JsonValueKind.Array ||
            flags.EnumerateArray().Any(flag =>
                flag.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(flag.GetString())))
        {
            AddError(
                issues,
                "event-quality-flags-invalid",
                "events.ndjson#/qualityFlags",
                "Quality flags must be an array of nonempty strings.",
                lineNumber);
        }
    }

    private static bool TryResolveArtifactPath(
        string rootPath,
        string? relativePath,
        out string absolutePath)
    {
        absolutePath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\') ||
            relativePath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return false;
        }

        absolutePath = Path.GetFullPath(
            Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        return absolutePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsArchiveMetadata(string relativePath) =>
        string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(relativePath, ".recording", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(relativePath, ReportRelativePath, StringComparison.OrdinalIgnoreCase);

    private static void RequireProperties(
        JsonElement value,
        IEnumerable<string> properties,
        string path,
        ICollection<ArchiveValidationIssue> issues,
        long? line = null)
    {
        foreach (var property in properties)
        {
            if (!value.TryGetProperty(property, out _))
            {
                AddError(
                    issues,
                    "required-property-missing",
                    $"{path}#/{property}",
                    $"Required property '{property}' is missing.",
                    line);
            }
        }
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static long? ReadInt64(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetInt64(out var result)
            ? result
            : null;

    private static ulong? ReadUInt64(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetUInt64(out var result)
            ? result
            : null;

    private static DateTimeOffset? ReadDateTimeOffset(
        JsonElement value,
        string property) =>
        value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String &&
        item.TryGetDateTimeOffset(out var result)
            ? result
            : null;

    private static ArchiveValidationResult CreateResult(
        string? sessionId,
        long eventsValidated,
        long artifactsValidated,
        IReadOnlyList<ArchiveValidationIssue> issues,
        bool artifactHashesVerified = true) =>
        new(
            ValidatorVersion,
            DateTimeOffset.UtcNow,
            issues.All(issue => issue.Severity != ArchiveValidationSeverity.Error),
            sessionId,
            eventsValidated,
            artifactsValidated,
            issues,
            artifactHashesVerified);

    private static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void AddError(
        ICollection<ArchiveValidationIssue> issues,
        string code,
        string path,
        string message,
        long? line = null) =>
        issues.Add(new ArchiveValidationIssue(
            code,
            ArchiveValidationSeverity.Error,
            path,
            message,
            line));

    private sealed record EventReference(string TargetEventId, long Line);
}
