namespace Agenda.Core;

public sealed record FeedColorChoice(string Name, string Hex);

public static class FeedColorPalette
{
    public static IReadOnlyList<FeedColorChoice> Colors { get; } = Array.AsReadOnly(new[] {
        new FeedColorChoice("Blue", "#0078D4"),
        new FeedColorChoice("Teal", "#2EC4B6"),
        new FeedColorChoice("Violet", "#B69CFF"),
        new FeedColorChoice("Gold", "#E9B44C"),
        new FeedColorChoice("Rose", "#F08AAC"),
        new FeedColorChoice("Green", "#82C986"),
        new FeedColorChoice("Coral", "#F28B74"),
        new FeedColorChoice("Indigo", "#899BFF"),
        new FeedColorChoice("Sky", "#60CDFF"),
        new FeedColorChoice("Mint", "#69DDB8"),
        new FeedColorChoice("Lavender", "#C6B4F0"),
        new FeedColorChoice("Amber", "#FFCA64"),
        new FeedColorChoice("Magenta", "#D875C9"),
        new FeedColorChoice("Emerald", "#36AD83"),
        new FeedColorChoice("Orange", "#F7A35C"),
        new FeedColorChoice("Cyan", "#45BDD1"),
        new FeedColorChoice("Slate", "#95A9BC"),
        new FeedColorChoice("Plum", "#AB80BD")
    });

    public static string PickNext(IEnumerable<string> usedColors)
    {
        var used = usedColors.ToArray();
        return Colors.OrderBy(color => used.Count(value =>
            string.Equals(value, color.Hex, StringComparison.OrdinalIgnoreCase))).First().Hex;
    }
}
