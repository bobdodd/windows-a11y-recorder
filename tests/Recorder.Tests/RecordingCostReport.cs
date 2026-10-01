using System.Globalization;
using System.Text;
using Recorder.Database.RecordingFiles;

namespace Recorder.Tests;

// A diagnostic, run only when RECORDER_COST_FILE names a recording file:
// reports the bytes the file holds for each channel, over the recording and
// per minute, before chunk compression, and on browser.layout the bytes of each record type and of an
// average change set, so two recordings of the same pages can be compared.
// The report is written to RECORDER_COST_REPORT, or to cost-report.txt in
// the temporary directory.
public sealed class RecordingCostReport
{
    [Fact]
    public async Task ReportsTheBytesOfARecordingFile()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_COST_FILE") is not { } source)
        {
            return;
        }

        var channels = new SortedDictionary<string, (long Count, long Bytes)>(StringComparer.Ordinal);
        var layoutTypes = new SortedDictionary<string, (long Count, long Bytes)>(StringComparer.Ordinal);
        long changeSetBytes = 0;
        var styleKinds = new SortedDictionary<string, StyleTotals>(StringComparer.Ordinal);
        var listedProperties = new SortedSet<int>();
        var fragmentTotals = new SortedDictionary<string, (long Records, long Bytes, long Fragments, long Children)>(StringComparer.Ordinal);
        var urls = new SortedSet<string>(StringComparer.Ordinal);
        long first = long.MaxValue;
        long last = long.MinValue;
        using (var reader = RecordingFileReader.Open(source))
        {
            foreach (var message in reader.ReadAll())
            {
                // Some writer records are stamped 0; they do not bound the
                // recording's time.
                if (message.LogTime > 0)
                {
                    first = Math.Min(first, message.LogTime);
                    last = Math.Max(last, message.LogTime);
                }
                var bytes = message.Data.Length;
                var topic = message.Channel.Topic;
                channels[topic] = Add(channels.GetValueOrDefault(topic), bytes);
                if (topic == "browser.navigation")
                {
                    var navigation = RecordingEventCodec.Decode(message.Data.Span).Event.Payload;
                    if (navigation.TryGetProperty("url", out var url) && url.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        urls.Add(url.GetString()!);
                    }
                }
                if (topic != "browser.layout")
                {
                    continue;
                }
                var record = RecordingEventCodec.Decode(message.Data.Span);
                var type = record.Event.EventType;
                layoutTypes[type] = Add(layoutTypes.GetValueOrDefault(type), bytes);
                if (record.Event.Payload.TryGetProperty("changeSetId", out _))
                {
                    changeSetBytes += bytes;
                }
                var payload = record.Event.Payload;
                // Protocol 0.38: the JSON text of the box fragments, and the
                // fragments and child links they hold, in node records.
                if (payload.TryGetProperty("boxFragments", out var boxFragments) &&
                    boxFragments.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var (fragmentCount, childCount) = CountFragments(boxFragments.GetProperty("fragments"));
                    fragmentTotals[type] = (
                        fragmentTotals.GetValueOrDefault(type).Records + 1,
                        fragmentTotals.GetValueOrDefault(type).Bytes + boxFragments.GetRawText().Length,
                        fragmentTotals.GetValueOrDefault(type).Fragments + fragmentCount,
                        fragmentTotals.GetValueOrDefault(type).Children + childCount);
                }
                if (type == "layout-node-changed")
                {
                    // Protocol 0.37 states whether the style is whole; an
                    // earlier record has no such field.
                    var kind = payload.TryGetProperty("computedStyleComplete", out var complete)
                        ? complete.ValueKind.ToString().ToLowerInvariant()
                        : "absent";
                    styleKinds[kind] = AddStyle(styleKinds.GetValueOrDefault(kind), bytes, payload);
                }
                else if (type == "layout-checkpoint-node")
                {
                    styleKinds["checkpoint"] = AddStyle(styleKinds.GetValueOrDefault("checkpoint"), bytes, payload);
                }
                else if (type == "layout-checkpoint-started" &&
                    payload.TryGetProperty("styleProperties", out var listed))
                {
                    listedProperties.Add(listed.GetArrayLength());
                }
            }
        }

