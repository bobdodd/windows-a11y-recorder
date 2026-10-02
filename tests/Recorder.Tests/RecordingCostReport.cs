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
        var walkReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var changeSetsByDocument = new Dictionary<string, int>(StringComparer.Ordinal);
        var fragmentTotals = new SortedDictionary<string, (long Records, long Bytes, long Fragments, long Children)>(StringComparer.Ordinal);
        var textTotals = new SortedDictionary<string, TextTotals>(StringComparer.Ordinal);
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
                    // Protocol 0.39: text, items, and glyphs.
                    var text = textTotals.TryGetValue(type, out var found) ? found : textTotals[type] = new TextTotals();
                    if (boxFragments.TryGetProperty("textContentUnchanged", out var unchanged) &&
                        unchanged.ValueKind == System.Text.Json.JsonValueKind.True)
                    {
                        text.Unchanged++;
                    }
                    text.TextCharacters += TextLength(boxFragments);
                    foreach (var fragment in boxFragments.GetProperty("fragments").EnumerateArray())
                    {
                        CountItems(fragment, text);
                    }
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
                if (type == "layout-changes-started" &&
                    payload.GetProperty("context").TryGetProperty("documentToken", out var changedToken))
                {
                    var key = changedToken.GetString() ?? "null";
                    changeSetsByDocument[key] = changeSetsByDocument.GetValueOrDefault(key) + 1;
                }
                if (type == "layout-checkpoint-started")
                {
                    var walkReason = payload.TryGetProperty("walkReason", out var reason)
                        ? reason.GetString() ?? "null"
                        : "absent";
                    walkReasons[walkReason] = walkReasons.GetValueOrDefault(walkReason) + 1;
                }
                if (type == "layout-checkpoint-started" &&
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
        report.AppendLine(CultureInfo.InvariantCulture,
            $"checkpoints by walk reason: {string.Join(", ", walkReasons.Select(entry => $"{entry.Key} {entry.Value}"))}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"change sets per document, most first: {string.Join(", ", changeSetsByDocument.Values.OrderDescending())}");
        report.AppendLine("node records by computedStyleComplete: records, bytes per record, style values per styled record, custom properties per styled record");
        foreach (var (kind, totals) in styleKinds)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {kind}: {totals.Count}, {(double)totals.Bytes / totals.Count:F0}, {(totals.Styled > 0 ? (double)totals.Values / totals.Styled : 0):F1}, {(totals.Styled > 0 ? (double)totals.Custom / totals.Styled : 0):F1}");
        }

        report.AppendLine("text and items by record type: records with text left out as unchanged, characters of text JSON, items, characters of items JSON without glyph runs, glyph runs, characters of their JSON, glyphs, characters of glyphs base64");
        foreach (var (type, totals) in textTotals)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {type}: {totals.Unchanged}, {totals.TextCharacters}, {totals.Items}, {totals.ItemCharacters - totals.RunCharacters}, {totals.Runs}, {totals.RunCharacters}, {totals.Glyphs}, {totals.GlyphCharacters}");
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

    private sealed class TextTotals
    {
        public long Unchanged;
        public long TextCharacters;
        public long Items;
        public long ItemCharacters;
        public long RunCharacters;
        public long Runs;
        public long Glyphs;
        public long GlyphCharacters;
    }

    private static long TextLength(System.Text.Json.JsonElement value)
    {
        long length = 0;
        foreach (var name in new[] { "textContent", "firstLineText" })
        {
            if (value.TryGetProperty(name, out var text) && text.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                length += text.GetRawText().Length;
            }
        }
        return length;
    }

    private static void CountItems(System.Text.Json.JsonElement fragment, TextTotals totals)
    {
        totals.TextCharacters += TextLength(fragment);
        if (fragment.TryGetProperty("items", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            totals.ItemCharacters += items.GetRawText().Length;
            foreach (var item in items.EnumerateArray())
            {
                totals.Items++;
                if (item.TryGetProperty("glyphRuns", out var runs) && runs.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    totals.RunCharacters += runs.GetRawText().Length;
                    foreach (var run in runs.EnumerateArray())
                    {
                        totals.Runs++;
                        var glyphs = run.GetProperty("glyphs").GetString()!;
                        totals.GlyphCharacters += glyphs.Length;
                        totals.Glyphs += glyphs.Length / 4 * 3 / 18;
                    }
                }
            }
        }
        foreach (var child in fragment.GetProperty("children").EnumerateArray())
        {
            if (child.TryGetProperty("fragment", out var nested) &&
                nested.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                CountItems(nested, totals);
            }
        }
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
