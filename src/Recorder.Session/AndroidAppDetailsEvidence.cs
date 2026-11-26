using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>The display groups for a recording's saved APK read. No screen or visit attribution is inferred.</summary>
public sealed record AndroidApkDetailGroup(string Topic, string Source, IReadOnlyList<string> Items);

public sealed record AndroidApkDetails(
    string Summary, string Context, string? Problem,
    IReadOnlyList<AndroidApkDetailGroup> Groups, IReadOnlyList<string> Coverage)
{
    public static AndroidApkDetails? From(SessionManifest manifest)
    {
        if (manifest.AndroidPreRecordingTextSizes is not { ApkFonts: { } apk } snapshot)
        {
            return null;
        }

        var version = apk.VersionName is { } name && apk.VersionCode is { } code
            ? $"version {name} (code {code})" : "version unavailable";
        var summary = $"{snapshot.PackageName}, {version}. Read {apk.ApksRead} of {apk.ApksTotal} installed APKs.";
        var evidence = apk.Accessibility is { SchemaVersion: 1 } saved ? saved : null;
        var context = FormattableString.Invariant($"Read before recording at {apk.ReadUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss 'UTC'}. ");
        context += evidence is null
            ? (apk.Accessibility is null ? "This recording has a font-only inventory. " : "The saved APK inventory format is not supported. Showing saved font lists. ") +
              $"Flutter font manifests read: {apk.FlutterManifestsRead}. " +
              "Bundled files and font declarations do not establish the font or size used by a screen. " +
              "The font-only inventory does not inspect native styles, compiled code or form layouts."
            : $"{evidence.Items.Count} structured entries saved. Bundled files, declarations, API symbols and static call sites are package evidence, not observations from a recorded window. " +
              "A component locator is not a link to a screen visit. A method locator identifies compiled code, not a recorded execution.";

        var items = evidence?.Items ?? [];
        var groups = items.GroupBy(item => (item.Category, item.Kind, item.Scope))
            .Select(group => new AndroidApkDetailGroup(group.Key.Category, Source(group.Key.Kind, group.Key.Scope),
                group.Select(item => $"{ShowValue(item.Value)} [{item.Split}: {item.Locator}]").ToArray()))
            .ToList();

        // Font-only recordings and source entries beyond the structured recording limit
        // still contribute every saved path and declaration, without repeating matched rows.
        var savedFiles = items.Where(item => item.Category == "Font file").Select(item => item.Value).ToHashSet(StringComparer.Ordinal);
        AddMissing("Font file", "Bundled, app-wide", apk.FontFiles.Where(path => !savedFiles.Contains(path)));
        var savedFamilies = items.Where(item => item.Category == "Font family").Select(item => item.Value).ToHashSet(StringComparer.Ordinal);
        AddMissing("Font family", "Declared, app-wide", apk.FlutterFamilies.Select(DescribeFamily).Where(item => !savedFamilies.Contains(item)));

        var coverage = evidence?.Coverage.Select(item => $"{item.Reader}: {item.Status}. {item.Detail}").ToArray() ?? [];
        return new(summary, context, apk.Problem, groups
            .OrderBy(group => group.Topic switch { "Font file" => 0, "Font family" => 1, "Typography" => 2, _ => 3 })
            .ThenBy(group => group.Topic, StringComparer.Ordinal)
            .ThenBy(group => group.Source, StringComparer.Ordinal).ToArray(), coverage);

        void AddMissing(string topic, string source, IEnumerable<string> rows)
        {
            var missing = rows.ToArray();
            if (missing.Length == 0)
            {
                return;
            }
            var group = groups.FindIndex(item => item.Topic == topic && item.Source == source);
            if (group < 0)
            {
                groups.Add(new(topic, source, missing));
            }
            else
            {
                groups[group] = groups[group] with { Items = [.. groups[group].Items, .. missing] };
            }
        }
    }

    private static string DescribeFamily(AndroidApkFontFamily item) => $"{item.Family}; asset {item.Asset}" +
        (item.Weight is { } weight ? $"; weight {weight}" : "") +
        (item.Style is { } style ? $"; style {style}" : "");

    private static string Source(string kind, string scope) => (kind switch
    {
        "bundled" => "Bundled",
        "declared" => "Declared",
        "symbol" => "API symbol",
        "call" => "Static call site",
        _ => kind
    }) + (scope switch { "app" => ", app-wide", "component" => ", component", "method" => ", method", _ => $", {scope}" });

    private static string ShowValue(string value) => value.Any(char.IsControl)
        ? string.Concat(value.Select(character => char.IsControl(character) ? $"\\u{(int)character:X4}" : character.ToString()))
        : value;
}

