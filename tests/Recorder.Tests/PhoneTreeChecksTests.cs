using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 3 of docs/architecture/android-checks.md, "Build order": the checks
// from the tree, each following the ATF check it names. The items are made
// up, one rule each, at the Pixel 10 Pro XL's measured density of 390 dpi
// (1 dp = 2.4375 pixels) and screen of 1080 by 2404 pixels.
public sealed class PhoneTreeChecksTests
{
    private const float Density = 390f / 160f;

    private static readonly PhoneCheckContext Pixel = new(Density, 1080, 2404);

    private const string Button = "android.widget.Button";

    private const string TextView = "android.widget.TextView";

    private const string Frame = "android.widget.FrameLayout";

    private static AndroidTreeNode Item(
        int index, int? parent, string className, (int Left, int Top, int Right, int Bottom) bounds,
        string? text = null, string? description = null, bool clickable = false, string? viewId = null,
        bool important = true, bool visible = true) =>
        new()
        {
            Index = index,
            Parent = parent,
            ClassName = className,
            Bounds = new AndroidNodeBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            Text = text,
            ContentDescription = description,
            Clickable = clickable,
            ViewId = viewId,
            ImportantForAccessibility = important,
            VisibleToUser = visible,
            Checked = "unchecked",
        };

    // The page every case's items are in: the whole screen, not clickable.
    private static AndroidTreeNode Page() => Item(0, null, Frame, (0, 0, 1080, 2404));

    private static PhoneCheckWindow Window(params AndroidTreeNode[] items) => new(512, 1, items);

    private static List<PhoneCheckResult> Results(Action<PhoneCheckWindow, List<PhoneCheckResult>> check, PhoneCheckWindow window)
    {
        var results = new List<PhoneCheckResult>();
        check(window, results);
        return results;
    }

    private static List<PhoneCheckResult> Touch(PhoneCheckWindow window) =>
        Results((items, results) => PhoneTreeChecks.TouchTargetSize(items, Pixel, results), window);

    private static List<PhoneCheckResult> Wcag(PhoneCheckWindow window) =>
        Results((items, results) => PhoneTreeChecks.TouchTargetWcag(items, Pixel, results), window);

    // Touch target size (ATF, 48dp)

    [Fact]
    public void TouchTarget_A98PixelButton_Is40dp_AnError()
    {
        var results = Touch(Window(Page(), Item(1, 0, Button, (100, 200, 198, 298), text: "Save", clickable: true)));

        var result = Assert.Single(results);
        Assert.Equal((PhoneCheckNames.TouchTarget, PhoneCheckLevel.Error, 1), (result.Check, result.Level, result.Item));
        Assert.Equal("40 by 40 dp, too narrow and too short; ATF needs 48 by 48 dp", result.Measured);
    }

    [Fact]
    public void TouchTarget_AgainstTheScreensSide_Needs32dpWide()
    {
        // 80 pixels is 33 dp, enough against the side; 100 pixels is 41 dp.
        var results = Touch(Window(Page(), Item(1, 0, Button, (0, 200, 80, 300), text: "Menu", clickable: true)));

        var result = Assert.Single(results);
        Assert.Equal(PhoneCheckLevel.Error, result.Level);
        Assert.Equal("33 by 41 dp, too short; ATF needs 32 by 48 dp against the screen's edge", result.Measured);
    }

    [Fact]
    public void TouchTarget_InsideAClickableItemOfTheNeededSize_IsAWarning()
    {
        var results = Touch(Window(
            Page(),
            Item(1, 0, "android.widget.LinearLayout", (0, 400, 1080, 600), clickable: true),
            Item(2, 1, Button, (100, 450, 198, 548), text: "Info", clickable: true)));

        var result = Assert.Single(results);
        Assert.Equal((PhoneCheckLevel.Warning, 2), (result.Level, result.Item));
        Assert.EndsWith("; a warning, as it is inside a clickable item of the needed size", result.Measured, StringComparison.Ordinal);
    }

