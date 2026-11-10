package io.github.bobdodd.a11yrecorder.companion;

import android.accessibilityservice.AccessibilityService;
import android.graphics.Rect;
import android.os.Build;
import android.util.Log;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;

/**
 * Snapshots of the phone's accessibility tree, slice 7 of
 * docs/architecture/android-implementation-plan.md, step 7b.
 *
 * While a connection wants them, a snapshot is taken when the connection
 * asks, on every window change (TYPE_WINDOWS_CHANGED and
 * TYPE_WINDOW_STATE_CHANGED) and on every content change
 * (TYPE_WINDOW_CONTENT_CHANGED). Each snapshot lists every window, as
 * getWindows() gives them; a snapshot taken when asked or on a window
 * change holds the tree of every window, and one taken on a content change
 * holds the tree of each window that changed. So the latest tree of a
 * window is that of the latest snapshot that holds it.
 *
 * Snapshots are taken one at a time on their own thread, so the service's
 * events are not held up. A change that arrives while one is being taken
 * is covered by the next, which starts as soon as that one ends; each
 * snapshot says how many changes it covers. No change is passed over.
 *
 * Each window's items are listed in depth-first order, children in their
 * order, each with its parent's place in the list and its depth, so the
 * tree can be built again; labelled-by and traversal links are given as
 * places in the same list. The names match the recorder's
 * AndroidAccessibilityTree (src/Recorder.Contracts/AndroidAccessibilityRecords.cs),
 * which must agree with this file.
 */
final class AccessibilityTrees {
    /** The most items a window's list holds; more is marked "truncated". */
    static final int MAX_ITEMS = 10000;

    private static final String TAG = "A11yRecorderCompanion";

    private final AccessibilityService service;
    private final RecorderLink link;
    private final ExecutorService taker = Executors.newSingleThreadExecutor();
    private final Object lock = new Object();

    // Guarded by lock: what the next snapshot is to cover.
    private boolean scheduled;
    private boolean everyWindow;
    private final Set<Integer> windows = new HashSet<>();
    private final Set<String> reasons = new TreeSet<>();
    private int changes;
    private long firstChange = -1;
    private long lastChange = -1;

    AccessibilityTrees(AccessibilityService service, RecorderLink link) {
        this.service = service;
        this.link = link;
    }

    /** A connection asked for snapshots: one of every window, at once. */
    void requested() {
        add("requested", true, -1, -1);
    }

    /** Called on the main thread for every event while a connection wants snapshots. */
    void eventArrived(AccessibilityEvent event) {
        int type = event.getEventType();
        if (type == AccessibilityEvent.TYPE_WINDOWS_CHANGED
                || type == AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) {
            add("window-change", true, -1, event.getEventTime());
        } else if (type == AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED) {
            add("content-change", false, event.getWindowId(), event.getEventTime());
        }
    }

    void stop() {
        taker.shutdownNow();
    }

    private void add(String reason, boolean all, int windowId, long eventTime) {
        synchronized (lock) {
            reasons.add(reason);
            if (all) {
                everyWindow = true;
            } else {
                windows.add(windowId);
            }
            if (eventTime >= 0) {
                changes++;
                if (firstChange < 0) {
                    firstChange = eventTime;
                }
                lastChange = eventTime;
            }
            if (!scheduled) {
                scheduled = true;
                try {
                    taker.execute(this::take);
                } catch (RejectedExecutionException ignored) {
                    // Stopped meanwhile.
                    scheduled = false;
                }
            }
        }
    }

