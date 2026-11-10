package io.github.bobdodd.a11yrecorder.companion;

import android.accessibilityservice.AccessibilityServiceInfo;
import android.content.Context;
import android.media.AudioManager;
import android.view.accessibility.AccessibilityManager;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.List;

/**
 * Raises the phone's Accessibility volume to at least a share of its
 * range, for the recorder (android-implementation-plan.md, slice 4, step
 * 4c). The phone's sound is captured at the level the phone plays it, and
 * check 1 found TalkBack too quiet to use below about 80%.
 *
 * Android lets only a caller with CHANGE_ACCESSIBILITY_VOLUME, or an
 * accessibility service that is on, change this volume (AudioService,
 * canChangeAccessibilityVolume); adb's shell has neither, so the app does
 * it. The volume is separate from the media volume only while a service
 * that asks for it (FLAG_ENABLE_ACCESSIBILITY_VOLUME), such as TalkBack,
 * is on; otherwise Android treats it as the media volume (AudioService,
 * updateStreamVolumeAlias), and setting it would raise the media volume
 * instead. So the app changes it only while it is separate.
 *
 * The volume is raised only, never lowered, and is left where it was set
 * when the recording ends (decided by the owner on 2026-11-09).
 */
final class AccessibilityVolume {
    private AccessibilityVolume() {
    }

    /** The enabled services that ask for a separate Accessibility volume, by component name. */
    static JSONArray servicesUsingIt(Context context) {
        JSONArray names = new JSONArray();
        AccessibilityManager accessibility = context.getSystemService(AccessibilityManager.class);
        if (accessibility == null) {
            return names;
        }
        List<AccessibilityServiceInfo> services =
                accessibility.getEnabledAccessibilityServiceList(AccessibilityServiceInfo.FEEDBACK_ALL_MASK);
        for (AccessibilityServiceInfo service : services) {
            if ((service.flags & AccessibilityServiceInfo.FLAG_ENABLE_ACCESSIBILITY_VOLUME) != 0) {
                names.put(service.getId());
            }
        }
        return names;
    }

    /**
     * Raises the volume to at least minimumPercent of its largest step, if
     * it is separate and lower, and says what it found and did.
     */
    static JSONObject raise(Context context, int minimumPercent, String reason) throws JSONException {
        JSONObject message = new JSONObject();
        message.put("type", "accessibility-volume");
        message.put("reason", reason);
        message.put("minimumPercent", minimumPercent);

        JSONArray services = servicesUsingIt(context);
        boolean separate = services.length() > 0;
        message.put("separate", separate);
        message.put("services", services);

        AudioManager audio = context.getSystemService(AudioManager.class);
        if (audio == null) {
            message.put("outcome", "failed");
            message.put("detail", "The phone has no audio service.");
            message.put("time", System.nanoTime());
            return message;
        }

        int stream = AudioManager.STREAM_ACCESSIBILITY;
        int max = audio.getStreamMaxVolume(stream);
        int before = audio.getStreamVolume(stream);
        // The smallest step at or above the share asked for.
        int target = (int) Math.ceil(max * (minimumPercent / 100.0));
        message.put("max", max);
        message.put("before", before);
        message.put("target", target);

        String outcome;
        String detail = null;
        if (!separate) {
            outcome = "not-separate";
        } else if (before >= target) {
            outcome = "already";
        } else {
            try {
                audio.setStreamVolume(stream, target, 0);
                // Android ignores a caller it does not allow, without an
                // error, so the volume is read again to tell.
                outcome = audio.getStreamVolume(stream) >= target ? "raised" : "refused";
            } catch (SecurityException exception) {
                outcome = "refused";
                detail = exception.getMessage();
            }
        }

        message.put("after", audio.getStreamVolume(stream));
        message.put("outcome", outcome);
        if (detail != null) {
            message.put("detail", detail);
        }
        message.put("time", System.nanoTime());
        return message;
    }
}
