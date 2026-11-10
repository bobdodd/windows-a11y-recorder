package io.github.bobdodd.a11yrecorder.companion;

import android.graphics.Rect;
import android.os.Build;
import android.os.Parcelable;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityRecord;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.List;

/**
 * An accessibility event as the companion sends it to the recorder, slice 7
 * of docs/architecture/android-implementation-plan.md, step 7a: every field
 * the event carries, its extra records, and the item it came from.
 *
 * The event is read while the service's onAccessibilityEvent runs, so the
 * fields are those Android delivered. The source item is fetched then too,
 * with getSource(), and holds the item's state when it was fetched, which
 * can be later than the event.
 *
 * The names match the recorder's AndroidAccessibilityEvent and
 * AndroidAccessibilityNode (src/Recorder.Contracts/AndroidAccessibilityRecords.cs),
 * which must agree with this file. A field Android does not have on the
 * phone's version is null.
 */
final class AccessibilityEventJson {
    private AccessibilityEventJson() {
    }

    /**
     * The event's message, without the per-connection "index", which the
     * link adds as it sends it.
     *
     * @param received the phone's System.nanoTime() when the service was given the event
     */
    static JSONObject event(AccessibilityEvent event, long received) throws JSONException {
        JSONObject message = new JSONObject();
        message.put("type", "accessibility-event");
        int type = event.getEventType();
        message.put("eventType", type);
        message.put("eventTypeName", AccessibilityEvent.eventTypeToString(type));
        // SystemClock.uptimeMillis(), set by AccessibilityManager when the
        // app sent it; the clock System.nanoTime() is based on.
        message.put("eventTimeMilliseconds", event.getEventTime());
        message.put("receivedPhoneNanoseconds", received);
        message.put("packageName", text(event.getPackageName()));
        message.put("className", text(event.getClassName()));
        message.put("windowId", event.getWindowId());
        message.put("displayId", Build.VERSION.SDK_INT >= 33 ? event.getDisplayId() : JSONObject.NULL);
        message.put("text", texts(event.getText()));
        message.put("contentDescription", text(event.getContentDescription()));
        message.put("beforeText", text(event.getBeforeText()));
        message.put("contentChangeTypes", event.getContentChangeTypes());
        message.put("windowChanges", event.getWindowChanges());
        message.put("speechStateChangeTypes",
                Build.VERSION.SDK_INT >= 33 ? event.getSpeechStateChangeTypes() : JSONObject.NULL);
        message.put("action", event.getAction());
        message.put("movementGranularity", event.getMovementGranularity());
        recordFields(message, event);
        message.put("scrollDeltaX", event.getScrollDeltaX());
        message.put("scrollDeltaY", event.getScrollDeltaY());
        message.put("accessibilityDataSensitive",
                Build.VERSION.SDK_INT >= 34 ? event.isAccessibilityDataSensitive() : JSONObject.NULL);
        Parcelable data = event.getParcelableData();
        message.put("parcelableDataClass", data == null ? JSONObject.NULL : data.getClass().getName());

        JSONArray records = new JSONArray();
        for (int index = 0; index < event.getRecordCount(); index++) {
            AccessibilityRecord record = event.getRecord(index);
            JSONObject item = new JSONObject();
            item.put("className", text(record.getClassName()));
            item.put("windowId", record.getWindowId());
            item.put("text", texts(record.getText()));
            item.put("contentDescription", text(record.getContentDescription()));
            item.put("beforeText", text(record.getBeforeText()));
            recordFields(item, record);
            records.put(item);
        }
        message.put("records", records);

        AccessibilityNodeInfo source = event.getSource();
        message.put("source", source == null ? JSONObject.NULL : node(source));
        message.put("handledPhoneNanoseconds", System.nanoTime());
        return message;
    }

    // The fields an event shares with its extra records.
    private static void recordFields(JSONObject message, AccessibilityRecord record) throws JSONException {
        message.put("fromIndex", record.getFromIndex());
        message.put("toIndex", record.getToIndex());
        message.put("addedCount", record.getAddedCount());
        message.put("removedCount", record.getRemovedCount());
        message.put("itemCount", record.getItemCount());
        message.put("currentItemIndex", record.getCurrentItemIndex());
        message.put("scrollX", record.getScrollX());
        message.put("scrollY", record.getScrollY());
        message.put("maxScrollX", record.getMaxScrollX());
        message.put("maxScrollY", record.getMaxScrollY());
        message.put("checked", record.isChecked());
        message.put("enabled", record.isEnabled());
        message.put("password", record.isPassword());
        message.put("fullScreen", record.isFullScreen());
        message.put("scrollable", record.isScrollable());
    }