    private void take() {
        boolean all;
        Set<Integer> wanted;
        List<String> why;
        int covered;
        long first;
        long last;
        synchronized (lock) {
            // From here, a change is for the next snapshot.
            scheduled = false;
            all = everyWindow;
            wanted = new HashSet<>(windows);
            why = new ArrayList<>(reasons);
            covered = changes;
            first = firstChange;
            last = lastChange;
            everyWindow = false;
            windows.clear();
            reasons.clear();
            changes = 0;
            firstChange = -1;
            lastChange = -1;
        }
        if (!link.wantsTrees()) {
            return;
        }

        try {
            JSONObject message = new JSONObject();
            message.put("type", "accessibility-tree");
            message.put("reasons", new JSONArray(why));
            message.put("everyWindow", all);
            message.put("changes", covered);
            message.put("firstChangeEventTimeMilliseconds", first < 0 ? JSONObject.NULL : first);
            message.put("lastChangeEventTimeMilliseconds", last < 0 ? JSONObject.NULL : last);
            long started = System.nanoTime();
            message.put("startedPhoneNanoseconds", started);
            JSONArray list = new JSONArray();
            String problem = null;
            try {
                for (AccessibilityWindowInfo window : service.getWindows()) {
                    boolean withItems = all || wanted.contains(window.getId());
                    list.put(window(window, withItems));
                }
            } catch (RuntimeException exception) {
                // The windows changed under the snapshot, or the service is
                // being turned off; what was read is sent, and why.
                problem = exception.getClass().getSimpleName() + ": " + exception.getMessage();
            }
            message.put("windows", list);
            message.put("problem", problem == null ? JSONObject.NULL : problem);
            message.put("finishedPhoneNanoseconds", System.nanoTime());
            link.sendTree(message);
        } catch (JSONException exception) {
            Log.w(TAG, "A tree snapshot could not be written", exception);
        }
    }

    private static JSONObject window(AccessibilityWindowInfo window, boolean withItems) throws JSONException {
        JSONObject item = new JSONObject();
        item.put("id", window.getId());
        item.put("type", window.getType());
        item.put("typeName", typeName(window.getType()));
        item.put("layer", window.getLayer());
        CharSequence title = window.getTitle();
        item.put("title", title == null ? JSONObject.NULL : title.toString());
        Rect bounds = new Rect();
        window.getBoundsInScreen(bounds);
        JSONObject box = new JSONObject();
        box.put("left", bounds.left);
        box.put("top", bounds.top);
        box.put("right", bounds.right);
        box.put("bottom", bounds.bottom);
        item.put("bounds", box);
        item.put("active", window.isActive());
        item.put("focused", window.isFocused());
        item.put("accessibilityFocused", window.isAccessibilityFocused());
        item.put("pictureInPicture", window.isInPictureInPictureMode());
        item.put("displayId", window.getDisplayId());
        AccessibilityWindowInfo parent = window.getParent();
        item.put("parentId", parent == null ? JSONObject.NULL : parent.getId());
        item.put("transitionTimeMilliseconds",
                Build.VERSION.SDK_INT >= 34 ? window.getTransitionTimeMillis() : JSONObject.NULL);
        if (!withItems) {
            item.put("items", JSONObject.NULL);
            item.put("truncated", false);
            item.put("missingChildren", 0);
            return item;
        }

        AccessibilityNodeInfo root = Build.VERSION.SDK_INT >= 33
                ? window.getRoot(AccessibilityNodeInfo.FLAG_PREFETCH_DESCENDANTS_DEPTH_FIRST)
                : window.getRoot();
        items(item, root);
        return item;
    }

    // One step of the walk: an item, its parent's place and its depth.
    private static final class Step {
        final AccessibilityNodeInfo node;
        final int parent;
        final int depth;

        Step(AccessibilityNodeInfo node, int parent, int depth) {
            this.node = node;
            this.parent = parent;
            this.depth = depth;
        }
    }

