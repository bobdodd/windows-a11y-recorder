package io.github.bobdodd.a11yrecorder.apkfixtures;

import android.app.Activity;
import android.graphics.Canvas;
import android.graphics.Insets;
import android.graphics.Paint;
import android.os.Bundle;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

/** Fixed viewport with independent focus stops, a semantic group and a canvas-only control. */
public final class TalkBackFixtureActivity extends Activity {
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout screen = new LinearLayout(this);
        screen.setOrientation(LinearLayout.VERTICAL);
        screen.setOnApplyWindowInsetsListener((view, insets) -> {
            Insets safe = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
            view.setPadding(safe.left + dp(16), safe.top + dp(12), safe.right + dp(16), safe.bottom);
            return WindowInsets.CONSUMED;
        });
        Button first = addButton(screen, "First fixture action");
        LinearLayout group = new LinearLayout(this);
        group.setOrientation(LinearLayout.VERTICAL);
        group.setPadding(dp(8), dp(6), dp(8), dp(6));
        group.setScreenReaderFocusable(true);
        group.setContentDescription("Fixture summary, two items");
        addGroupText(group, "Fixture summary");
        addGroupText(group, "Two items");
        screen.addView(group);
        screen.addView(new DrawnControl(), new LinearLayout.LayoutParams(-1, dp(58)));
        Button last = addButton(screen, "Last fixture action");
        TextView status = new TextView(this);
        status.setText("No fixture action activated");
        status.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        screen.addView(status);
        first.setOnClickListener(view -> status.setText("First fixture action activated"));
        last.setOnClickListener(view -> status.setText("Last fixture action activated"));
        setContentView(screen);
    }

    private Button addButton(LinearLayout parent, String label) {
        Button button = new Button(this);
        button.setText(label);
        parent.addView(button, new LinearLayout.LayoutParams(-1, -2));
        return button;
    }

    private void addGroupText(LinearLayout parent, String label) {
        TextView text = new TextView(this);
        text.setText(label);
        text.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        parent.addView(text, new LinearLayout.LayoutParams(-1, -2));
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private final class DrawnControl extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private boolean pressed;
        private float downX;
        private float downY;

        DrawnControl() {
            super(TalkBackFixtureActivity.this);
            setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
            setFocusable(false);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            paint.setColor(pressed ? 0xff294a3d : 0xff263850);
            canvas.drawRect(0, dp(6), getWidth(), getHeight() - dp(6), paint);
            paint.setColor(0xffffffff);
            paint.setTextSize(dp(16));
            canvas.drawText(pressed ? "Canvas-only action activated" : "Canvas-only action",
                    dp(12), dp(35), paint);
        }

        @Override
        public boolean onTouchEvent(MotionEvent event) {
            // A deliberate fixture fault: a visually labelled tap target
            // without a corresponding accessible node or click action.
            if (event.getActionMasked() == MotionEvent.ACTION_DOWN) {
                downX = event.getX();
                downY = event.getY();
            } else if (event.getActionMasked() == MotionEvent.ACTION_UP
                    && Math.abs(event.getX() - downX) < dp(10)
                    && Math.abs(event.getY() - downY) < dp(10)) {
                pressed = !pressed;
                invalidate();
            }
            return true;
        }
    }
}
