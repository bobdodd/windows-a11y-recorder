using System.Globalization;
using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// A style sheet of a recorded document as its latest full entry in a
/// style-sheets-updated record gives it (protocol 0.51, slice 4e). Sheet is
/// its number in the renderer. TextSource is "arrived", "element", "cssom",
/// or "none"; TextDigest names the text for "arrived" and "cssom".
/// </summary>
public sealed record RecordedStyleSheet(
    string Sheet,
    string Kind,
    long? OwnerNodeId,
    string? ParentSheet,
    int? RuleIndex,
    string? Href,
    string Media,
    string Title,
    bool Disabled,
    bool Active,
    string TextSource,
    string? TextDigest);

/// <summary>
/// The sheets of one tree scope at the frame, by number: its sheets in
/// document.styleSheets order with each import after the sheet that imports
/// it, and its adopted sheets in order.
/// </summary>
public sealed record RecordedStyleSheetScope(
    long ScopeNodeId,
    IReadOnlyList<string> Sheets,
    IReadOnlyList<string> Adopted);

/// <summary>
/// The style sheets of a recorded document at a frame (protocol 0.51, slice
/// 4e): each tree scope's sheets from the latest style-sheets-updated record
/// that touched it at or before the frame, each sheet's details from the
/// record that last gave them in full, and the digest of the text each sheet
/// address arrived with, for answering the page's requests. See
/// docs/architecture/page-recreation.md, "Slice 4e".
/// </summary>
public sealed class RecordedStyleSheets
{
    private readonly IReadOnlyDictionary<string, string> _arrivedByUrl;

    public RecordedStyleSheets(
        IReadOnlyList<RecordedStyleSheetScope> scopes,
        IReadOnlyDictionary<string, RecordedStyleSheet> sheets,
        IReadOnlyDictionary<string, string> arrivedByUrl,
        bool recorded)
    {
        Scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        Sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        _arrivedByUrl = arrivedByUrl ?? throw new ArgumentNullException(nameof(arrivedByUrl));
        Recorded = recorded;
    }

    public static RecordedStyleSheets None { get; } = new([], new Dictionary<string, RecordedStyleSheet>(), new Dictionary<string, string>(), false);

    /// <summary>Whether the recording holds style sheet records (protocol 0.51).</summary>
    public bool Recorded { get; }

    public IReadOnlyList<RecordedStyleSheetScope> Scopes { get; }

    /// <summary>Every sheet named by a scope at the frame and given in full, by number.</summary>
    public IReadOnlyDictionary<string, RecordedStyleSheet> Sheets { get; }

