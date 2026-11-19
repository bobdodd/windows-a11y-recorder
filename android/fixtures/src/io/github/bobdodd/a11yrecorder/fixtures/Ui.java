package io.github.bobdodd.a11yrecorder.fixtures;

import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.util.TypedValue;
import android.view.View;
import android.widget.ImageButton;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * The parts every fixture screen is made of. The colours and sizes are
 * the screens' plain defaults, chosen to pass the checks, so that only
 * what a screen sets out to fail fails.
 */
final class Ui {
    /** The screens' background, set on each screen so the theme's off-white does not change a contrast. */
    static final int BACKGROUND = Color.WHITE;

    /** Body and heading text, about 16:1 on white. */
    static final int TEXT = 0xFF202124;

    private Ui() {
    }

    static int dp(Context context, float dp) {
        return Math.round(TypedValue.applyDimension(
                TypedValue.COMPLEX_UNIT_DIP, dp, context.getResources().getDisplayMetrics()));
    }

    /** A screen's column, with the screen's padding and background. */
    static LinearLayout column(Context context) {
        LinearLayout column = new LinearLayout(context);
        column.setOrientation(LinearLayout.VERTICAL);
        int padding = dp(context, 16);
        column.setPadding(padding, padding, padding, padding);
        column.setBackgroundColor(BACKGROUND);
        return column;
    }

    /** A row of items side by side. */
    static LinearLayout row(Context context) {
        LinearLayout row = new LinearLayout(context);
        row.setOrientation(LinearLayout.HORIZONTAL);
        return row;
    }

    /** The screen's heading: large, bold and marked as a heading. */
    static TextView heading(Context context, String text) {
        TextView heading = text(context, text, 24, TEXT);
        heading.setId(R.id.screen_heading);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        heading.setAccessibilityHeading(true);
        return heading;
    }

    /** Body text at 16 sp. */
    static TextView body(Context context, String text) {
        return text(context, text, 16, TEXT);
    }

    static TextView text(Context context, String text, float sizeSp, int colour) {
        TextView view = new TextView(context);
        view.setText(text);
        view.setTextSize(TypedValue.COMPLEX_UNIT_SP, sizeSp);
        view.setTextColor(colour);
        int gap = dp(context, 8);
        view.setPadding(0, gap, 0, gap);
        return view;
    }

    /**
     * An icon-only button of exactly the given size in dp, with no
     * background, so its bounds are its size and nothing else is drawn
     * round it. Clicking it does nothing, so the screen does not change.
     */
    static ImageButton iconButton(Context context, int id, int icon, String name, int sizeDp) {
        ImageButton button = new ImageButton(context);
        button.setId(id);
        button.setImageResource(icon);
        button.setContentDescription(name);
        button.setBackground(null);
        button.setScaleType(ImageView.ScaleType.FIT_CENTER);
        int inset = dp(context, sizeDp / 6f);
        button.setPadding(inset, inset, inset, inset);
        button.setMinimumWidth(0);
        button.setMinimumHeight(0);
        button.setOnClickListener(view -> { });
        int size = dp(context, sizeDp);
        button.setLayoutParams(new LinearLayout.LayoutParams(size, size));
        return button;
    }

    /** Empty space, in dp. */
    static View space(Context context, int widthDp, int heightDp) {
        View space = new View(context);
        space.setLayoutParams(new LinearLayout.LayoutParams(dp(context, widthDp), dp(context, heightDp)));
        return space;
    }
}
