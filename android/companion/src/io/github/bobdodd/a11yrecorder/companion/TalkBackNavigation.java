package io.github.bobdodd.a11yrecorder.companion;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.AccessibilityServiceInfo;
import android.accessibilityservice.GestureDescription;
import android.accessibilityservice.MagnificationConfig;
import android.content.ComponentName;
import android.graphics.Path;
import android.graphics.Point;
import android.graphics.Rect;
import android.hardware.display.DisplayManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.view.Display;
import android.view.accessibility.AccessibilityManager;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;

import java.util.List;
import java.util.function.BiConsumer;
import java.util.function.BooleanSupplier;

/** Serializes fixed TalkBack navigation and separately approved app-window probe taps. */
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
                        !clearPath(windowId, bounds, new Rect((int)x1, (int)y, (int)x2 + 1, (int)y + 1), true)) {
                    finish.accept("scope-changed", 0L);
                    return;
                }
                Path path = new Path();
                path.moveTo(next ? x1 : x2, y);
                path.lineTo(next ? x2 : x1, y);
                dispatch(path, SWIPE_MS, received, connected, finish);
            } catch (RuntimeException exception) {
                inFlight = false;
                finish.accept("gesture-refused", 0L);
            }
        });
    }

    void probe(int windowId, String packageName, Rect bounds, int x, int y,
            int width, int height, int rotation, long received, BooleanSupplier connected,
            BiConsumer<String, Long> finish) {
        main.post(() -> {
            if (!connected.getAsBoolean() || System.nanoTime() - received > MAX_REQUEST_AGE_NS || inFlight) {
                finish.accept("expired-or-busy", 0L);
                return;
            }
            try {
                AccessibilityManager manager = service.getSystemService(AccessibilityManager.class);
                if (manager == null || manager.isTouchExplorationEnabled() || !unmagnified()) {
                    finish.accept("direct-touch-unavailable", 0L);
                    return;
                }
                DisplayManager displays = service.getSystemService(DisplayManager.class);
                Display display = displays == null ? null : displays.getDisplay(Display.DEFAULT_DISPLAY);
                Point size = new Point();
                if (display != null) {
                    display.getRealSize(size);
                }
                if (display == null || display.getDisplayId() != Display.DEFAULT_DISPLAY ||
                        display.getRotation() * 90 != rotation || size.x != width || size.y != height ||
                        x < 2 || y < 2 || x >= width - 2 || y >= height - 2 ||
                        !bounds.contains(x - 1, y - 1) || !bounds.contains(x + 1, y + 1) ||
                        !activeAppMatches(windowId, packageName) ||
                        !clearPath(windowId, bounds, new Rect(x - 1, y - 1, x + 2, y + 2), false)) {
                    finish.accept("scope-changed", 0L);
                    return;
                }
                Path path = new Path();
                path.moveTo(x, y);
                dispatch(path, 55, received, connected, finish);
            } catch (RuntimeException exception) {
                inFlight = false;
                finish.accept("gesture-refused", 0L);
            }
        });
    }

    private boolean unmagnified() {
        AccessibilityService.MagnificationController controller = service.getMagnificationController();
        if (Build.VERSION.SDK_INT >= 33) {
            MagnificationConfig config = controller.getMagnificationConfig();
            return config != null && config.getScale() == 1f &&
                    (Build.VERSION.SDK_INT < 34 || !config.isActivated());
        }
        return controller.getScale() == 1f;
    }

    private void dispatch(Path path, long duration, long received, BooleanSupplier connected,
            BiConsumer<String, Long> finish) {
        GestureDescription gesture = new GestureDescription.Builder()
                .addStroke(new GestureDescription.StrokeDescription(path, 0, duration)).build();
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

    private boolean clearPath(int windowId, Rect expected, Rect path, boolean requireAccessibilityFocus) {
        // getWindows() returns windows on the default display, where the gesture is dispatched.
        List<AccessibilityWindowInfo> windows = service.getWindows();
        try {
            AccessibilityWindowInfo app = null;
            for (AccessibilityWindowInfo window : windows) {
                if (window.getId() == windowId && window.getType() == AccessibilityWindowInfo.TYPE_APPLICATION
                        && window.isActive() && (!requireAccessibilityFocus || window.isAccessibilityFocused())
                        && !window.isInPictureInPictureMode()) {
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
