using System.Globalization;

namespace Recorder.Session;

/// <summary>What a settled accessibility tree actually published, not a new check.</summary>
public sealed record PhoneFormEvidence(PhoneVisit Visit, long TreeTime, IReadOnlyList<string> Fields)
{
    public string TimeLabel => WindowsPreferenceTimeline.Clock(TreeTime);

    internal static IReadOnlyList<string> Describe(PhoneCheckWindow window)
    {
        var byIndex = window.Items.ToLookup(item => item.Node.Index, item => item.Node);
        var fields = new List<string>();
        foreach (var item in window.Items)
        {
            var node = item.Node;
            if (!node.Editable || !node.VisibleToUser || item.Bounds.IsEmpty)
            {
                continue;
            }

            var labels = node.LabeledBy.Where(index => index != node.Index).Select(index =>
            {
                var label = byIndex[index].FirstOrDefault();
                var words = label is { Editable: false, Password: false }
                    ? Quote(label.Text) ?? Quote(label.ContentDescription) : null;
                return $"node {index}" + (label is null
                    ? " (not in this tree)"
                    : (label.VisibleToUser ? "" : " (not visible to user)") +
                      (words is null ? "" : $" {words}"));
            }).ToList();
            if (node.LabeledByOutside > 0)
            {
                labels.Add($"{node.LabeledByOutside} outside the captured nodes");
            }
            var linked = labels.Count == 0 ? "none published" : string.Join(", ", labels);
            var hint = Quote(node.Hint) ?? "none published";
            var required = node.FieldRequired is { } value ? YesNo(value) : "not recorded";
            fields.Add(string.Create(CultureInfo.CurrentCulture,
                $"Node {node.Index}, {node.ViewId ?? node.ClassName ?? "unidentified field"}: label links: {linked}. " +
                $"Hint: {hint}. Showing hint: {YesNo(node.ShowingHint)}. Content description present: {YesNo(Quote(node.ContentDescription) is not null)}. " +
                $"Required: {required}. Multiline: {YesNo(node.MultiLine)}. Input type code: {node.InputType}. " +
                $"Marked invalid: {YesNo(node.ContentInvalid)}. Error text present: {YesNo(Quote(node.Error) is not null)}."));
        }
        return fields;
    }

    private static string? Quote(string? text) => string.IsNullOrWhiteSpace(text)
        ? null : $"“{string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))}”";

    private static string YesNo(bool value) => value ? "yes" : "no";
}
