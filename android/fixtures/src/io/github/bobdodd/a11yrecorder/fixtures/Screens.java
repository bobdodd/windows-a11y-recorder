package io.github.bobdodd.a11yrecorder.fixtures;

import android.app.Activity;
import android.graphics.Typeface;
import android.view.View;
import android.webkit.WebView;
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
            new Screen("web-basics", "Opening hours", Screens::webBasics)));

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
    private static View textContrast(Activity activity) {
        LinearLayout column = Ui.column(activity);
        column.addView(Ui.heading(activity, "Delivery"));
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
    // question). The screen's own heading is the first.
    private static View headingLook(Activity activity) {
        LinearLayout column = Ui.column(activity);
        TextView good = Ui.heading(activity, "Points");
        good.setId(R.id.heading_good);
        column.addView(good);
        column.addView(Ui.body(activity, "Every order earns points to spend later."));
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
    // app's assets and given to the WebView as data, as a file:///android_asset
    // URL did not load on the first recording (ERR_FILE_NOT_FOUND); where
    // the asset cannot be read, the screen says so in plain text.
    private static View webBasics(Activity activity) {
        String page;
        try {
            page = asset(activity, "web/basics.html");
        } catch (IOException e) {
            LinearLayout column = Ui.column(activity);
            column.addView(Ui.body(activity, "The fixture page web/basics.html could not be read: " + e.getMessage()));
            return column;
        }
        WebView web = new WebView(activity);
        web.setId(R.id.web_page);
        web.setBackgroundColor(Ui.BACKGROUND);
        web.loadDataWithBaseURL("https://fixtures.invalid/web/", page, "text/html", "utf-8", null);
        return web;
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
