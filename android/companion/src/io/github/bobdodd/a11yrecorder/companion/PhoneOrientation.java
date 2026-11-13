package io.github.bobdodd.a11yrecorder.companion;

import android.content.Context;
import android.hardware.SensorManager;
import android.os.Handler;
import android.os.Looper;
import android.view.OrientationEventListener;

import org.json.JSONException;
import org.json.JSONObject;

/**
 * How the phone itself is held, from its accelerometer, for the
 * orientation check (docs/architecture/android-checks.md, step 15): a
 * locked app keeps the display from turning, so the display's rotation
 * alone cannot show that the phone was turned.
 *
 * Android's OrientationEventListener gives the phone's angle in degrees:
 * 0 in its natural position, 90 with its left side at the top, 180 upside
 * down and 270 with its right side at the top, or ORIENTATION_UNKNOWN when
 * the phone is too close to flat to tell. The angle is placed in the
 * nearest quarter only when it is within {@link #BAND} degrees of it, so a
 * phone held between two quarters keeps the last one, and a message is sent
 * only when the quarter, or flat, changes. The listener runs only while a
 * connection asks for it.
 *
 * Each message is "orientation", with "reason" ("first", "changed" or
 * "unavailable", when the phone has no sensor to tell), "orientation" (0,
 * 90, 180 or 270, or -1 for flat or unknown), "degrees" (the angle that
 * placed it, or -1) and "time", the phone's System.nanoTime() when the
 * reading arrived.
 */
final class PhoneOrientation {
    /** How near a quarter the angle must be to be placed in it, in degrees. */
    static final int BAND = 30;

    /** Flat, or too near flat to tell. */
    static final int FLAT = -1;

    interface Sink {
        void orientation(JSONObject message);
    }

    private final Context context;
    private final Sink sink;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    // Used on the main thread only.
    private OrientationEventListener listener;
    private boolean sentFirst;
    private int quarter = Integer.MIN_VALUE;
    // The last message sent, for lastAsFirst(); written on the main thread.
    private volatile String last;

    PhoneOrientation(Context context, Sink sink) {
        this.context = context;
        this.sink = sink;
    }

    /** Starts the listener, if it is not running; the first reading is sent as "first". */
    void start() {
        mainHandler.post(() -> {
            if (listener != null) {
                return;
            }
            listener = new OrientationEventListener(context, SensorManager.SENSOR_DELAY_NORMAL) {
                @Override
                public void onOrientationChanged(int degrees) {
                    reading(degrees, System.nanoTime());
                }
            };
            sentFirst = false;
            quarter = Integer.MIN_VALUE;
            last = null;
            if (!listener.canDetectOrientation()) {
                listener = null;
                send("unavailable", FLAT, FLAT, System.nanoTime());
                return;
            }
            listener.enable();
        });
    }

    /** Stops the listener, if it is running. */
    void stop() {
        mainHandler.post(() -> {
            if (listener != null) {
                listener.disable();
                listener = null;
            }
            last = null;
        });
    }

    /**
     * The last message sent, as "first" (or "unavailable") at the time of
     * asking, for a connection that asks after the listener was already
     * started; null before the first reading.
     */
    JSONObject lastAsFirst() {
        String line = last;
        if (line == null) {
            return null;
        }
        try {
            JSONObject message = new JSONObject(line);
            if (!"unavailable".equals(message.optString("reason"))) {
                message.put("reason", "first");
            }
            // The phone is still held as it was, so it is sent as now,
            // never earlier than the connection that asks.
            message.put("time", System.nanoTime());
            return message;
        } catch (JSONException exception) {
            return null;
        }
    }

    // On the main thread.
    private void reading(int degrees, long time) {
        int placed = place(degrees, quarter);
        if (sentFirst && placed == quarter) {
            return;
        }
        String reason = sentFirst ? "changed" : "first";
        sentFirst = true;
        quarter = placed;
        send(reason, placed, degrees < 0 ? FLAT : degrees, time);
    }

    /**
     * The quarter an angle is in: the nearest of 0, 90, 180 and 270 when
     * the angle is within BAND degrees of it, the previous quarter when it
     * is not (the nearest, after none or flat), and FLAT for an unknown
     * angle.
     */
    static int place(int degrees, int previous) {
        if (degrees == OrientationEventListener.ORIENTATION_UNKNOWN || degrees < 0) {
            return FLAT;
        }
        int nearest = ((degrees + 45) / 90 % 4) * 90;
        int off = Math.abs(degrees - nearest);
        off = Math.min(off, 360 - off);
        if (off <= BAND) {
            return nearest;
        }
        return previous == Integer.MIN_VALUE || previous == FLAT ? nearest : previous;
    }

    private void send(String reason, int orientation, int degrees, long time) {
        try {
            JSONObject message = new JSONObject();
            message.put("type", "orientation");
            message.put("reason", reason);
            message.put("orientation", orientation);
            message.put("degrees", degrees);
            message.put("time", time);
            last = message.toString();
            sink.orientation(message);
        } catch (JSONException ignored) {
            // Not thrown for these values.
        }
    }
}
