using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>Step 31 of android-checks.md: instructions that rely on place, shape or colour, WCAG 1.3.3, from items' text.</summary>
public sealed class PhoneSensoryChecksTests
{
    private const string TextView = "android.widget.TextView";

    // A shown TextView, a button when clickable.
    private static AndroidTreeNode Item(int index, string text, bool clickable = false, int parent = 0, bool shown = true) =>
        new()
        {
            Index = index,
            Parent = parent,
            ClassName = TextView,
            Bounds = new AndroidNodeBounds(40, 200 * index, 1040, 200 * index + 100),
            Text = text,
            ImportantForAccessibility = true,
            VisibleToUser = shown,
            Clickable = clickable,
            Focusable = clickable,
            Checked = "unchecked",
        };

    private static AndroidTreeNode Page() => new()
    {
        Index = 0,
        ClassName = "android.widget.LinearLayout",
        Bounds = new AndroidNodeBounds(0, 0, 1080, 2404),
        ImportantForAccessibility = true,
        VisibleToUser = true,
        Checked = "unchecked",
    };

    private static PhoneCheckWindow Window(params AndroidTreeNode[] items) => new(512, 1, [Page(), .. items]);

    private static List<PhoneCheckResult> Run(PhoneCheckWindow window) => [.. PhoneSensoryChecks.Run(window)];

    [Fact]
    public void AnInstructionByPlace_WithTheControlNamedOnlyBeforeTheVerb_IsAnError()
    {
        var window = Window(
            Item(1, "To save the list, tap the button on the right."),
            Item(2, "Clear", clickable: true),
            Item(3, "Save", clickable: true));

        var finding = Assert.Single(Run(window));
        Assert.Equal((PhoneCheckNames.InstructionByPlace, PhoneCheckLevel.Error, 1), (finding.Check, finding.Level, finding.Item));
        Assert.Contains("'tap the button on the right'", finding.Measured, StringComparison.Ordinal);
        Assert.Contains("'right'", finding.Measured, StringComparison.Ordinal);
        Assert.Contains("F14", finding.Measured, StringComparison.Ordinal);
        Assert.Contains("names the control in another way", finding.Measured, StringComparison.Ordinal);
        Assert.Equal(1, PhoneSensoryChecks.Measure(window).Instructions);
    }

    [Fact]
    public void AnInstructionByPlace_ThatNamesTheControl_GivesNoFinding()
    {
        var window = Window(
            Item(1, "Tap Save, on the right, to save the list."),
            Item(2, "Clear", clickable: true),
            Item(3, "Save", clickable: true));

        Assert.Empty(Run(window));
    }

    [Fact]
    public void AnInstructionByShape_IsAnError_AndNamingTheControlIsANearMiss()
    {
        var fault = Window(Item(1, "To add an item, press the round button."), Item(2, "Add", clickable: true), Item(3, "Sort", clickable: true));
        var nearMiss = Window(Item(1, "Press the round Add button to add an item."), Item(2, "Add", clickable: true), Item(3, "Sort", clickable: true));

        var finding = Assert.Single(Run(fault));
        Assert.Equal((PhoneCheckNames.InstructionByShape, PhoneCheckLevel.Error), (finding.Check, finding.Level));
        Assert.Contains("'round'", finding.Measured, StringComparison.Ordinal);
        Assert.Empty(Run(nearMiss));
    }

    [Fact]
    public void AControlsNameUsedAfterTheVerbInAnotherSense_PassesWrongly_AsALimit()
    {
        var window = Window(Item(1, "Press the round button to add an item."), Item(2, "Add", clickable: true));

        Assert.Empty(Run(window));
    }

