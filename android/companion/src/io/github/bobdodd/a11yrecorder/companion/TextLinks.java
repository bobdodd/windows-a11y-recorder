package io.github.bobdodd.a11yrecorder.companion;

import android.graphics.RectF;
import android.os.Bundle;
import android.os.Parcelable;
import android.text.Spanned;
import android.text.style.ClickableSpan;
import android.text.style.URLSpan;
import android.view.accessibility.AccessibilityNodeInfo;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.Arrays;
import java.util.List;

/**
 * The links in an item's text, docs/architecture/android-checks.md, step 29:
 * where each is in the text, what kind it is, and, when asked, where its
 * characters are on the screen.
 *
 * getText() gives the item's text with its ClickableSpans and URLSpans
 * replaced by ones an accessibility service can click, so the ranges are
 * read from the spans. A link's place on the screen is asked of the app
 * with refreshWithExtraData and EXTRA_DATA_TEXT_CHARACTER_LOCATION_KEY,
 * which gives a box for each character in screen pixels, null for one
 * that is off the screen, and is changed by the display's magnification.
 * That is a call to the app, so it is made only for an item with links,
 * only where asked (the tree snapshots, on their own thread, and not an
 * event's source item, read on the service's main thread), and on a copy
 * of the item, so the item being read is not changed by it.
 *
 * The names match the recorder's AndroidTextLink
 * (src/Recorder.Contracts/AndroidAccessibilityRecords.cs), which must agree
 * with this file.
 */
final class TextLinks {
    /** Not asked for, as for an event's source item. */
    static final String NOT_ASKED = "not-asked";
    /** The item does not offer its characters' places. */
    static final String NOT_OFFERED = "not-offered";
    /** Asked for, and none came back, as when the item has gone. */
    static final String NOT_RETURNED = "not-returned";
    /** The item's text changed before its characters' places came back. */
    static final String TEXT_CHANGED = "text-changed";
    /** The links span more characters than one request may ask for. */
    static final String TOO_LONG = "too-long";
    /** The places came back; a character off the screen has none. */
    static final String LOCATED = "located";

    // AccessibilityNodeInfo.EXTRA_DATA_TEXT_CHARACTER_LOCATION_ARG_MAX_LENGTH,
    // which is not in every Android version the companion runs on.
    private static final int MAX_LENGTH = 20000;

    private TextLinks() {
    }

    /**
     * The links in the item's text, in the text's order, or an empty list
     * when it has none.
     *
     * @param locate whether to ask the app where each link's characters are
     */
    @SuppressWarnings("deprecation")
    static JSONArray links(AccessibilityNodeInfo node, boolean locate) throws JSONException {
        JSONArray links = new JSONArray();
        CharSequence text = node.getText();
        if (!(text instanceof Spanned)) {
            return links;
        }
        Spanned spanned = (Spanned) text;
        ClickableSpan[] spans = spanned.getSpans(0, spanned.length(), ClickableSpan.class);
        if (spans.length == 0) {
            return links;
        }
        Arrays.sort(spans, (a, b) -> {
            int byStart = Integer.compare(spanned.getSpanStart(a), spanned.getSpanStart(b));
            return byStart != 0 ? byStart : Integer.compare(spanned.getSpanEnd(a), spanned.getSpanEnd(b));
        });

        int first = Integer.MAX_VALUE;
        int last = -1;
        for (ClickableSpan span : spans) {
            int start = spanned.getSpanStart(span);
            int end = spanned.getSpanEnd(span);
            if (start < 0 || end <= start) {
                continue;
            }
            first = Math.min(first, start);
            last = Math.max(last, end);
        }

        // One request for every link's characters, from the first link's
        // start to the last one's end.
        String location = NOT_ASKED;
        Parcelable[] boxes = null;
        if (locate && last > first) {
            List<String> offered = node.getAvailableExtraData();
            if (offered == null || !offered.contains(AccessibilityNodeInfo.EXTRA_DATA_TEXT_CHARACTER_LOCATION_KEY)) {
                location = NOT_OFFERED;
            } else if (last - first > MAX_LENGTH) {
                location = TOO_LONG;
            } else {
                AccessibilityNodeInfo copy = new AccessibilityNodeInfo(node);
                Bundle arguments = new Bundle();
                arguments.putInt(AccessibilityNodeInfo.EXTRA_DATA_TEXT_CHARACTER_LOCATION_ARG_START_INDEX, first);
                arguments.putInt(AccessibilityNodeInfo.EXTRA_DATA_TEXT_CHARACTER_LOCATION_ARG_LENGTH, last - first);
                if (!copy.refreshWithExtraData(AccessibilityNodeInfo.EXTRA_DATA_TEXT_CHARACTER_LOCATION_KEY, arguments)) {
                    location = NOT_RETURNED;
                } else if (!text.toString().equals(String.valueOf(copy.getText()))) {
                    location = TEXT_CHANGED;
                } else {
                    boxes = copy.getExtras().getParcelableArray(AccessibilityNodeInfo.EXTRA_DATA_TEXT_CHARACTER_LOCATION_KEY);
                    location = boxes == null ? NOT_RETURNED : LOCATED;
                }
            }
        }

        for (ClickableSpan span : spans) {
            int start = spanned.getSpanStart(span);
            int end = spanned.getSpanEnd(span);
            if (start < 0 || end <= start) {
                continue;
            }
            JSONObject link = new JSONObject();
            link.put("start", start);
            link.put("end", end);
            boolean url = span instanceof URLSpan;
            link.put("kind", url ? "url" : "clickable");
            String address = url ? ((URLSpan) span).getURL() : null;
            link.put("url", address == null ? JSONObject.NULL : address);
            link.put("location", location);
            JSONArray lines = new JSONArray();
            int located = 0;
            if (boxes != null) {
                located = lines(boxes, start - first, end - first, lines);
            }
            link.put("charactersLocated", boxes == null ? JSONObject.NULL : located);
            link.put("boxes", lines);
            links.put(link);
        }
        return links;
    }

    /**
     * Joins the characters' boxes from one index to another into a box for
     * each line they are on, rounded out to whole pixels, and gives how
     * many of the characters had a box. A character starts a new line when
     * its middle is above or below the line so far, or it starts left of
     * where the line so far ends, as where the text wraps.
     */
    private static int lines(Parcelable[] boxes, int from, int to, JSONArray lines) throws JSONException {
        int located = 0;
        RectF line = null;
        for (int index = Math.max(0, from); index < Math.min(to, boxes.length); index++) {
            if (!(boxes[index] instanceof RectF)) {
                continue;
            }
            RectF box = (RectF) boxes[index];
            if (box.height() <= 0) {
                continue;
            }
            located++;
            if (line != null && box.centerY() >= line.top && box.centerY() <= line.bottom
                    && box.left >= line.right - Math.max(1f, line.height() / 4f)) {
                line.union(box);
                continue;
            }
            if (line != null) {
                lines.put(rectangle(line));
            }
            line = new RectF(box);
        }
        if (line != null) {
            lines.put(rectangle(line));
        }
        return located;
    }

    private static JSONObject rectangle(RectF box) throws JSONException {
        JSONObject rectangle = new JSONObject();
        rectangle.put("left", (int) Math.floor(box.left));
        rectangle.put("top", (int) Math.floor(box.top));
        rectangle.put("right", (int) Math.ceil(box.right));
        rectangle.put("bottom", (int) Math.ceil(box.bottom));
        return rectangle;
    }
}