    [Fact]
    public void TouchTarget_AgainstTheEdgeOfAListThatScrolls_IsNotRun()
    {
        var list = Item(1, 0, "androidx.recyclerview.widget.RecyclerView", (0, 0, 1080, 1000)) with
        {
            Scrollable = true,
            Actions = [new AndroidNodeAction(0x1000, null)],
        };
        var results = Touch(Window(Page(), list, Item(2, 1, Button, (100, 970, 600, 1000), text: "Reset", clickable: true)));

        var result = Assert.Single(results);
        Assert.Equal(PhoneCheckLevel.NotRun, result.Level);
        Assert.EndsWith("; not run, as it is against the edge of a list that scrolls, so may be cut off by it", result.Measured, StringComparison.Ordinal);
    }

    [Fact]
    public void TouchTarget_WithoutTheDensity_IsNotChecked()
    {
        var window = Window(Page(), Item(1, 0, Button, (100, 200, 198, 298), text: "Save", clickable: true));

        Assert.Contains(PhoneTreeChecks.Run(window, Pixel), result => result.Check == PhoneCheckNames.TouchTarget);
        Assert.DoesNotContain(PhoneTreeChecks.Run(window, new PhoneCheckContext(0, 1080, 2404)),
            result => result.Check is PhoneCheckNames.TouchTarget or PhoneCheckNames.TouchTargetWcag);
    }

    // Touch target under 24dp (WCAG 2.5.8)

    [Fact]
    public void Wcag_TwoTargetsUnder24dp_WhoseCirclesMeet_AreErrors()
    {
        // 49 pixels is 20.1 dp; the centres are 40 pixels apart, under the
        // two 12 dp radii (58.5 pixels).
        var results = Wcag(Window(
            Page(),
            Item(1, 0, Button, (100, 500, 149, 549), text: "A", clickable: true),
            Item(2, 0, Button, (140, 500, 189, 549), text: "B", clickable: true)));

        Assert.Equal([1, 2], results.Select(result => result.Item));
        Assert.All(results, result => Assert.Equal(PhoneCheckLevel.Error, result.Level));
        Assert.Equal("20.1 by 20.1 dp, under 24 by 24 dp, and its 24dp circle meets Button \"B\"", results[0].Measured);
    }

    [Fact]
    public void Wcag_TargetsUnder24dp_SpacedApart_Pass()
    {
        var results = Wcag(Window(
            Page(),
            Item(1, 0, Button, (100, 500, 149, 549), text: "A", clickable: true),
            Item(2, 0, Button, (300, 500, 349, 549), text: "B", clickable: true)));

        Assert.Empty(results);
    }

    [Fact]
    public void Wcag_AnItemsOwnClickableParent_IsNotAnotherTarget()
    {
        var results = Wcag(Window(
            Page(),
            Item(1, 0, "android.widget.LinearLayout", (0, 400, 1080, 600), clickable: true),
            Item(2, 1, Button, (100, 450, 149, 499), text: "Info", clickable: true)));

        Assert.Empty(results);
    }

    // Speakable text present (ATF)

    [Fact]
    public void SpeakableText_AFocusableItemWithNoText_IsAnError_ButNotOnceDescribed()
    {
        var bare = Results(PhoneTreeChecks.SpeakableTextPresent,
            Window(Page(), Item(1, 0, "android.widget.ImageButton", (100, 200, 300, 400), clickable: true)));
        var described = Results(PhoneTreeChecks.SpeakableTextPresent,
            Window(Page(), Item(1, 0, "android.widget.ImageButton", (100, 200, 300, 400), description: "Back", clickable: true)));

        var result = Assert.Single(bare);
        Assert.Equal((PhoneCheckNames.SpeakableText, PhoneCheckLevel.Error, 1), (result.Check, result.Level, result.Item));
        Assert.Empty(described);
    }