    private static void items(JSONObject window, AccessibilityNodeInfo root) throws JSONException {
        List<AccessibilityNodeInfo> nodes = new ArrayList<>();
        List<JSONObject> written = new ArrayList<>();
        Map<AccessibilityNodeInfo, Integer> places = new HashMap<>();
        int missing = 0;
        boolean truncated = false;
        ArrayDeque<Step> stack = new ArrayDeque<>();
        if (root != null) {
            stack.push(new Step(root, -1, 0));
        }
        while (!stack.isEmpty()) {
            if (nodes.size() >= MAX_ITEMS) {
                truncated = true;
                break;
            }
            Step step = stack.pop();
            int place = nodes.size();
            JSONObject item = AccessibilityEventJson.node(step.node);
            item.put("index", place);
            item.put("parent", step.parent < 0 ? JSONObject.NULL : step.parent);
            item.put("depth", step.depth);
            nodes.add(step.node);
            written.add(item);
            places.put(step.node, place);

            // Pushed last child first, so the children come off in order.
            List<AccessibilityNodeInfo> children = new ArrayList<>();
            for (int index = 0; index < step.node.getChildCount(); index++) {
                AccessibilityNodeInfo child = Build.VERSION.SDK_INT >= 33
                        ? step.node.getChild(index, AccessibilityNodeInfo.FLAG_PREFETCH_DESCENDANTS_DEPTH_FIRST)
                        : step.node.getChild(index);
                if (child == null) {
                    missing++;
                } else {
                    children.add(child);
                }
            }
            Collections.reverse(children);
            for (AccessibilityNodeInfo child : children) {
                stack.push(new Step(child, place, step.depth + 1));
            }
        }

        // The links between items, as places in the list, once every place
        // is known. A traversal link to an item outside the list is null; a
        // label outside it is counted in "labeledByOutside".
        JSONArray list = new JSONArray();
        for (int place = 0; place < nodes.size(); place++) {
            AccessibilityNodeInfo node = nodes.get(place);
            JSONObject item = written.get(place);
            JSONArray labels = new JSONArray();
            int outside = 0;
            for (AccessibilityNodeInfo label : labeledBy(node)) {
                Object labelPlace = placeOf(places, label);
                if (labelPlace == JSONObject.NULL) {
                    outside++;
                } else {
                    labels.put(labelPlace);
                }
            }
            item.put("labeledBy", labels);
            item.put("labeledByOutside", outside);
            item.put("traversalBefore", placeOf(places, node.getTraversalBefore()));
            item.put("traversalAfter", placeOf(places, node.getTraversalAfter()));
            list.put(item);
        }
        window.put("items", list);
        window.put("truncated", truncated);
        window.put("missingChildren", missing);
    }

    // getLabeledByList() replaced getLabeledBy() in Android 16 (API 36),
    // allowing more than one label.
    @SuppressWarnings("deprecation")
    private static List<AccessibilityNodeInfo> labeledBy(AccessibilityNodeInfo node) {
        if (Build.VERSION.SDK_INT >= 36) {
            return node.getLabeledByList();
        }
        AccessibilityNodeInfo label = node.getLabeledBy();
        return label == null ? Collections.emptyList() : Collections.singletonList(label);
    }

    private static Object placeOf(Map<AccessibilityNodeInfo, Integer> places, AccessibilityNodeInfo node) {
        if (node == null) {
            return JSONObject.NULL;
        }
        Integer place = places.get(node);
        return place == null ? JSONObject.NULL : place;
    }

    private static String typeName(int type) {
        switch (type) {
            case AccessibilityWindowInfo.TYPE_APPLICATION:
                return "TYPE_APPLICATION";
            case AccessibilityWindowInfo.TYPE_INPUT_METHOD:
                return "TYPE_INPUT_METHOD";
            case AccessibilityWindowInfo.TYPE_SYSTEM:
                return "TYPE_SYSTEM";
            case AccessibilityWindowInfo.TYPE_ACCESSIBILITY_OVERLAY:
                return "TYPE_ACCESSIBILITY_OVERLAY";
            case AccessibilityWindowInfo.TYPE_SPLIT_SCREEN_DIVIDER:
                return "TYPE_SPLIT_SCREEN_DIVIDER";
            case 6:
                // AccessibilityWindowInfo.TYPE_MAGNIFICATION_OVERLAY, API 33.
                return "TYPE_MAGNIFICATION_OVERLAY";
            case 7:
                // AccessibilityWindowInfo.TYPE_WINDOW_CONTROL, API 36.
                return "TYPE_WINDOW_CONTROL";
            default:
                return "TYPE_" + type;
        }
    }
}
