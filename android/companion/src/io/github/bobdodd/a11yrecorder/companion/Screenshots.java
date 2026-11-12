package io.github.bobdodd.a11yrecorder.companion;

import android.accessibilityservice.AccessibilityService;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.ColorSpace;
import android.hardware.HardwareBuffer;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Base64;
import android.util.Log;
import android.view.Display;
import android.view.accessibility.AccessibilityEvent;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;

/**
 * Screenshots of the phone's display, step 4a of
 * docs/architecture/android-checks.md, for measuring colours without the
 * video's compression.
 *
 * While a connection wants them, one is taken when the connection asks,
 * and one each time a window has had no change for half a second, the
 * recorder's rule for a settled snapshot: a change is a window change
 * (TYPE_WINDOWS_CHANGED, TYPE_WINDOW_STATE_CHANGED), a content change
 * (TYPE_WINDOW_CONTENT_CHANGED) or a scroll (TYPE_VIEW_SCROLLED), each
 * counted against the window it came from. Windows that settle while one
 * is being taken are covered by another, taken as soon as that one ends.
 *
 * Android refuses calls closer than 333 ms apart
 * (ACCESSIBILITY_TAKE_SCREENSHOT_REQUEST_INTERVAL_TIMES_MS), so they are
 * kept at least SPACING_MILLISECONDS apart, and one refused as too soon is
 * taken again. Any other refusal, such as a window marked secure, is sent
 * as a "screenshot-failed" message with Android's code.
 *
 * Each screenshot is copied from Android's hardware buffer, converted to
 * sRGB if Android gave another colour space, written as PNG and sent as one
 * "screenshot" line with the PNG in base64. The work is done on its own
 * thread, and the lines are sent from another, so neither the service's
 * events nor the other messages wait for it. Nothing is kept on the phone.
 */
final class Screenshots {
    /** How long a window has had no change before it is settled, as on the recorder. */
    static final long SETTLE_MILLISECONDS = 500;

    /** The least time between two calls; Android refuses calls closer than 333 ms. */
    static final long SPACING_MILLISECONDS = 350;

    private static final String TAG = "A11yRecorderCompanion";

    private final AccessibilityService service;
    private final RecorderLink link;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ExecutorService encoder = Executors.newSingleThreadExecutor();
    private final Runnable takeNow = this::take;

    // Used on the main thread only.
    private final Map<Integer, Runnable> settling = new HashMap<>();
    private boolean taking;
    private boolean again;
    private boolean stopped;
    private long lastCall = -SPACING_MILLISECONDS;
    private long lastChange = -1;
    private String reason = "settled";

    Screenshots(AccessibilityService service, RecorderLink link) {
        this.service = service;
        this.link = link;
    }

    /** A connection asked for screenshots: one at once. Called on any thread. */
    void requested() {
        main.post(() -> want("requested"));
    }

    /** Called on the main thread for every event while a connection wants screenshots. */
    void eventArrived(AccessibilityEvent event) {
        int type = event.getEventType();
        if (type != AccessibilityEvent.TYPE_WINDOWS_CHANGED
                && type != AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED
                && type != AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED
                && type != AccessibilityEvent.TYPE_VIEW_SCROLLED) {
            return;
        }
        lastChange = Math.max(lastChange, event.getEventTime());
        int window = event.getWindowId();
        Runnable previous = settling.remove(window);
        if (previous != null) {
            main.removeCallbacks(previous);
        }
        Runnable settled = () -> {
            settling.remove(window);
            want("settled");
        };
        settling.put(window, settled);
        main.postDelayed(settled, SETTLE_MILLISECONDS);
    }

    void stop() {
        main.post(() -> {
            stopped = true;
            for (Runnable settled : settling.values()) {
                main.removeCallbacks(settled);
            }
            settling.clear();
            main.removeCallbacks(takeNow);
        });
        encoder.shutdown();
    }

    // On the main thread.
    private void want(String why) {
        if (stopped || !link.wantsScreenshots()) {
            return;
        }
        if ("requested".equals(why)) {
            reason = why;
        }
        if (taking) {
            again = true;
            return;
        }
        main.removeCallbacks(takeNow);
        long wait = lastCall + SPACING_MILLISECONDS - SystemClock.uptimeMillis();
        if (wait > 0) {
            main.postDelayed(takeNow, wait);
        } else {
            take();
        }
    }

