package io.github.bobdodd.a11yrecorder.fixtures;

import android.app.Activity;
import android.graphics.Paint;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.text.SpannableString;
import android.text.Spanned;
import android.text.TextPaint;
import android.text.method.LinkMovementMethod;
import android.text.style.ClickableSpan;
import android.text.style.URLSpan;
import android.view.Gravity;
import android.view.View;
import android.webkit.WebView;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;

/**
 * The fixture screens. Each one's faults and near misses, and what each
 * check should find on them, are listed in android/fixtures/expected.json;
 * a test keeps the two in step. Every word on a screen is made up.
 */
final class Screens {
    static final List<Screen> ALL = Collections.unmodifiableList(Arrays.asList(
            new Screen("touch-targets", "Basket", Screens::touchTargets),
            new Screen("text-contrast", "Delivery", Screens::textContrast),
            new Screen("control-names", "Holiday photos", Screens::controlNames),
            new Screen("heading-look", "Points", Screens::headingLook),
            new Screen("web-basics", "Opening hours", Screens::webBasics),
            new Screen("colour-only", "Recipes", Screens::colourOnly),
            new Screen("link-purpose", "Kitchen help", Screens::linkPurpose),
            new Screen("sensory", "Shopping list", Screens::sensory),
            new Screen("chart-bars", "Plant sales", ChartScreens::bars),
            new Screen("chart-lightness", "Seed orders", ChartScreens::lightness),
            new Screen("chart-outlines", "Shop visits", ChartScreens::outlines),
            new Screen("chart-lines", "Rainfall", ChartScreens::lines),
            new Screen("chart-line-labels", "River levels", ChartScreens::lineLabels),
            new Screen("chart-pie", "Household budget", ChartScreens::pie),
            new Screen("chart-stacked", "Energy mix", ChartScreens::stacked),
            new Screen("chart-highlight", "Monthly spending", ChartScreens::highlight),
            new Screen("chart-scatter", "Orchard survey", ChartScreens::scatter),
            new Screen("chart-heat", "Class attendance", ChartScreens::heat),
            new Screen("chart-image", "Ticket sales", ChartScreens::image),
            new Screen("web-charts", "Library loans", Screens::webCharts)));

    private Screens() {
    }

    static Screen find(String id) {
        if (id == null) {
            return null;
        }
        for (Screen screen : ALL) {
            if (screen.id.equals(id)) {
                return screen;
            }
        }
        return null;
    }

    // Two 20 dp buttons 2 dp apart, whose 24 dp circles meet (fails 2.5.8
    // and ATF's 48 dp); a 20 dp button with nothing within 24 dp (passes
    // 2.5.8 by its spacing exception, fails ATF); a 32 dp button (passes
    // 2.5.8, fails ATF); a 48 dp button (passes both).
    private static View touchTargets(Activity activity) {
        LinearLayout column = Ui.column(activity);
        column.addView(Ui.heading(activity, "Basket"));
        column.addView(Ui.body(activity, "Change how many you want, or move on."));

        LinearLayout pair = Ui.row(activity);
        pair.addView(Ui.iconButton(activity, R.id.touch_pair_add, R.drawable.ic_add, "Add one", 20));
        pair.addView(Ui.space(activity, 2, 20));
        pair.addView(Ui.iconButton(activity, R.id.touch_pair_remove, R.drawable.ic_remove, "Take one away", 20));
        column.addView(pair);

        column.addView(Ui.space(activity, 1, 40));
        column.addView(Ui.iconButton(activity, R.id.touch_spaced, R.drawable.ic_close, "Empty the basket", 20));
        column.addView(Ui.space(activity, 1, 40));
        column.addView(Ui.iconButton(activity, R.id.touch_medium, R.drawable.ic_star, "Save for later", 32));
        column.addView(Ui.space(activity, 1, 40));
        column.addView(Ui.iconButton(activity, R.id.touch_large, R.drawable.ic_arrow, "Go to checkout", 48));
        return column;
    }

