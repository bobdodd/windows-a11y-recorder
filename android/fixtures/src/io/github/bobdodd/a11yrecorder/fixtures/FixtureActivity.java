package io.github.bobdodd.a11yrecorder.fixtures;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Insets;
import android.os.Bundle;
import android.view.View;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;

/**
 * Shows one fixture screen, named by the intent's "screen" extra, or with
 * no extra an index of every screen for a person to choose from.
 */
public final class FixtureActivity extends Activity {
    static final String SCREEN = "screen";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        show(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        show(intent);
    }

    private void show(Intent intent) {
        Screen screen = Screens.find(intent.getStringExtra(SCREEN));
        View content;
        if (screen == null) {
            setTitle(R.string.app_name);
            content = index();
        } else {
            setTitle(screen.title);
            content = screen.builder.build(this);
        }
        keepClearOfSystemBars(content);
        setContentView(content);
    }

    private View index() {
        LinearLayout column = Ui.column(this);
        column.addView(Ui.heading(this, "Screens"));
        for (Screen screen : Screens.ALL) {
            Button button = new Button(this);
            button.setText(screen.title);
            button.setOnClickListener(view -> {
                Intent open = new Intent(this, FixtureActivity.class);
                open.putExtra(SCREEN, screen.id);
                startActivity(open);
            });
            column.addView(button);
        }
        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(Ui.BACKGROUND);
        scroll.addView(column);
        return scroll;
    }

    // Apps aimed at Android 15 and later are drawn under the system bars,
    // so the content is moved in by them, keeping it clear of the screen's
    // edges as the checks expect.
    private static void keepClearOfSystemBars(View content) {
        int left = content.getPaddingLeft();
        int top = content.getPaddingTop();
        int right = content.getPaddingRight();
        int bottom = content.getPaddingBottom();
        content.setOnApplyWindowInsetsListener((view, insets) -> {
            Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
            view.setPadding(left + bars.left, top + bars.top, right + bars.right, bottom + bars.bottom);
            return WindowInsets.CONSUMED;
        });
    }
}
