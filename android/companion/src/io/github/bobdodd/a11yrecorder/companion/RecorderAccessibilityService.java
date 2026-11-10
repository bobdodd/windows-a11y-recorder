package io.github.bobdodd.a11yrecorder.companion;

import android.accessibilityservice.AccessibilityService;
import android.util.Log;
import android.view.accessibility.AccessibilityEvent;

import org.json.JSONException;
import org.json.JSONObject;

/**
 * The companion's accessibility service. While it is on, it answers the
 * recorder on a local socket (see {@link RecorderLink}): who it is, the
 * phone's clock, and the display's rotation and size. Slice 2 of
 * docs/architecture/android-implementation-plan.md. From slice 7, step 7a,
 * it sends every accessibility event it receives to each connection that
 * asked for them (see {@link AccessibilityEventJson}); with none asking,
 * the events are not read. From step 7b, it takes snapshots of the
 * accessibility tree for each connection that asked for them (see
 * {@link AccessibilityTrees}).
 *
 * The service performs no actions, has no internet permission and stores
 * nothing on the phone.
 */
public final class RecorderAccessibilityService extends AccessibilityService {
    private static final String TAG = "A11yRecorderCompanion";

    private RecorderLink link;
    private AccessibilityTrees trees;

    @Override
    protected void onServiceConnected() {
        super.onServiceConnected();
        if (link == null) {
            link = new RecorderLink(this);
            trees = new AccessibilityTrees(this, link);
            link.setTreeRequests(trees::requested);
            link.start();
        }
    }

    @Override
    public void onAccessibilityEvent(AccessibilityEvent event) {
        // Read first, so the time is as close as possible to delivery.
        long received = System.nanoTime();
        RecorderLink current = link;
        if (current == null) {
            return;
        }
        AccessibilityTrees currentTrees = trees;
        if (currentTrees != null && current.wantsTrees()) {
            currentTrees.eventArrived(event);
        }
        if (!current.wantsEvents()) {
            return;
        }

        // The event is read here, while Android's copy is the service's;
        // it is sent from the link's own thread.
        JSONObject message;
        try {
            message = AccessibilityEventJson.event(event, received);
        } catch (JSONException | RuntimeException exception) {
            Log.w(TAG, "An accessibility event could not be read", exception);
            return;
        }
        current.sendEvent(message);
    }

    @Override
    public void onInterrupt() {
        // Nothing to stop: the service gives no feedback of its own.
    }

    @Override
    public void onDestroy() {
        if (trees != null) {
            trees.stop();
            trees = null;
        }
        if (link != null) {
            link.stop();
            link = null;
        }
        super.onDestroy();
    }
}