    [Fact]
    public void AnInstructionByColour_IsAnError_Citing141_AndNamingTheControlIsANearMiss()
    {
        var fault = Window(Item(1, "When you have finished, select the green button."), Item(2, "Done", clickable: true), Item(3, "Undo", clickable: true));
        var nearMiss = Window(Item(1, "When you have finished, select Done, the green button."), Item(2, "Done", clickable: true), Item(3, "Undo", clickable: true));

        var finding = Assert.Single(Run(fault));
        Assert.Equal((PhoneCheckNames.InstructionByColour, PhoneCheckLevel.Error), (finding.Check, finding.Level));
        Assert.Contains("1.4.1", finding.Measured, StringComparison.Ordinal);
        Assert.Contains("F13", finding.Measured, StringComparison.Ordinal);
        Assert.Empty(Run(nearMiss));
    }

    [Fact]
    public void AnInstructionRelyingOnTwoCharacteristics_GivesAFindingForEach()
    {
        var window = Window(Item(1, "Tap the green button on the right."), Item(2, "Done", clickable: true));

        Assert.Equal(
            [PhoneCheckNames.InstructionByPlace, PhoneCheckNames.InstructionByColour],
            Run(window).Select(result => result.Check));
    }

    [Fact]
    public void AboveAndBelow_AreNotPlaceWords()
    {
        var window = Window(Item(1, "Tap the button below to go on, or the one above to go back."), Item(2, "Next", clickable: true));

        Assert.Empty(Run(window));
    }

    [Fact]
    public void ADescription_NotAnInstruction_GivesNoFinding()
    {
        var window = Window(Item(1, "The green light on the right shows the timer is on."), Item(2, "Stop", clickable: true));

        Assert.Empty(Run(window));
        Assert.Equal(0, PhoneSensoryChecks.Measure(window).Instructions);
    }

    [Fact]
    public void AnInstruction_EndsAtTheNextVerb()
    {
        var window = Window(Item(1, "Tap Save and press the button on the left."), Item(2, "Save", clickable: true), Item(3, "Clear", clickable: true));

        var finding = Assert.Single(Run(window));
        Assert.Equal(PhoneCheckNames.InstructionByPlace, finding.Check);
        Assert.Contains("'press the button on the left'", finding.Measured, StringComparison.Ordinal);
        Assert.Equal(2, PhoneSensoryChecks.Measure(window).Instructions);
    }

    [Fact]
    public void AHiddenItem_IsNotLookedAt()
    {
        var window = Window(Item(1, "Tap the button on the right.", shown: false), Item(2, "Save", clickable: true));

        Assert.Empty(Run(window));
        Assert.Equal(0, PhoneSensoryChecks.Measure(window).Instructions);
    }

    [Fact]
    public void AHiddenControlsName_DoesNotCount()
    {
        var window = Window(Item(1, "Tap the Save button on the right."), Item(2, "Save", clickable: true, shown: false));

        Assert.Equal(PhoneCheckNames.InstructionByPlace, Assert.Single(Run(window)).Check);
    }

    [Fact]
    public void TheInstructionsOwnControl_IsNotAName()
    {
        // A clickable item whose own text is the instruction does not name
        // itself, nor does the clickable row it is in.
        var row = Item(1, "", clickable: true);
        var window = Window(row, Item(2, "Tap the round button.", parent: 1));

        Assert.Equal(PhoneCheckNames.InstructionByShape, Assert.Single(Run(window)).Check);
    }

    [Fact]
    public void AControlWithNoTextOfItsOwn_IsNamedByTheTextInsideIt()
    {
        var window = Window(Item(1, "Tap the round Add button."), Item(2, "", clickable: true), Item(3, "Add", parent: 2));

        Assert.Empty(Run(window));
    }

    [Fact]
    public void TheTitles_SayTheCriteria()
    {
        Assert.Equal(
            ["Instruction relies on place (WCAG 1.3.3)", "Instruction relies on shape (WCAG 1.3.3)", "Instruction relies on colour (WCAG 1.3.3, 1.4.1)"],
            new[] { PhoneCheckNames.InstructionByPlace, PhoneCheckNames.InstructionByShape, PhoneCheckNames.InstructionByColour }.Select(PhoneCheckNames.Title));
    }
}
