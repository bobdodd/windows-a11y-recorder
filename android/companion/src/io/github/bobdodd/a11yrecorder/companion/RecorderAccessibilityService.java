package io.github.bobdodd.a11yrecorder.companion;

import android.accessibilityservice.AccessibilityService;
import android.view.accessibility.AccessibilityEvent;

/**
 * The companion's accessibility service. In part 0 of the implementation
 * plan it does nothing: it exists so the build pipeline produces an
 * installable app with the service declared. Later parts add the clock
 * channel, accessibility events and the window tree.
 */
public final class RecorderAccessibilityService extends AccessibilityService {
    @Override
    public void onAccessibilityEvent(AccessibilityEvent event) {
        // Nothing is recorded yet.
    }

    @Override
    public void onInterrupt() {
        // Nothing to stop.
    }
}