        // Log times are in nanoseconds.
        var minutes = first != long.MaxValue && last > first ? (last - first) / 60e9 : 0;
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"file: {Path.GetFileName(source)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"bytes on disk: {new FileInfo(new FileInfo(source).ResolveLinkTarget(true)?.FullName ?? source).Length}");
        report.AppendLine(CultureInfo.InvariantCulture, $"minutes between first and last message: {minutes:F2}");
        report.AppendLine("bytes below are of the records before chunk compression");
        report.AppendLine("channel: messages, bytes, bytes per minute");
        foreach (var (topic, (count, bytes)) in channels)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {topic}: {count}, {bytes}, {(minutes > 0 ? bytes / minutes : 0):F0}");
        }
        report.AppendLine("browser.layout record type: records, bytes, bytes per record");
        foreach (var (type, (count, bytes)) in layoutTypes)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {type}: {count}, {bytes}, {(double)bytes / count:F0}");
        }
        var changeSets = layoutTypes.GetValueOrDefault("layout-changes-started").Count;
        report.AppendLine(CultureInfo.InvariantCulture,
            $"change sets: {changeSets}, bytes of their records {changeSetBytes}, bytes per change set {(changeSets > 0 ? (double)changeSetBytes / changeSets : 0):F0}");

        report.AppendLine("navigation addresses:");
        foreach (var url in urls)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {url}");
        }
        report.AppendLine(CultureInfo.InvariantCulture,
            $"properties listed by checkpoint starts: {string.Join(", ", listedProperties)}");
        report.AppendLine("node records by computedStyleComplete: records, bytes per record, style values per styled record, custom properties per styled record");
        foreach (var (kind, totals) in styleKinds)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {kind}: {totals.Count}, {(double)totals.Bytes / totals.Count:F0}, {(totals.Styled > 0 ? (double)totals.Values / totals.Styled : 0):F1}, {(totals.Styled > 0 ? (double)totals.Custom / totals.Styled : 0):F1}");
        }

        report.AppendLine("box fragments by record type: records with them, characters of their JSON, fragments, child links");
        foreach (var (type, totals) in fragmentTotals)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {type}: {totals.Records}, {totals.Bytes}, {totals.Fragments}, {totals.Children}");
        }

        var reportPath = Environment.GetEnvironmentVariable("RECORDER_COST_REPORT") ??
            Path.Combine(Path.GetTempPath(), "cost-report.txt");
        await File.WriteAllTextAsync(reportPath, report.ToString(), TestContext.Current.CancellationToken);
    }

    // The fragments and child links in a list of fragments, nested ones
    // included.
    private static (long Fragments, long Children) CountFragments(System.Text.Json.JsonElement fragments)
    {
        long fragmentCount = 0;
        long childCount = 0;
        foreach (var fragment in fragments.EnumerateArray())
        {
            var (nestedFragments, nestedChildren) = CountFragment(fragment);
            fragmentCount += nestedFragments;
            childCount += nestedChildren;
        }
        return (fragmentCount, childCount);
    }

    private static (long Fragments, long Children) CountFragment(System.Text.Json.JsonElement fragment)
    {
        long fragmentCount = 1;
        long childCount = 0;
        foreach (var child in fragment.GetProperty("children").EnumerateArray())
        {
            childCount++;
            if (child.TryGetProperty("fragment", out var nested) &&
                nested.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var (nestedFragments, nestedChildren) = CountFragment(nested);
                fragmentCount += nestedFragments;
                childCount += nestedChildren;
            }
        }
        return (fragmentCount, childCount);
    }

    private sealed record StyleTotals(long Count, long Bytes, long Styled, long Values, long Custom);

    private static StyleTotals AddStyle(StyleTotals? totals, long bytes, System.Text.Json.JsonElement payload)
    {
        totals ??= new StyleTotals(0, 0, 0, 0, 0);
        var styled = payload.TryGetProperty("computedStyle", out var style) &&
            style.ValueKind == System.Text.Json.JsonValueKind.Object;
        var values = styled ? style.EnumerateObject().Count() : 0;
        var custom = payload.TryGetProperty("customProperties", out var properties) &&
            properties.ValueKind == System.Text.Json.JsonValueKind.Object
                ? properties.EnumerateObject().Count()
                : 0;
        return new StyleTotals(
            totals.Count + 1,
            totals.Bytes + bytes,
            totals.Styled + (styled ? 1 : 0),
            totals.Values + values,
            totals.Custom + custom);
    }

    private static (long Count, long Bytes) Add((long Count, long Bytes) total, long bytes) =>
        (total.Count + 1, total.Bytes + bytes);
}