    // On the main thread.
    private void take() {
        if (stopped || !link.wantsScreenshots()) {
            return;
        }
        taking = true;
        again = false;
        lastCall = SystemClock.uptimeMillis();
        String why = reason;
        reason = "settled";
        long change = lastChange;
        long requested = System.nanoTime();
        try {
            service.takeScreenshot(Display.DEFAULT_DISPLAY, encoder, new AccessibilityService.TakeScreenshotCallback() {
                @Override
                public void onSuccess(AccessibilityService.ScreenshotResult result) {
                    long captured = System.nanoTime();
                    send(result, why, change, requested, captured);
                    main.post(Screenshots.this::finished);
                }

                @Override
                public void onFailure(int errorCode) {
                    long failed = System.nanoTime();
                    refused(errorCode, why, requested, failed);
                    main.post(() -> {
                        if (errorCode == AccessibilityService.ERROR_TAKE_SCREENSHOT_INTERVAL_TIME_SHORT) {
                            again = true;
                        }
                        finished();
                    });
                }
            });
        } catch (RejectedExecutionException | IllegalStateException exception) {
            // Stopped meanwhile.
            taking = false;
        }
    }

    // On the main thread.
    private void finished() {
        taking = false;
        if (again) {
            want("settled");
        }
    }

    // On the encoder thread.
    private void send(AccessibilityService.ScreenshotResult result, String why, long change, long requested, long captured) {
        HardwareBuffer buffer = result.getHardwareBuffer();
        ColorSpace colourSpace = result.getColorSpace();
        Bitmap copy = null;
        try {
            Bitmap wrapped = Bitmap.wrapHardwareBuffer(buffer, colourSpace);
            if (wrapped == null) {
                Log.w(TAG, "A screenshot could not be read");
                return;
            }
            copy = wrapped.copy(Bitmap.Config.ARGB_8888, false);
            wrapped.recycle();
            if (copy == null) {
                Log.w(TAG, "A screenshot could not be copied");
                return;
            }
            ColorSpace srgb = ColorSpace.get(ColorSpace.Named.SRGB);
            boolean converted = false;
            if (colourSpace != null && !srgb.equals(colourSpace)) {
                Bitmap target = Bitmap.createBitmap(copy.getWidth(), copy.getHeight(), Bitmap.Config.ARGB_8888, true, srgb);
                new Canvas(target).drawBitmap(copy, 0, 0, null);
                copy.recycle();
                copy = target;
                converted = true;
            }
            ByteArrayOutputStream png = new ByteArrayOutputStream(1 << 20);
            if (!copy.compress(Bitmap.CompressFormat.PNG, 100, png)) {
                Log.w(TAG, "A screenshot could not be written as PNG");
                return;
            }
            long encoded = System.nanoTime();

            JSONObject message = new JSONObject();
            message.put("type", "screenshot");
            message.put("reason", why);
            message.put("timestampMillis", result.getTimestamp());
            message.put("requested", requested);
            message.put("captured", captured);
            message.put("encoded", encoded);
            message.put("lastChangeMillis", change);
            message.put("width", copy.getWidth());
            message.put("height", copy.getHeight());
            message.put("colourSpace", colourSpace == null ? "unknown" : colourSpace.getName());
            message.put("converted", converted);
            // The PNG is put in by hand: org.json writes each "/" of the
            // base64 as "\/", a third larger and slower to read.
            String head = message.toString();
            String line = head.substring(0, head.length() - 1)
                    + ",\"png\":\"" + Base64.encodeToString(png.toByteArray(), Base64.NO_WRAP) + "\"}";
            link.sendScreenshot(line);
        } catch (JSONException | RuntimeException exception) {
            Log.w(TAG, "A screenshot could not be sent", exception);
        } finally {
            if (copy != null) {
                copy.recycle();
            }
            buffer.close();
        }
    }

    // On the encoder thread.
    private void refused(int code, String why, long requested, long failed) {
        try {
            JSONObject message = new JSONObject();
            message.put("type", "screenshot-failed");
            message.put("reason", why);
            message.put("code", code);
            message.put("requested", requested);
            message.put("failed", failed);
            link.sendScreenshot(message.toString());
        } catch (JSONException exception) {
            Log.w(TAG, "A screenshot's failure could not be sent", exception);
        }
    }
}
