package io.github.bobdodd.a11yrecorder.companion;

import android.os.Bundle;
import android.os.SystemClock;
import android.view.accessibility.AccessibilityNodeInfo;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayDeque;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Supplier;
import java.util.regex.Pattern;

/** One bounded, text-free read of size evidence from the selected active window. */
final class TextRendering {
    private static final String EXTRA = AccessibilityNodeInfo.EXTRA_DATA_RENDERING_INFO_KEY;
    private static final Pattern PACKAGE = Pattern.compile("[a-zA-Z][a-zA-Z0-9_]*(?:\\.[a-zA-Z][a-zA-Z0-9_]*)*");
    private static final int MAX_NODES = 512;
    // Checked between accessibility calls. A call already in Android may complete later.
    private static final long BUDGET_MS = 2000;

    private final Supplier<AccessibilityNodeInfo> activeRoot;
    private final AtomicBoolean reading = new AtomicBoolean();

    TextRendering(Supplier<AccessibilityNodeInfo> activeRoot) {
        this.activeRoot = activeRoot;
    }

    static boolean validPackage(Object value) {
        return value instanceof String && ((String) value).length() <= 255
                && PACKAGE.matcher((String) value).matches();
    }

    JSONObject read(String packageName) throws JSONException {
        if (!reading.compareAndSet(false, true)) {
            return reply(packageName, "busy", new JSONArray());
        }
        try {
            AccessibilityNodeInfo root = activeRoot.get();
            if (!inPackage(root, packageName)) {
                return reply(packageName, "not-active", new JSONArray());
            }
            int windowId = root.getWindowId();
            JSONArray nodes = new JSONArray();
            boolean complete = walk(root, packageName, windowId, nodes);
            AccessibilityNodeInfo current = activeRoot.get();
            if (!inPackage(current, packageName) || current.getWindowId() != windowId) {
                return reply(packageName, "window-changed", new JSONArray());
            }
            return reply(packageName, complete ? "complete" : "partial", nodes);
        } catch (RuntimeException exception) {
            return reply(packageName, "unavailable", new JSONArray());
        } finally {
            reading.set(false);
        }
    }

    private static boolean walk(AccessibilityNodeInfo root, String packageName, int windowId,
                                JSONArray nodes) throws JSONException {
        long deadline = SystemClock.uptimeMillis() + BUDGET_MS;
        int examined = 1;
        boolean complete = true;
        ArrayDeque<Step> pending = new ArrayDeque<>();
        pending.push(new Step(root, nodes));
        while (!pending.isEmpty()) {
            Step step = pending.peek();
            if (step.child >= step.node.getChildCount()) {
                pending.pop();
                continue;
            }
            if (examined >= MAX_NODES || SystemClock.uptimeMillis() >= deadline) {
                return false;
            }
            examined++;
            AccessibilityNodeInfo child = step.node.getChild(step.child++);
            if (child == null) {
                complete = false;
            } else if (inPackage(child, packageName) && child.getWindowId() == windowId) {
                pending.push(new Step(child, nodes));
            }
        }
        return complete;
    }

    private static final class Step {
        final AccessibilityNodeInfo node;
        int child;

        Step(AccessibilityNodeInfo node, JSONArray nodes) throws JSONException {
            this.node = node;
            // Android can expose size evidence even for a node it marks not visible.
            nodes.put(size(node, nodes.length()));
        }
    }

    private static boolean inPackage(AccessibilityNodeInfo node, String packageName) {
        return node != null && packageName.contentEquals(
                node.getPackageName() == null ? "" : node.getPackageName());
    }

    private static JSONObject size(AccessibilityNodeInfo node, int index) throws JSONException {
        JSONObject item = new JSONObject().put("index", index);
        String viewId = node.getViewIdResourceName();
        item.put("viewId", viewId == null ? JSONObject.NULL : viewId);
        item.put("visibleToUser", node.isVisibleToUser());
        String status = "unavailable";
        try {
            if (!node.getAvailableExtraData().contains(EXTRA)) {
                status = "not-advertised";
            } else if (node.refreshWithExtraData(EXTRA, new Bundle())) {
                AccessibilityNodeInfo.ExtraRenderingInfo info = node.getExtraRenderingInfo();
                if (info != null) {
                    float px = info.getTextSizeInPx();
                    if (Float.isFinite(px) && px >= 0) {
                        int unit = info.getTextSizeUnit();
                        item.put("textSizePx", (double) px);
                        if (unit >= 0) {
                            item.put("textSizeUnit", unit);
                        }
                        status = "available";
                    }
                }
            }
        } catch (RuntimeException ignored) {
            // A stale or unsupported item need not hide evidence from its neighbours.
        }
        return item.put("status", status);
    }

    private static JSONObject reply(String packageName, String status, JSONArray nodes) throws JSONException {
        return new JSONObject().put("type", "text-rendering").put("packageName", packageName)
                .put("status", status).put("nodes", nodes);
    }
}
