using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 16 of docs/architecture/android-checks.md: white space used for
// layout (WCAG 1.3.2), from the tree's text, three discrete errors (owner,
// 2026-11-13). The texts that must not be found are as measured in 45 of
// the Pixel's recordings (Gmail, Settings and the client's app).
public sealed class PhoneSequenceChecksTests
{
    [Theory]
    [InlineData("W e l c o m e", 7, "Welcome", "spaces")]
    [InlineData("H\u00a0E\u00a0L\u00a0L\u00a0O", 5, "HELLO", "no-break spaces")]
    [InlineData("\u6771\u3000\u4eac", 2, "\u6771\u4eac", "ideographic spaces")]
    [InlineData("\u6771\n\u4eac\n\u90fd", 3, "\u6771\u4eac\u90fd", "line breaks")]
    [InlineData("Made in the U S A today", 3, "USA", "spaces")]
    [InlineData("C a f e\u0301", 4, "Cafe\u0301", "spaces")]
    public void SpacedLetters_FindsSingleLettersSeparatedByOneWhiteSpace(string text, int letters, string joined, string separators)
    {
        var spaced = PhoneSequenceChecks.SpacedLetters(text);

        Assert.NotNull(spaced);
        Assert.Equal((letters, joined, separators), (spaced.Letters, spaced.Joined, spaced.Separators));
    }

    [Theory]
    [InlineData("A B")]
    [InlineData("Plan A or B")]
    [InlineData("1 2 3 4")]
    [InlineData("x  y  z")]
    [InlineData("\U0001F62B\nTerrible")]
    [InlineData("Sleep Goals\nUpdate your goals")]
    [InlineData("I am a b")]
    public void SpacedLetters_DoesNotFindWordsOrDigits(string text) =>
        Assert.Null(PhoneSequenceChecks.SpacedLetters(text));

    [Fact]
    public void Columns_NeedTwoLinesWithAGapBetweenWords()
    {
        var columns = PhoneSequenceChecks.Columns("Name      Hours\nBob       7.5\nTotal\tnone");

        Assert.NotNull(columns);
        Assert.Equal((3, 3, "Name      Hours"), (columns.WithGap, columns.Lines, columns.First));

        // One line with a gap is not columns: Gmail's sender and count, and a
        // subject with a double space (measured).
        Assert.Null(PhoneSequenceChecks.Columns("me,\u00a0Chris\u00a0\u00a02"));
        Assert.Null(PhoneSequenceChecks.Columns("The multiplier effect: How authoring tools can transform web  accessibility"));

        // Settings' list, each line starting with a no-break space (measured):
        // white space at the start of a line is not a gap between words.
        Assert.Null(PhoneSequenceChecks.Columns(
            "Keep in mind\n\u00a0Colours will change in media and images\n\u00a0Colour inversion works on all apps\n"));
    }

    [Fact]
    public void MidSentenceBreaks_AreBeforeALowercaseLetter_WithNoSentenceOrClauseEnd()
    {
        var breaks = PhoneSequenceChecks.MidSentenceBreaks(
            "Start Select to Speak, then\nyou can tap a specific item\nlike text or an image.\nNext, drag");

        Assert.Equal(
            [new PhoneSequenceChecks.Break("Select to Speak, then", "you can tap a"), new PhoneSequenceChecks.Break("tap a specific item", "like text or an")],
            breaks);

        // As measured: titles over subtitles, lists after a colon, a sentence
        // end before the break, an emoji over a word.
        Assert.Empty(PhoneSequenceChecks.MidSentenceBreaks("Sleep Goals\nUpdate your goals"));
        Assert.Empty(PhoneSequenceChecks.MidSentenceBreaks("Start Select to Speak, then you can:\n\u2022 Tap a specific item"));
        Assert.Empty(PhoneSequenceChecks.MidSentenceBreaks("Signed in as bob dodd bobdodd@gmail.com\nAccount and settings."));
        Assert.Empty(PhoneSequenceChecks.MidSentenceBreaks("Done.\nnext time"));
        Assert.Empty(PhoneSequenceChecks.MidSentenceBreaks("He said \"stop.\"\nthen left"));
        Assert.Empty(PhoneSequenceChecks.MidSentenceBreaks("\U0001F62B\nTerrible"));

        // Vertical text is F32's, not counted again.
        Assert.Empty(PhoneSequenceChecks.MidSentenceBreaks("w\no\nr\nd"));
    }