    // Three lines of 16 sp grey text on white. The check judges OCR's
    // lines, as it cannot know the text's size: about 2.2:1, below even
    // the 3:1 for large text (an error); about 3.0:1, which fails unless
    // the text is large (a warning); and about 4.54:1, just over the 4.5:1
    // it needs (passes when measured from a screenshot; from the video the
    // check allows for the video's colours and warns that it is near).
    // They follow a line not tested and a gap (see clearOfTalkBack).
    private static View textContrast(Activity activity) {
        LinearLayout column = Ui.column(activity);
        column.addView(Ui.heading(activity, "Delivery"));
        clearOfTalkBack(activity, column, "Choose how your order reaches you.");
        TextView error = Ui.text(activity, "Orders are packed on the day they are placed.", 16, 0xFFB0B0B0);
        error.setId(R.id.contrast_error);
        column.addView(error);
        TextView warning = Ui.text(activity, "Orders over forty dollars travel free of charge.", 16, 0xFF949494);
        warning.setId(R.id.contrast_warning);
        column.addView(warning);
        TextView near = Ui.text(activity, "Parcels usually arrive within three working days.", 16, 0xFF767676);
        near.setId(R.id.contrast_near);
        column.addView(near);
        return column;
    }

    // An icon-only button named with its resource name (a placeholder, no
    // model); an arrow named for its icon, "Arrow", not for what it does
    // (a model's question); the same arrow named "Next photo" (passes).
    private static View controlNames(Activity activity) {
        LinearLayout column = Ui.column(activity);
        column.addView(Ui.heading(activity, "Holiday photos"));
        column.addView(Ui.body(activity, "Photo 2 of 9, a beach at sunset."));
        LinearLayout row = Ui.row(activity);
        row.addView(Ui.iconButton(activity, R.id.names_resource, R.drawable.ic_close, "btn_close", 48));
        row.addView(Ui.space(activity, 24, 48));
        row.addView(Ui.iconButton(activity, R.id.names_icon, R.drawable.ic_arrow, "Arrow", 48));
        row.addView(Ui.space(activity, 24, 48));
        row.addView(Ui.iconButton(activity, R.id.names_good, R.drawable.ic_arrow, "Next photo", 48));
        column.addView(row);
        return column;
    }

    // "Points", large and bold, marked as a heading (passes); a value,
    // "1,250 points", in body text, marked as a heading (fails, a model's
    // question). The screen's own heading is the first. The value follows
    // a line not tested and a gap (see clearOfTalkBack).
    private static View headingLook(Activity activity) {
        LinearLayout column = Ui.column(activity);
        TextView good = Ui.heading(activity, "Points");
        good.setId(R.id.heading_good);
        column.addView(good);
        clearOfTalkBack(activity, column, "Every order earns points to spend later.");
        TextView value = Ui.body(activity, "1,250 points");
        value.setId(R.id.heading_value);
        value.setTypeface(Typeface.DEFAULT);
        value.setAccessibilityHeading(true);
        column.addView(value);
        column.addView(Ui.body(activity, "Points can be spent when paying."));
        return column;
    }

    // A WebView page with low and good contrast text and a value marked as
    // a heading; see assets/web/basics.html. The page is read from the
    // app's assets and given to the WebView as data; where the asset cannot
    // be read, the screen says so in plain text. On the first two recordings
    // it could not be found, as the app built on Windows named it
    // assets/web\basics.html, with a backslash (measured, 2026-11-19);
    // scripts/Build-CompanionApp.ps1 now gives such names forward slashes.
    private static View webBasics(Activity activity) {
        return webPage(activity, "web/basics.html");
    }

    // Two SVG bar charts, one told apart only by hue and one with a
    // pattern as well, for the check not yet built of a chart that relies
    // on colour (1.4.1); see assets/web/charts.html and ChartScreens.
    private static View webCharts(Activity activity) {
        return webPage(activity, "web/charts.html");
    }