    /** One item, as the tree of slice 7 records it, without its place in the tree. */
    @SuppressWarnings("deprecation")
    static JSONObject node(AccessibilityNodeInfo node) throws JSONException {
        JSONObject item = new JSONObject();
        item.put("windowId", node.getWindowId());
        item.put("className", text(node.getClassName()));
        item.put("packageName", text(node.getPackageName()));
        item.put("viewId", text(node.getViewIdResourceName()));
        item.put("uniqueId", Build.VERSION.SDK_INT >= 33 ? text(node.getUniqueId()) : JSONObject.NULL);
        item.put("text", text(node.getText()));
        item.put("contentDescription", text(node.getContentDescription()));
        item.put("stateDescription", text(node.getStateDescription()));
        item.put("hint", text(node.getHintText()));
        item.put("paneTitle", text(node.getPaneTitle()));
        item.put("tooltip", text(node.getTooltipText()));
        item.put("error", text(node.getError()));
        item.put("containerTitle", Build.VERSION.SDK_INT >= 34 ? text(node.getContainerTitle()) : JSONObject.NULL);
        item.put("supplementalDescription",
                Build.VERSION.SDK_INT >= 36 ? text(node.getSupplementalDescription()) : JSONObject.NULL);

        Rect bounds = new Rect();
        node.getBoundsInScreen(bounds);
        JSONObject box = new JSONObject();
        box.put("left", bounds.left);
        box.put("top", bounds.top);
        box.put("right", bounds.right);
        box.put("bottom", bounds.bottom);
        item.put("bounds", box);

        item.put("childCount", node.getChildCount());
        item.put("drawingOrder", node.getDrawingOrder());
        // isChecked() gave way to getChecked() in Android 16 (API 36),
        // which also tells a partly checked item.
        String checked;
        if (Build.VERSION.SDK_INT >= 36) {
            int state = node.getChecked();
            checked = state == AccessibilityNodeInfo.CHECKED_STATE_TRUE ? "checked"
                    : state == AccessibilityNodeInfo.CHECKED_STATE_PARTIAL ? "partial" : "unchecked";
        } else {
            checked = node.isChecked() ? "checked" : "unchecked";
        }
        item.put("checked", checked);
        item.put("expandedState", Build.VERSION.SDK_INT >= 36 ? node.getExpandedState() : JSONObject.NULL);
        item.put("importantForAccessibility", node.isImportantForAccessibility());
        item.put("visibleToUser", node.isVisibleToUser());
        item.put("accessibilityFocused", node.isAccessibilityFocused());
        item.put("focused", node.isFocused());
        item.put("focusable", node.isFocusable());
        item.put("screenReaderFocusable", node.isScreenReaderFocusable());
        item.put("selected", node.isSelected());
        item.put("checkable", node.isCheckable());
        item.put("enabled", node.isEnabled());
        item.put("clickable", node.isClickable());
        item.put("longClickable", node.isLongClickable());
        item.put("contextClickable", node.isContextClickable());
        item.put("scrollable", node.isScrollable());
        item.put("editable", node.isEditable());
        item.put("password", node.isPassword());
        item.put("heading", node.isHeading());
        item.put("showingHint", node.isShowingHintText());
        item.put("multiLine", node.isMultiLine());
        item.put("dismissable", node.isDismissable());
        item.put("contentInvalid", node.isContentInvalid());
        item.put("textEntryKey", node.isTextEntryKey());
        item.put("fieldRequired", Build.VERSION.SDK_INT >= 36 ? node.isFieldRequired() : JSONObject.NULL);
        item.put("inputType", node.getInputType());
        item.put("maxTextLength", node.getMaxTextLength());
        item.put("liveRegion", node.getLiveRegion());
        item.put("textSelectionStart", node.getTextSelectionStart());
        item.put("textSelectionEnd", node.getTextSelectionEnd());

        JSONArray actions = new JSONArray();
        for (AccessibilityNodeInfo.AccessibilityAction action : node.getActionList()) {
            JSONObject entry = new JSONObject();
            entry.put("id", action.getId());
            entry.put("label", text(action.getLabel()));
            actions.put(entry);
        }
        item.put("actions", actions);

        AccessibilityNodeInfo.CollectionInfo collection = node.getCollectionInfo();
        if (collection == null) {
            item.put("collectionInfo", JSONObject.NULL);
        } else {
            JSONObject entry = new JSONObject();
            entry.put("rowCount", collection.getRowCount());
            entry.put("columnCount", collection.getColumnCount());
            entry.put("hierarchical", collection.isHierarchical());
            entry.put("selectionMode", collection.getSelectionMode());
            item.put("collectionInfo", entry);
        }

        AccessibilityNodeInfo.CollectionItemInfo collectionItem = node.getCollectionItemInfo();
        if (collectionItem == null) {
            item.put("collectionItemInfo", JSONObject.NULL);
        } else {
            JSONObject entry = new JSONObject();
            entry.put("rowIndex", collectionItem.getRowIndex());
            entry.put("rowSpan", collectionItem.getRowSpan());
            entry.put("columnIndex", collectionItem.getColumnIndex());
            entry.put("columnSpan", collectionItem.getColumnSpan());
            entry.put("selected", collectionItem.isSelected());
            entry.put("rowTitle", Build.VERSION.SDK_INT >= 33 ? text(collectionItem.getRowTitle()) : JSONObject.NULL);
            entry.put("columnTitle", Build.VERSION.SDK_INT >= 33 ? text(collectionItem.getColumnTitle()) : JSONObject.NULL);
            item.put("collectionItemInfo", entry);
        }

        AccessibilityNodeInfo.RangeInfo range = node.getRangeInfo();
        if (range == null) {
            item.put("rangeInfo", JSONObject.NULL);
        } else {
            JSONObject entry = new JSONObject();
            entry.put("type", range.getType());
            entry.put("min", number(range.getMin()));
            entry.put("max", number(range.getMax()));
            entry.put("current", number(range.getCurrent()));
            item.put("rangeInfo", entry);
        }

        return item;
    }

    // JSONObject.put removes a name given null, so a missing value is
    // JSONObject.NULL, which is written as null.
    private static Object text(CharSequence value) {
        return value == null ? JSONObject.NULL : value.toString();
    }

    private static JSONArray texts(List<CharSequence> values) {
        JSONArray array = new JSONArray();
        for (CharSequence value : values) {
            array.put(value == null ? "" : value.toString());
        }
        return array;
    }

    // JSON has no infinity or NaN, which a range can hold.
    private static Object number(float value) {
        return Float.isNaN(value) || Float.isInfinite(value) ? JSONObject.NULL : (double) value;
    }
}
