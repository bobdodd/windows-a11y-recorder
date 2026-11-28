using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public sealed record AndroidVisionProposal(string Label, AndroidNodeBounds NormalizedBox)
{
    // Round outward so that even the smallest valid normalized box can be drawn.
    public AndroidNodeBounds ImageBounds(int width, int height) => new(
        (int)((long)NormalizedBox.Left * width / 1000), (int)((long)NormalizedBox.Top * height / 1000),
        (int)(((long)NormalizedBox.Right * width + 999) / 1000), (int)(((long)NormalizedBox.Bottom * height + 999) / 1000));
}

public static class AndroidVisionProposals
{
    public const int Limit = 40;

    // Validate the structured answer, but keep its order, values, and duplicates.
    public static IReadOnlyList<AndroidVisionProposal>? Parse(string content)
    {
        try
        {
            using var json = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 6 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("targets", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > Limit)
            {
                return null;
            }
            var result = new List<AndroidVisionProposal>(entries.GetArrayLength());
            foreach (var target in entries.EnumerateArray())
            {
                if (target.ValueKind != JsonValueKind.Object || target.EnumerateObject().Count() != 2 ||
                    !target.TryGetProperty("box", out var box) || box.ValueKind != JsonValueKind.Array || box.GetArrayLength() != 4 ||
                    !target.TryGetProperty("label", out var named) || named.ValueKind != JsonValueKind.String || named.GetString() is not { } label ||
                    label.Length is 0 or > 80 || label.Any(char.IsControl) || !label.Any(char.IsLetterOrDigit))
                {
                    return null;
                }
                var coords = new int[4];
                for (var i = 0; i < coords.Length; i++)
                {
                    if (box[i].ValueKind != JsonValueKind.Number || !box[i].TryGetInt32(out coords[i]) || coords[i] is < 0 or > 1000)
                    {
                        return null;
                    }
                }
                if (coords[0] >= coords[2] || coords[1] >= coords[3])
                {
                    return null;
                }
                result.Add(new(label, new(coords[0], coords[1], coords[2], coords[3])));
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