    private static View webPage(Activity activity, String path) {
        String page;
        try {
            page = asset(activity, path);
        } catch (IOException e) {
            LinearLayout column = Ui.column(activity);
            column.addView(Ui.body(activity, "The fixture page " + path + " could not be read: " + e.getMessage()));
            return column;
        }
        WebView web = new WebView(activity);
        web.setId(R.id.web_page);
        web.setBackgroundColor(Ui.BACKGROUND);
        web.loadDataWithBaseURL("https://fixtures.invalid/web/", page, "text/html", "utf-8", null);

        // The WebView is held in a frame, which takes the padding that keeps
        // the screen clear of the system bars: a WebView given the padding
        // itself still filled the window, and drew the page's heading and
        // first line under the app bar (measured on the third recording,
        // 2026-11-19).
        FrameLayout frame = new FrameLayout(activity);
        frame.setBackgroundColor(Ui.BACKGROUND);
        frame.addView(web, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));
        return frame;
    }

    // Two checks not yet built, of what is shown only by colour (1.4.1).
    // A row of tabs whose selected tab differs only in colour: #B0306A
    // against the others' #5F6368, both about 6:1 on white and of the
    // same luminance, so the difference is hue alone (fails); a row whose
    // selected tab is also bold and underlined (passes). A paragraph whose
    // link is only coloured, #1558B0, about 2.3:1 against the body text
    // and 6.9:1 on white (fails, under the 3:1 against the text that a
    // colour alone would need); the same link underlined (passes). Clicking
    // a tab or link does nothing, so the screen does not change. They follow a line not tested and a gap (see
    // clearOfTalkBack), as the checks read their colours.
    private static final int TAB = 0xFF5F6368;
    private static final int TAB_SELECTED = 0xFFB0306A;
    private static final int LINK = 0xFF1558B0;

    private static View colourOnly(Activity activity) {
        LinearLayout column = Ui.column(activity);
        column.addView(Ui.heading(activity, "Recipes"));
        clearOfTalkBack(activity, column, "Pick a course to see its recipes.");

        LinearLayout courses = Ui.row(activity);
        courses.addView(tab(activity, View.NO_ID, "Soups", false, false));
        courses.addView(tab(activity, R.id.selected_colour, "Salads", true, false));
        courses.addView(tab(activity, View.NO_ID, "Desserts", false, false));
        column.addView(courses);

        LinearLayout meals = Ui.row(activity);
        meals.addView(tab(activity, View.NO_ID, "Breakfast", false, false));
        meals.addView(tab(activity, R.id.selected_marked, "Lunch", true, true));
        meals.addView(tab(activity, View.NO_ID, "Dinner", false, false));
        column.addView(meals);

        column.addView(Ui.space(activity, 1, 24));
        column.addView(link(activity, R.id.link_colour, "Read the ", "cooking terms", " before you start.", false));
        column.addView(link(activity, R.id.link_underlined, "Keep the ", "kitchen safety notes", " close by.", true));
        return column;
    }

    // Link purpose (WCAG 2.4.4 and 2.4.9), each link underlined so the
    // check of links told apart only by colour has nothing to find:
    // "Read more" as all of its paragraph, after a paragraph that gives its
    // context in another item (fails 2.4.4); "here" in a sentence that says
    // where it leads (passes 2.4.4, fails 2.4.9: a warning at AA); a link
    // whose words say where it leads (passes both); two "Details" links to
    // different addresses, each after its course's name (non-specific, and
    // the same words for different places); and two "opening hours" links
    // to the same address (passes). The addresses are example.org's, and
    // the tour does not follow them.
    private static View linkPurpose(Activity activity) {
        LinearLayout column = Ui.column(activity);
        column.addView(Ui.heading(activity, "Kitchen help"));
        clearOfTalkBack(activity, column, "Tips for cooking at home.");
        column.addView(Ui.body(activity, "Knives are kept sharp and stored in the top drawer."));
        column.addView(link(activity, R.id.link_alone, "", "Read more", "", true));
        column.addView(link(activity, R.id.link_here, "To see the full menu, tap ", "here", ".", true));
        column.addView(link(activity, R.id.link_described, "Before you start, read the ", "knife safety guide", ".", true));
        column.addView(link(activity, R.id.link_details_soups, "Soups: ", "Details", "", "https://example.org/soups"));
        column.addView(link(activity, R.id.link_details_salads, "Salads: ", "Details", "", "https://example.org/salads"));
        column.addView(link(activity, R.id.link_hours_first, "See the ", "opening hours", " before you come.", "https://example.org/hours"));
        column.addView(link(activity, R.id.link_hours_again, "The ", "opening hours", " change in winter.", "https://example.org/hours"));
        return column;
    }

    // A tab at least 48 dp each way, so the touch target checks have
    // nothing to find. A selected tab is marked selected for accessibility
    // services, and shown in TAB_SELECTED, and, when marked, bold and
    // underlined too.
    private static TextView tab(Activity activity, int id, String text, boolean selected, boolean marked) {
        TextView tab = Ui.text(activity, text, 16, selected ? TAB_SELECTED : TAB);
        tab.setId(id);
        tab.setGravity(Gravity.CENTER);
        int across = Ui.dp(activity, 16);
        tab.setPadding(across, 0, across, 0);
        tab.setMinWidth(Ui.dp(activity, 48));
        tab.setMinHeight(Ui.dp(activity, 48));
        tab.setSelected(selected);
        if (marked) {
            tab.setTypeface(Typeface.DEFAULT_BOLD);
            tab.setPaintFlags(tab.getPaintFlags() | Paint.UNDERLINE_TEXT_FLAG);
        }
        tab.setOnClickListener(view -> { });
        return tab;
    }

    // A paragraph of body text holding one link in LINK, underlined or
    // not. The link movement method makes the paragraph clickable and
    // focusable, and the paragraph is at least 48 dp tall, so the touch
    // target checks have nothing to find.
    private static TextView link(Activity activity, int id, String before, String linked, String after, boolean underlined) {
        return paragraph(activity, id, before, linked, after, new ClickableSpan() {
            @Override
            public void onClick(View view) {
            }

            @Override
            public void updateDrawState(TextPaint paint) {
                paint.setColor(LINK);
                paint.setUnderlineText(underlined);
            }
        });
    }

    // A paragraph holding a link to an address, a URLSpan, underlined.
    private static TextView link(Activity activity, int id, String before, String linked, String after, String url) {
        return paragraph(activity, id, before, linked, after, new URLSpan(url) {
            @Override
            public void updateDrawState(TextPaint paint) {
                paint.setColor(LINK);
                paint.setUnderlineText(true);
            }
        });
    }

    private static TextView paragraph(Activity activity, int id, String before, String linked, String after, Object span) {
        SpannableString text = new SpannableString(before + linked + after);
        text.setSpan(span, before.length(), before.length() + linked.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        TextView paragraph = Ui.body(activity, "");
        paragraph.setId(id);
        paragraph.setText(text);
        paragraph.setMovementMethod(LinkMovementMethod.getInstance());
        paragraph.setHighlightColor(Ui.BACKGROUND);
        paragraph.setMinHeight(Ui.dp(activity, 48));
        return paragraph;
    }

    // Instructions that rely on sensory characteristics (WCAG 1.3.3), each
    // pair over the row of buttons it is about: one instruction that names
    // a button only by where it is, its shape or its colour (fails), and one
    // that names the other button by its text as well (passes). Each button
    // is at least 48 dp each way, its white text at least 5:1 on its fill
    // and its fill at least 5:1 on white, so the touch target and contrast
    // checks have nothing to find. Clicking a button does nothing.
    private static final int BUTTON_GREY = 0xFF5F6368;
    private static final int BUTTON_GREEN = 0xFF1E7A3C;
    private static final int BUTTON_BLUE = 0xFF1A4FA0;

    private static View sensory(Activity activity) {
        LinearLayout column = Ui.column(activity);
        column.addView(Ui.heading(activity, "Shopping list"));
        clearOfTalkBack(activity, column, "Six things to buy this week.");

        column.addView(instruction(activity, R.id.instruction_location, "To save the list, tap the button on the right."));
        column.addView(instruction(activity, R.id.instruction_location_named, "To empty the list, tap Clear, on the left."));
        column.addView(buttons(activity,
                button(activity, "Clear", BUTTON_BLUE, false),
                button(activity, "Save", BUTTON_BLUE, false)));

        column.addView(instruction(activity, R.id.instruction_shape, "To add a thing to buy, tap the round button."));
        column.addView(instruction(activity, R.id.instruction_shape_named, "To sort the list, tap Sort, the square button."));
        column.addView(buttons(activity,
                button(activity, "Add", BUTTON_BLUE, true),
                button(activity, "Sort", BUTTON_BLUE, false)));

        column.addView(instruction(activity, R.id.instruction_colour, "To mark the list as bought, tap the green button."));
        column.addView(instruction(activity, R.id.instruction_colour_named, "To take that back, tap Undo, the grey button."));
        column.addView(buttons(activity,
                button(activity, "Done", BUTTON_GREEN, false),
                button(activity, "Undo", BUTTON_GREY, false)));
        return column;
    }

    private static TextView instruction(Activity activity, int id, String text) {
        TextView line = Ui.body(activity, text);
        line.setId(id);
        return line;
    }

    private static LinearLayout buttons(Activity activity, TextView first, TextView second) {
        LinearLayout row = Ui.row(activity);
        row.setGravity(Gravity.CENTER_VERTICAL);
        int gap = Ui.dp(activity, 16);
        row.setPadding(0, Ui.dp(activity, 8), 0, gap);
        row.addView(first);
        LinearLayout.LayoutParams apart = (LinearLayout.LayoutParams) second.getLayoutParams();
        apart.leftMargin = gap;
        row.addView(second);
        return row;
    }

    // A button drawn as white text on a fill: a 64 dp circle if round, or
    // a rectangle with square corners at least 48 dp each way.
    private static TextView button(Activity activity, String text, int fill, boolean round) {
        TextView button = Ui.text(activity, text, 16, 0xFFFFFFFF);
        button.setGravity(Gravity.CENTER);
        GradientDrawable shape = new GradientDrawable();
        shape.setShape(round ? GradientDrawable.OVAL : GradientDrawable.RECTANGLE);
        shape.setColor(fill);
        button.setBackground(shape);
        int across = Ui.dp(activity, 16);
        button.setPadding(across, 0, across, 0);
        int size = Ui.dp(activity, 64);
        LinearLayout.LayoutParams params = round
                ? new LinearLayout.LayoutParams(size, size)
                : new LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, Ui.dp(activity, 48));
        button.setLayoutParams(params);
        button.setMinWidth(Ui.dp(activity, round ? 64 : 88));
        button.setOnClickListener(view -> { });
        return button;
    }

    // A line not tested, then a gap, so that what TalkBack draws over the
    // screen covers nothing the contrast checks are tested on, as they do
    // not measure what it covers. In the second recording, made with
    // TalkBack on (measured, 2026-11-19), TalkBack's focus outline was
    // drawn round the first line after the heading, and the accessibility
    // button's window was at the screen's left edge from 543 to 661 pixels
    // down, at 390 dpi, over the line after it. The gap puts the next line
    // about 180 pixels below that window on that phone; a person can drag
    // the button elsewhere, and then the checks say what it covers.
    static final int TALKBACK_GAP_DP = 120;

    private static void clearOfTalkBack(Activity activity, LinearLayout column, String line) {
        column.addView(Ui.body(activity, line));
        column.addView(Ui.space(activity, 1, TALKBACK_GAP_DP));
    }

    private static String asset(Activity activity, String path) throws IOException {
        try (InputStream input = activity.getAssets().open(path);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int read;
            while ((read = input.read(buffer)) > 0) {
                output.write(buffer, 0, read);
            }
            return new String(output.toByteArray(), StandardCharsets.UTF_8);
        }
    }
}
