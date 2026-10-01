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

        var reportPath = Environment.GetEnvironmentVariable("RECORDER_COST_REPORT") ??
            Path.Combine(Path.GetTempPath(), "cost-report.txt");
        await File.WriteAllTextAsync(reportPath, report.ToString(), TestContext.Current.CancellationToken);
    }

    private static (long Count, long Bytes) Add((long Count, long Bytes) total, long bytes) =>
        (total.Count + 1, total.Bytes + bytes);
}