    [Fact]
    public void SpeakableText_TakesTheTextOfItemsInIt_TheStateAndTheLabel()
    {
        var window = Window(
            Page(),
            Item(1, 0, "android.widget.LinearLayout", (0, 400, 1080, 600), clickable: true),
            Item(2, 1, TextView, (40, 420, 800, 480), text: "Wi-Fi"),
            Item(3, 1, TextView, (40, 500, 800, 560), text: "Connected"),
            Item(4, 0, "android.widget.CheckBox", (0, 700, 1080, 800), text: "Bluetooth", clickable: true) with
            {
                Checkable = true,
                Checked = "checked",
            },
            Item(5, 0, TextView, (0, 900, 300, 1000), text: "Name"),
            Item(6, 0, "android.widget.EditText", (300, 900, 1080, 1000), text: "Type a name") with
            {
                Hint = "Type a name",
                ShowingHint = true,
                Editable = true,
                Focusable = true,
                LabeledBy = [5],
            });

        Assert.Equal("Wi-Fi, Connected", PhoneTreeChecks.SpeakableText(window.Items[1]));
        Assert.Equal("checked, Bluetooth", PhoneTreeChecks.SpeakableText(window.Items[4]));
        Assert.Equal("Type a name for Name", PhoneTreeChecks.SpeakableText(window.Items[6]));
    }

    // Duplicate speakable text (ATF)

    [Fact]
    public void DuplicateText_IsAWarningOnTheFirstClickable_AndInfoWhenNoneIs()
    {
        var clickable = Results(PhoneTreeChecks.DuplicateSpeakableText, Window(
            Page(),
            Item(1, 0, TextView, (0, 100, 500, 200), text: "Save"),
            Item(2, 0, Button, (0, 300, 500, 400), text: "Save", clickable: true)));
        var plain = Results(PhoneTreeChecks.DuplicateSpeakableText, Window(
            Page(),
            Item(1, 0, TextView, (0, 100, 500, 200), text: "2"),
            Item(2, 0, TextView, (0, 300, 500, 400), text: "2")));

        var warning = Assert.Single(clickable);
        Assert.Equal((PhoneCheckLevel.Warning, 2), (warning.Level, warning.Item));
        Assert.Equal("speaks \"Save\", as 1 other item does", warning.Measured);
        var info = Assert.Single(plain);
        Assert.Equal((PhoneCheckLevel.Info, 1), (info.Level, info.Item));
        Assert.Equal("speaks \"2\", as 1 other item does; none is clickable", info.Measured);
    }

    // Duplicate clickable bounds (ATF)

    [Fact]
    public void DuplicateBounds_TwoClickableItemsInOnePlace_AreAnError()
    {
        var results = Results(PhoneTreeChecks.DuplicateClickableBounds, Window(
            Page(),
            Item(1, 0, Frame, (0, 400, 1080, 600), clickable: true),
            Item(2, 1, Button, (0, 400, 1080, 600), text: "Open", clickable: true)));

        var result = Assert.Single(results);
        Assert.Equal((PhoneCheckLevel.Error, 1), (result.Level, result.Item));
        Assert.Equal("the same bounds as 1 other clickable item: Button \"Open\"", result.Measured);
    }

    // Editable content description (ATF)

    [Fact]
    public void EditableDescription_AFieldWithADescription_IsAnError()
    {
        var results = Results(PhoneTreeChecks.EditableContentDescription, Window(
            Page(),
            Item(1, 0, "android.widget.EditText", (0, 400, 1080, 600), description: "Email") with { Editable = true }));

        var result = Assert.Single(results);
        Assert.Equal(PhoneCheckLevel.Error, result.Level);
        Assert.StartsWith("an editable field with the content description \"Email\"", result.Measured, StringComparison.Ordinal);
    }

    // Redundant description (ATF)