public static class AndroidAppDetailsEvidence
{
    public static bool IsAvailable(SessionPlaybackArchive? archive) => archive is not null &&
        (archive.FrameSource == SessionFrameSource.Phone ||
         archive.Manifest.AndroidPreRecordingTextSizes is not null ||
         archive.PhoneScreens.Recorded || archive.PhoneDevice.VersionsRecorded || archive.PhoneDevice.Densities.Count > 0 ||
         archive.PhoneTouches.Recorded || archive.Timeline.ChannelCounts.ContainsKey(AndroidTouchRecords.KeyChannel) ||
         archive.TalkBackSpeech.Recorded || archive.PhoneAccessibility.TreesRecorded || archive.PhoneAccessibility.FocusRecorded ||
         archive.PhoneSettings.Recorded || archive.AudioTracks.Any(track => track.Stream == AndroidSoundRecords.StreamName) ||
         archive.Manifest.Collectors.Any(collector => collector.Channels.Any(channel => channel is
             AndroidCompanionRecords.CompanionChannel or AndroidScreenRecords.VideoChannel or AndroidFrameRecords.FramesChannel or AndroidDeviceRecords.Channel)));

    public static IReadOnlyList<string> Packages(SessionPlaybackArchive archive) =>
        archive.PhoneScreens.Apps.Select(app => app.Package)
            .Concat(archive.Manifest.AndroidPreRecordingTextSizes is { } selected ? [selected.PackageName] : [])
            .Concat(archive.PhoneDevice.PackagesWithVersionRecords)
            .Distinct(StringComparer.Ordinal).ToArray();
}

public sealed record AndroidFormVisitDetails(int Screen, string ScreenLabel, int Visit, IReadOnlyList<PhoneFormEvidence> Trees);

public sealed record AndroidFormAppDetails(string Package, IReadOnlyList<AndroidFormVisitDetails> Visits);

public sealed record AndroidFormDetails(string Summary, IReadOnlyList<AndroidFormAppDetails> Apps)
{
    public static AndroidFormDetails From(SessionPlaybackArchive archive, PhoneCheckRun? run, bool failed = false)
    {
        var summary = failed ? "Recorded trees could not be reviewed." :
            !archive.PhoneScreens.Recorded ? "No visits with application trees were available in this recording." :
            run is null ? "Form evidence has not yet been read." :
            run.WindowsChecked == 0 ? "No settled application tree was available to read." :
            run.FormEvidence.Count == 0 ? "No visible editable nodes were published in the settled application trees read." :
            "These labels, hints and field states were published in settled recorded application trees. Field values are not shown. A hint is not a label link.";
        return new(summary, Group(run?.FormEvidence ?? []));
    }

    public static IReadOnlyList<AndroidFormAppDetails> Group(IEnumerable<PhoneFormEvidence> evidence) =>
        evidence.GroupBy(form => form.Visit.Package, StringComparer.Ordinal)
            .Select(app => new AndroidFormAppDetails(app.Key, app.GroupBy(form => form.Visit)
                .Select(visit => new AndroidFormVisitDetails(visit.Key.Screen.Number, visit.Key.Screen.Label, visit.Key.Number,
                    visit.OrderBy(form => form.TreeTime).ToArray())).ToArray())).ToArray();
}
