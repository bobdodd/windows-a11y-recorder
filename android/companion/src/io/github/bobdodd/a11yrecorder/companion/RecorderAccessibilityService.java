package io.github.bobdodd.a11yrecorder.companion;

import android.accessibilityservice.AccessibilityService;
import android.view.accessibility.AccessibilityEvent;

/**
 * The companion's accessibility service. While it is on, it answers the
 * recorder on a local socket (see {@link RecorderLink}): who it is, the
 * phone's clock, and the display's rotation and size. Slice 2 of
 * docs/architecture/android-implementation-plan.md. Accessibility events
 * and the window tree are recorded from slice 7; until then the events the
 * service receives are not used.
 *
 * The service performs no actions, has no internet permission and stores
 * nothing on the phone.
 */
public final class RecorderAccessibilityService extends AccessibilityService {
    private RecorderLink link;

    @Override
    protected void onServiceConnected() {
        super.onServiceConnected();
        if (link == null) {
            link = new RecorderLink(this);
            link.start();
        }
    }

    @Override
    public void onAccessibilityEvent(AccessibilityEvent event) {
        // Not recorded until slice 7.
    }

    @Override
    public void onInterrupt() {
        // Nothing to stop: the service gives no feedback of its own.
    }

    @Override
    public void onDestroy() {
        if (link != null) {
            link.stop();
            link = null;
        }
        super.onDestroy();
    }
}