    /// <summary>The sheets in effect at the frame, each once, in scope order.</summary>
    public IReadOnlyList<RecordedStyleSheet> InEffect()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<RecordedStyleSheet>();
        foreach (var scope in Scopes)
        {
            foreach (var number in scope.Sheets.Concat(scope.Adopted))
            {
                if (seen.Add(number) && Sheets.TryGetValue(number, out var sheet))
                {
                    result.Add(sheet);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// The digest of the text a sheet at a URL arrived with, compared without
    /// its fragment, or null.
    /// </summary>
    public string? ArrivedDigest(string url) =>
        _arrivedByUrl.TryGetValue(RecordedPageResources.WithoutFragment(url), out var digest) ? digest : null;

    public int ArrivedUrlCount => _arrivedByUrl.Count;

    /// <summary>
    /// Collects the style sheet records of one document, in recording order,
    /// and gives the sheets at the cut.
    /// </summary>
    public sealed class Builder
    {
        private readonly Dictionary<long, RecordedStyleSheetScope> _scopes = [];
        private readonly List<long> _scopeOrder = [];
        private readonly Dictionary<string, RecordedStyleSheet> _sheets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _resourceByUrl = new(StringComparer.Ordinal);
        private bool _recorded;

        /// <summary>A style-sheet-resource record, of any document.</summary>
        public void AddResource(JsonElement payload)
        {
            _recorded = true;
            if (Text(payload, "url") is not { } url || Text(payload, "digest") is not { } digest ||
                !payload.TryGetProperty("textRecorded", out var recorded) || recorded.ValueKind != JsonValueKind.True)
            {
                return;
            }
            _resourceByUrl[RecordedPageResources.WithoutFragment(url)] = digest;
            if (Text(payload, "responseUrl") is { } response)
            {
                _resourceByUrl[RecordedPageResources.WithoutFragment(response)] = digest;
            }
        }

        /// <summary>A style-sheets-updated record of the document.</summary>
        public void AddUpdate(JsonElement payload)
        {
            _recorded = true;
            if (!payload.TryGetProperty("scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            foreach (var scope in scopes.EnumerateArray())
            {
                if (!scope.TryGetProperty("scopeNodeId", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var scopeNodeId))
                {
                    continue;
                }
                var sheets = Entries(scope, "sheets");
                var adopted = Entries(scope, "adopted");
                if (!_scopes.ContainsKey(scopeNodeId))
                {
                    _scopeOrder.Add(scopeNodeId);
                }
                _scopes[scopeNodeId] = new RecordedStyleSheetScope(scopeNodeId, sheets, adopted);
            }
        }

        private List<string> Entries(JsonElement scope, string name)
        {
            var numbers = new List<string>();
            if (!scope.TryGetProperty(name, out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                return numbers;
            }
            foreach (var entry in entries.EnumerateArray())
            {
                if (Text(entry, "sheet") is not { } number)
                {
                    continue;
                }
                numbers.Add(number);
                if (Text(entry, "kind") is { } kind)
                {
                    _sheets[number] = new RecordedStyleSheet(
                        number,
                        kind,
                        entry.TryGetProperty("ownerNodeId", out var owner) && owner.ValueKind == JsonValueKind.Number && owner.TryGetInt64(out var ownerId) ? ownerId : null,
                        Text(entry, "parentSheet"),
                        entry.TryGetProperty("ruleIndex", out var rule) && rule.ValueKind == JsonValueKind.Number && rule.TryGetInt32(out var ruleIndex) ? ruleIndex : null,
                        Text(entry, "href"),
                        Text(entry, "media") ?? "",
                        Text(entry, "title") ?? "",
                        entry.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True,
                        entry.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True,
                        Text(entry, "textSource") ?? "none",
                        Text(entry, "textDigest"));
                }
            }
            return numbers;
        }

        public RecordedStyleSheets Build()
        {
            var scopes = _scopeOrder.Select(id => _scopes[id]).ToList();
            var named = new HashSet<string>(scopes.SelectMany(scope => scope.Sheets.Concat(scope.Adopted)), StringComparer.Ordinal);
            var sheets = _sheets.Where(item => named.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            // A sheet in effect that arrived with recorded text answers its
            // own address with that text; any other address a sheet names,
            // such as a sheet script changed after it arrived, takes the
            // latest resource record of that address at or before the cut.
            var arrived = new Dictionary<string, string>(_resourceByUrl, StringComparer.Ordinal);
            foreach (var sheet in sheets.Values)
            {
                if (sheet is { TextSource: "arrived", TextDigest: { } digest, Href: { } href })
                {
                    arrived[RecordedPageResources.WithoutFragment(href)] = digest;
                }
            }
            return new RecordedStyleSheets(scopes, sheets, arrived, _recorded);
        }
    }

    /// <summary>What the evidence panel says of the document's sheets.</summary>
    public IReadOnlyList<string> Notes()
    {
        static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
        if (!Recorded)
        {
            return ["The recording holds no style sheet records, which are recorded from protocol 0.51, so the page has only its recorded style elements; every other style sheet request is refused."];
        }
        var inEffect = InEffect();
        var kinds = inEffect.GroupBy(sheet => sheet.Kind).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{Count(group.Count())} {group.Key}");
        var notes = new List<string>
        {
            $"{Count(inEffect.Count)} style sheets were in effect at the frame ({string.Join(", ", kinds)}). A link or @import sheet's address is answered with the text it arrived with, which DevTools' Styles and Sources panels show. A sheet script changed is given its recorded CSSOM text, each rule's cssText as Blink serialized it, and a constructed sheet is made from it and adopted as recorded."
        };
        var changed = inEffect.Count(sheet => sheet.TextSource == "cssom" && sheet.Kind != "constructed");
        if (changed > 0)
        {
            notes.Add($"{Count(changed)} sheets had been changed through the CSSOM at the frame, so their rules are the recorded CSSOM text, not the text they arrived with.");
        }
        var without = inEffect.Count(sheet => sheet.TextSource == "none");
        if (without > 0)
        {
            notes.Add($"{Count(without)} sheets have no recorded text, so they keep whatever the recreation loads for them, or nothing.");
        }
        var missing = Scopes.SelectMany(scope => scope.Sheets.Concat(scope.Adopted)).Distinct(StringComparer.Ordinal).Count(number => !Sheets.ContainsKey(number));
        if (missing > 0)
        {
            notes.Add($"{Count(missing)} sheets are named at the frame with no record of their details, so they are left as the recreation loads them.");
        }
        return notes;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
