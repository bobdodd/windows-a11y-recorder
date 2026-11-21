package io.github.bobdodd.a11yrecorder.apkfixtures;

import android.app.Activity;
import android.graphics.Insets;
import android.os.Bundle;
import android.os.CountDownTimer;
import android.util.TypedValue;
import android.view.View;
import android.view.WindowInsets;
import android.view.accessibility.AccessibilityManager;
import android.webkit.WebView;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;

/** Synthetic fixture only. No permissions, network, or recording-side code. */
public final class ApkEvidenceActivity extends Activity {
    private CountDownTimer timer;
    private WebView web;

    @Override
    public void onCreate(Bundle state) {
        super.onCreate(state);
        setContentView(R.layout.apk_form);
        keepClearOfSystemBars(findViewById(R.id.apk_form_root));
        LinearLayout controls = findViewById(R.id.apk_runtime);
        TextView label = new TextView(this);
        label.setId(R.id.apk_runtime_label);
        label.setText("Code-created label");
        label.setTextSize(TypedValue.COMPLEX_UNIT_SP, 21);
        label.setLabelFor(R.id.apk_runtime_field);
        controls.addView(label);
        EditText field = new EditText(this);
        field.setId(R.id.apk_runtime_field);
        field.setHint("Code-created hint");
        controls.addView(field);
        TextView status = findViewById(R.id.apk_status);
        findViewById(R.id.apk_start).setOnClickListener(view -> {
            stopTimer();
            AccessibilityManager manager = getSystemService(AccessibilityManager.class);
            int duration = manager.getRecommendedTimeoutMillis(8000, AccessibilityManager.FLAG_CONTENT_TEXT);
            status.setText("Timer started");
            timer = new CountDownTimer(duration, 1000) {
                @Override public void onTick(long millis) { }
                @Override public void onFinish() { status.setText("Timer finished"); }
            };
            timer.start();
        });
        findViewById(R.id.apk_cancel).setOnClickListener(view -> {
            stopTimer();
            status.setText("Timer cancelled");
        });
        findViewById(R.id.apk_web).setOnClickListener(view -> {
            stopTimer();
            web = new WebView(this);
            web.getSettings().setBlockNetworkLoads(true);
            setTitle("Packaged web form");
            keepClearOfSystemBars(web);
            setContentView(web);
            web.loadUrl("file:///android_asset/web/form.html");
        });
    }

    private static void keepClearOfSystemBars(View content) {
        content.setOnApplyWindowInsetsListener((view, insets) -> {
            Insets safe = insets.getInsets(WindowInsets.Type.systemBars()
                    | WindowInsets.Type.displayCutout() | WindowInsets.Type.ime());
            view.setPadding(safe.left, safe.top, safe.right, safe.bottom);
            return WindowInsets.CONSUMED;
        });
    }

    private void stopTimer() {
        if (timer != null) {
            timer.cancel();
            timer = null;
        }
    }

    @Override
    protected void onDestroy() {
        stopTimer();
        if (web != null) {
            setContentView(new TextView(this));
            web.destroy();
        }
        super.onDestroy();
    }
}
