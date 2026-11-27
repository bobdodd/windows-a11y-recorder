package io.github.bobdodd.a11yrecorder.companion;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.AccessibilityServiceInfo;
import android.accessibilityservice.GestureDescription;
import android.content.ComponentName;
import android.graphics.Path;
import android.graphics.Rect;
import android.os.Handler;
import android.os.Looper;
import android.view.accessibility.AccessibilityManager;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;

import java.util.List;
import java.util.function.BiConsumer;
import java.util.function.BooleanSupplier;

/** No arbitrary coordinates, node actions, clicks, setting writes, or queued swipes. */
final class TalkBackNavigation {
    private static final String TALKBACK = "com.google.android.marvin.talkback";
    private static final long MAX_REQUEST_AGE_NS = 750_000_000L;
    private static final long SWIPE_MS = 180;
    private final AccessibilityService service;
    private final Handler main = new Handler(Looper.getMainLooper());
    private boolean inFlight;

    TalkBackNavigation(AccessibilityService service) {
        this.service = service;
    }

    void request(boolean next, int windowId, String packageName, Rect bounds, long received,
            BooleanSupplier connected, BiConsumer<String, Long> finish) {
        main.post(() -> {
            if (!connected.getAsBoolean() || System.nanoTime() - received > MAX_REQUEST_AGE_NS || inFlight) {
                finish.accept("expired-or-busy", 0L);
                return;
            }
            try {
                if (!talkBackReady()) {
                    finish.accept("talkback-unavailable", 0L);
                    return;
                }
                if (!activeAppMatches(windowId, packageName)) {
                    finish.accept("scope-changed", 0L);
                    return;
                }
                float x1 = bounds.left + bounds.width() * .28f;
                float x2 = bounds.left + bounds.width() * .72f;
                float y = bounds.exactCenterY();
                if (bounds.left < 0 || bounds.top < 0 || bounds.width() < 200 || bounds.height() < 160 ||
                        !clearPath(windowId, bounds, new Rect((int)x1, (int)y, (int)x2 + 1, (int)y + 1))) {
                    finish.accept("scope-changed", 0L);
                    return;
                }
                Path path = new Path();
                path.moveTo(next ? x1 : x2, y);
                path.lineTo(next ? x2 : x1, y);
                GestureDescription gesture = new GestureDescription.Builder()
                        .addStroke(new GestureDescription.StrokeDescription(path, 0, SWIPE_MS)).build();
                if (!connected.getAsBoolean() || System.nanoTime() - received > MAX_REQUEST_AGE_NS) {
                    finish.accept("expired-or-busy", 0L);
                    return;
                }
                long started = System.nanoTime();
                inFlight = service.dispatchGesture(gesture, new AccessibilityService.GestureResultCallback() {
                    @Override
                    public void onCompleted(GestureDescription ignored) {
                        inFlight = false;
                        finish.accept("dispatched", started);
                    }

                    @Override
                    public void onCancelled(GestureDescription ignored) {
                        inFlight = false;
                        finish.accept("gesture-cancelled", started);
                    }
                }, main);
                if (!inFlight) {
                    finish.accept("gesture-refused", 0L);
                }
            } catch (RuntimeException exception) {
                inFlight = false;
                finish.accept("gesture-refused", 0L);
            }
        });
    }

    private boolean talkBackReady() {
        AccessibilityManager manager = service.getSystemService(AccessibilityManager.class);
        if (manager == null || !manager.isTouchExplorationEnabled()) {
            return false;
        }
        boolean found = false;
        for (AccessibilityServiceInfo info : manager.getEnabledAccessibilityServiceList(
                AccessibilityServiceInfo.FEEDBACK_ALL_MASK)) {
            ComponentName component = ComponentName.unflattenFromString(info.getId());
            if (component == null) {
                return false;
            }
            if (TALKBACK.equals(component.getPackageName())) {
                found = true;
            } else if (!service.getPackageName().equals(component.getPackageName())) {
                // Another service could handle the same gesture.
                return false;
            }
        }
        return found;
    }

    private boolean activeAppMatches(int windowId, String packageName) {
        AccessibilityNodeInfo root = service.getRootInActiveWindow();
        if (root == null) {
            return false;
        }
        try {
            return root.getWindowId() == windowId && packageName.contentEquals(root.getPackageName() == null
                    ? "" : root.getPackageName());
        } finally {
            root.recycle();
        }
    }

    private boolean clearPath(int windowId, Rect expected, Rect path) {
        // getWindows() returns windows on the default display, where the gesture is dispatched.
        List<AccessibilityWindowInfo> windows = service.getWindows();
        try {
            AccessibilityWindowInfo app = null;
            for (AccessibilityWindowInfo window : windows) {
                if (window.getId() == windowId && window.getType() == AccessibilityWindowInfo.TYPE_APPLICATION
                        && window.isActive() && window.isAccessibilityFocused()) {
                    app = window;
                    break;
                }
            }
            if (app == null) {
                return false;
            }
            Rect actual = new Rect();
            app.getBoundsInScreen(actual);
            if (!actual.equals(expected)) {
                return false;
            }
            for (AccessibilityWindowInfo window : windows) {
                if (window.getLayer() > app.getLayer()) {
                    window.getBoundsInScreen(actual);
                    if (Rect.intersects(actual, path)) {
                        return false;
                    }
                }
            }
            return true;
        } finally {
            for (AccessibilityWindowInfo window : windows) {
                window.recycle();
            }
        }
    }
}