    [Fact]
    public void Measure_GivesOneErrorOfEachCheckForAnItem_FromWhatTalkBackReads()
    {
        const string Text = "android.widget.TextView";
        var window = new PhoneCheckWindow(512, 1,
        [
            Node(0, null, "android.widget.FrameLayout"),
            Node(1, 0, Text, text: "W e l c o m e", description: "H E L L O"),
            Node(2, 0, Text, text: "Name      Hours\nBob       7.5"),
            Node(3, 0, Text, description: "Tap here to\ncontinue"),
            Node(4, 0, "android.widget.EditText", text: "t y p e d\nby me") with { Editable = true },
            Node(5, 0, "android.widget.EditText", text: "E n t e r") with { Editable = true, ShowingHint = true, Hint = "E n t e r" },
            Node(6, 0, Text, text: "H i d d e n") with { VisibleToUser = false },
            Node(7, 0, Text, text: "Sleep Goals\nUpdate your goals"),
        ]);

        var measured = PhoneSequenceChecks.Measure(window);

        Assert.Equal(
            [(PhoneCheckNames.SpacedLetters, 1), (PhoneCheckNames.SpaceColumns, 2), (PhoneCheckNames.LineBreakMidSentence, 3), (PhoneCheckNames.SpacedLetters, 5)],
            measured.Results.Select(result => (result.Check, result.Item)));
        Assert.All(measured.Results, result => Assert.Equal(PhoneCheckLevel.Error, result.Level));

        // Item 1's text and description, 2's and 3's, 5's hint once, and 7's;
        // the root has none, 4's typed text is the user's, and 6 is not shown.
        Assert.Equal(6, measured.Texts);
        Assert.Equal(
            "its text has 7 single letters separated by spaces, \"W e l c o m e\", so a screen reader may read them one at a time instead of as the word \"Welcome\"; " +
            "F32: white space used to space the letters of a word; an initialism spaced this way, which F32 allows, is found as well",
            measured.Results[0].Measured);
        Assert.Equal(
            "2 of the 2 lines of its text have a gap of 2 or more spaces, or a tab, between words, as columns laid out with white space, the first \"Name      Hours\"; " +
            "a screen reader reads across the columns, line by line; F33: white space used to make columns",
            measured.Results[1].Measured);
        Assert.Equal(
            "its content description has a line break in mid-sentence, the first between \"Tap here to\" and \"continue\": the line ends with no . ! ? : or ;, " +
            "and the next starts with a lowercase letter, so the break is there for its layout, and a screen reader or larger text keeps it",
            measured.Results[2].Measured);
        Assert.StartsWith("its hint has 5 single letters", measured.Results[3].Measured, StringComparison.Ordinal);
        Assert.Equal("Letters spaced apart with white space (WCAG 1.3.2, F32)", PhoneCheckNames.Title(PhoneCheckNames.SpacedLetters));
        Assert.Equal("Columns made with white space (WCAG 1.3.2, F33)", PhoneCheckNames.Title(PhoneCheckNames.SpaceColumns));
        Assert.Equal("Line break in mid-sentence (WCAG 1.3.2)", PhoneCheckNames.Title(PhoneCheckNames.LineBreakMidSentence));
    }

    [Fact]
    public void Measure_ShowsLineBreaksInTheRun()
    {
        var window = new PhoneCheckWindow(512, 1,
        [
            Node(0, null, "android.widget.FrameLayout"),
            Node(1, 0, "android.widget.TextView", text: "\u6771\n\u4eac\n\u90fd"),
        ]);

        var result = Assert.Single(PhoneSequenceChecks.Measure(window).Results);

        Assert.StartsWith("its text has 3 single letters separated by line breaks, \"\u6771 \u23ce \u4eac \u23ce \u90fd\"", result.Measured, StringComparison.Ordinal);
    }

    private static AndroidTreeNode Node(int index, int? parent, string className, string? text = null, string? description = null) =>
        new()
        {
            Index = index,
            Parent = parent,
            ClassName = className,
            Bounds = new AndroidNodeBounds(0, index * 100, 1080, index * 100 + 90),
            Text = text,
            ContentDescription = description,
            ImportantForAccessibility = true,
            VisibleToUser = true,
            Enabled = true,
            Checked = "unchecked",
        };
}