    [Fact]
    public void RedundantDescription_NamesEveryWordInOneResult()
    {
        var results = Results(PhoneTreeChecks.RedundantDescription, Window(
            Page(),
            Item(1, 0, "android.widget.ImageButton", (0, 400, 300, 600), description: "Tap to play button", clickable: true),
            Item(2, 0, "android.widget.ImageButton", (400, 400, 700, 600), description: "Buttonhole", clickable: true)));

        var result = Assert.Single(results);
        Assert.Equal((PhoneCheckLevel.Warning, 1), (result.Level, result.Item));
        Assert.Equal(
            "the content description \"Tap to play button\" has the type word \"button\" and the action word \"tap\", which a screen reader says already",
            result.Measured);
    }

    // Class name (ATF)

    [Fact]
    public void ClassName_AnAppsOwnClass_IsAWarning_AndroidsOwnIsNot()
    {
        var results = Results(PhoneTreeChecks.ClassName, Window(
            Page(),
            Item(1, 0, "com.example.FancyView", (0, 400, 300, 600), text: "Fancy"),
            Item(2, 0, "android.view.View", (400, 400, 700, 600), text: "Plain")));

        var result = Assert.Single(results);
        Assert.Equal((PhoneCheckLevel.Warning, 1), (result.Level, result.Item));
        Assert.Contains("com.example.FancyView is not one of Android's own", result.Measured, StringComparison.Ordinal);
    }

    // Traversal order (ATF)

    [Fact]
    public void TraversalOrder_ALoop_WarnsOnEachItemInIt()
    {
        var results = Results(PhoneTreeChecks.TraversalOrder, Window(
            Page(),
            Item(1, 0, Button, (0, 400, 300, 600), text: "A") with { TraversalBefore = 2 },
            Item(2, 0, Button, (400, 400, 700, 600), text: "B") with { TraversalBefore = 1 }));

        Assert.Equal([1, 2], results.Select(result => result.Item));
        Assert.All(results, result => Assert.Equal("its \"traversal before\" links come back to it, a loop", result.Measured));
    }

    [Fact]
    public void TraversalOrder_BeforeAndAfterTheSameItem_Conflict()
    {
        var results = Results(PhoneTreeChecks.TraversalOrder, Window(
            Page(),
            Item(1, 0, Button, (0, 400, 300, 600), text: "A") with { TraversalBefore = 2, TraversalAfter = 2 },
            Item(2, 0, Button, (400, 400, 700, 600), text: "B")));

        var result = Assert.Single(results);
        Assert.Equal(1, result.Item);
        Assert.Equal("its \"traversal before\" and \"traversal after\" links both lead to Button \"B\", so they conflict", result.Measured);
    }

    // The items' keys, identities and labels

    [Fact]
    public void Window_GivesTreeRowKeys_Identities_AndLabels()
    {
        var window = Window(
            Page(),
            Item(1, 0, Button, (0, 400, 300, 600), text: "Save", viewId: "com.example:id/save"),
            Item(2, 0, "android.view.View", (400, 400, 700, 600), description: "Star"),
            Item(3, 0, "android.view.View", (800, 400, 1000, 600), description: "Star"),
            Item(4, 0, "android.view.View", (0, 700, 1080, 900)),
            Item(5, 4, TextView, (0, 700, 500, 800), text: "Sleep"));

        Assert.Equal(
            ["window:512/0", "window:512/0/0", "window:512/0/1", "window:512/0/2", "window:512/0/3", "window:512/0/3/0"],
            window.Items.Select(item => item.TreeKey));
        Assert.Equal("id:com.example:id/save#0", window.Items[1].Identity);
        Assert.Equal("path:android.view.View|Star|android.widget.FrameLayout#0", window.Items[2].Identity);
        Assert.Equal("path:android.view.View|Star|android.widget.FrameLayout#1", window.Items[3].Identity);
        Assert.Equal(
            ["FrameLayout containing \"Save\", \"Star\", \"Star\"", "Button \"Save\"", "View described as \"Star\"", "View described as \"Star\"", "View containing \"Sleep\"", "TextView \"Sleep\""],
            window.Items.Select(item => item.Label));
    }
}
