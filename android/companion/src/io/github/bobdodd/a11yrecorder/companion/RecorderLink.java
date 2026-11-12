package io.github.bobdodd.a11yrecorder.companion;

import android.content.ContentResolver;
import android.content.Context;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.database.ContentObserver;
import android.hardware.display.DisplayManager;
import android.net.Uri;
import android.net.Credentials;
import android.net.LocalServerSocket;
import android.net.LocalSocket;
import android.net.LocalSocketAddress;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.Process;
import android.provider.Settings;
import android.util.Log;
import android.view.Display;
import android.view.Surface;
import android.view.accessibility.AccessibilityManager;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;

/**
 * The companion's link to the recorder: a local socket in Android's
 * abstract namespace, "a11yrecorder_companion", which the recorder reaches
 * with "adb forward tcp:0 localabstract:a11yrecorder_companion".
 *
 * Any app on the phone could connect to such a socket, so a connection is
 * kept only when its peer is adb's shell user (or root, on a phone where
 * adb runs as root); any other is closed without a word.
 *
 * Messages are lines of JSON, each with a "type". On connecting, the
 * companion sends "hello" and then "display". It answers each "clock"
 * request with its System.nanoTime() when the request was read and when the
 * reply was sent, and sends "display" again whenever the display's rotation
 * or size changes. From protocol 2 it answers an "accessibility-volume"
 * request by raising the phone's Accessibility volume to at least the share
 * asked for (see {@link AccessibilityVolume}), and, for a connection that
 * asked, does so again and says so whenever the volume becomes separate or
 * stops being separate, as when TalkBack is turned on or off during a
 * recording. From protocol 3 it answers an "accessibility-events" request
 * by sending that connection every accessibility event the service
 * receives from then on, each as an "accessibility-event" message with an
 * "index" counting from 1 on that connection (see
 * {@link AccessibilityEventJson}); a connection that did not ask, such as
 * the recorder's checks, is sent none. From protocol 4 it answers an
 * "accessibility-trees" request by sending that connection a snapshot of
 * every window's accessibility tree at once, and more on each window or
 * content change, each as an "accessibility-tree" message with its own
 * "index" counting from 1 on that connection (see
 * {@link AccessibilityTrees}). From protocol 5 it answers a "settings"
 * request, which lists settings by namespace ("secure", "system" or
 * "global") and name, by watching each with a ContentObserver and sending
 * that connection a "setting-changed" message, with the namespace, the
 * name and its System.nanoTime() when the notice came, each time one is
 * written. It sends no value: some of the settings cannot be read by an
 * app, so the recorder reads them with adb. From protocol 6 it answers a
 * "screenshots" request by sending that connection a screenshot of the
 * display at once, and another each time a window settles, each as a
 * "screenshot" message with the PNG in base64, or a "screenshot-failed"
 * message with Android's code when one is refused, each with its own
 * "index" counting from 1 on that connection (see {@link Screenshots}).
 * See docs/architecture/android-implementation-plan.md, slices 2, 4, 7
 * and 8, docs/architecture/android-checks.md, step 4a, and
 * src/Recorder.Collectors.Android/CompanionProtocol.cs, which must agree
 * with this file.
 */
final class RecorderLink {
    static final String SOCKET_NAME = "a11yrecorder_companion";
    static final int PROTOCOL = 6;
    private static final String TAG = "A11yRecorderCompanion";

    private final Context context;
    private final CopyOnWriteArrayList<Client> clients = new CopyOnWriteArrayList<>();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    // Display changes and accessibility events arrive on the main thread;
    // they are sent from this one, so a slow connection never holds up the
    // service.
    private final ExecutorService sender = Executors.newSingleThreadExecutor();
    // Screenshots are large, so they are sent from their own thread, and
    // the other messages do not wait behind them on this side.
    private final ExecutorService screenshotSender = Executors.newSingleThreadExecutor();
    private volatile boolean stopping;
    // Asks for a snapshot of every window; set before start().
    private volatile Runnable treeRequests;
    // Asks for a screenshot at once; set before start().
    private volatile Runnable screenshotRequests;
    private LocalServerSocket server;
    private Thread acceptThread;
    private DisplayManager displayManager;
    private DisplayManager.DisplayListener displayListener;
    private volatile String lastDisplay = "";
    private AccessibilityManager accessibilityManager;
    private AccessibilityManager.AccessibilityServicesStateChangeListener servicesListener;
    // Used on the sender thread only.
    private boolean lastSeparate;

    RecorderLink(Context context) {
        this.context = context;
    }

    void start() {
        try {
            server = new LocalServerSocket(SOCKET_NAME);
        } catch (IOException exception) {
            Log.w(TAG, "The recorder's socket could not be opened", exception);
            return;
        }

        displayManager = context.getSystemService(DisplayManager.class);
        displayListener = new DisplayManager.DisplayListener() {
            @Override
            public void onDisplayAdded(int displayId) {
            }

            @Override
            public void onDisplayRemoved(int displayId) {
            }

            @Override
            public void onDisplayChanged(int displayId) {
                if (displayId == Display.DEFAULT_DISPLAY && !stopping) {
                    try {
                        sender.execute(RecorderLink.this::displayChanged);
                    } catch (RejectedExecutionException ignored) {
                        // The link stopped meanwhile.
                    }
                }
            }
        };
        if (displayManager != null) {
            displayManager.registerDisplayListener(displayListener, mainHandler);
            lastDisplay = displayKey();
        }

        // The listener taking an executor is in Android 13 (API 33) and
        // later; on earlier phones the volume is raised only when asked.
        accessibilityManager = context.getSystemService(AccessibilityManager.class);
        if (accessibilityManager != null && Build.VERSION.SDK_INT >= 33) {
            lastSeparate = AccessibilityVolume.servicesUsingIt(context).length() > 0;
            servicesListener = manager -> servicesChanged();
            accessibilityManager.addAccessibilityServicesStateChangeListener(sender, servicesListener);
        }

        acceptThread = new Thread(this::acceptLoop, "A11y Recorder link");
        acceptThread.setDaemon(true);
        acceptThread.start();
    }

    void stop() {
        stopping = true;
        if (displayManager != null && displayListener != null) {
            displayManager.unregisterDisplayListener(displayListener);
        }
        if (accessibilityManager != null && servicesListener != null) {
            accessibilityManager.removeAccessibilityServicesStateChangeListener(servicesListener);
        }
        sender.shutdownNow();
        screenshotSender.shutdownNow();
        for (Client client : clients) {
            client.close();
        }
        if (server != null) {
            // Closing a LocalServerSocket does not wake a thread waiting in
            // accept(), so a connection of the app's own is made first; it
            // is refused by the peer check like any other app's.
            try (LocalSocket wake = new LocalSocket()) {
                wake.connect(new LocalSocketAddress(SOCKET_NAME));
            } catch (IOException ignored) {
                // Nothing is waiting.
            }
            try {
                server.close();
            } catch (IOException ignored) {
                // Already closed.
            }
        }
    }

    private void acceptLoop() {
        while (!stopping) {
            LocalSocket socket;
            try {
                socket = server.accept();
            } catch (IOException exception) {
                if (!stopping) {
                    Log.w(TAG, "The recorder's socket stopped accepting", exception);
                }
                return;
            }

            if (stopping || !fromAdb(socket)) {
                closeQuietly(socket);
                continue;
            }

            Client client = new Client(socket);
            clients.add(client);
            Thread thread = new Thread(client::run, "A11y Recorder client");
            thread.setDaemon(true);
            thread.start();
        }
    }

    private static boolean fromAdb(LocalSocket socket) {
        try {
            Credentials peer = socket.getPeerCredentials();
            return peer.getUid() == Process.SHELL_UID || peer.getUid() == Process.ROOT_UID;
        } catch (IOException exception) {
            return false;
        }
    }

    /** What to run when a connection asks for tree snapshots; set before start(). */
    void setTreeRequests(Runnable requests) {
        treeRequests = requests;
    }

    /** True when a connection has asked for tree snapshots. */
    boolean wantsTrees() {
        for (Client client : clients) {
            if (client.treesWanted) {
                return true;
            }
        }
        return false;
    }

    /** Sends a tree snapshot to each connection that asked for them. */
    void sendTree(JSONObject message) {
        if (stopping) {
            return;
        }
        try {
            sender.execute(() -> {
                for (Client client : clients) {
                    client.sendTree(message);
                }
            });
        } catch (RejectedExecutionException ignored) {
            // The link stopped meanwhile.
        }
    }

    /** What to run when a connection asks for screenshots; set before start(). */
    void setScreenshotRequests(Runnable requests) {
        screenshotRequests = requests;
    }

    /** True when a connection has asked for screenshots. */
    boolean wantsScreenshots() {
        for (Client client : clients) {
            if (client.screenshotsWanted) {
                return true;
            }
        }
        return false;
    }

    /**
     * Sends a screenshot's line, a JSON object without its index, to each
     * connection that asked for them.
     */
    void sendScreenshot(String line) {
        if (stopping) {
            return;
        }
        try {
            screenshotSender.execute(() -> {
                for (Client client : clients) {
                    client.sendScreenshot(line);
                }
            });
        } catch (RejectedExecutionException ignored) {
            // The link stopped meanwhile.
        }
    }

    /** True when a connection has asked for accessibility events. */
    boolean wantsEvents() {
        for (Client client : clients) {
            if (client.eventsWanted) {
                return true;
            }
        }
        return false;
    }

    /** Sends an event's message to each connection that asked for events. */
    void sendEvent(JSONObject message) {
        if (stopping) {
            return;
        }
        try {
            sender.execute(() -> {
                for (Client client : clients) {
                    client.sendEvent(message);
                }
            });
        } catch (RejectedExecutionException ignored) {
            // The link stopped meanwhile.
        }
    }

    private void displayChanged() {
        String key = displayKey();
        if (key.equals(lastDisplay)) {
            return;
        }
        lastDisplay = key;
        for (Client client : clients) {
            client.sendDisplay("changed");
        }
    }

    // On the sender thread. Services are turned on and off for many
    // reasons, so a connection hears only when the Accessibility volume
    // becomes separate or stops being separate.
    private void servicesChanged() {
        if (stopping) {
            return;
        }
        boolean separate = AccessibilityVolume.servicesUsingIt(context).length() > 0;
        if (separate == lastSeparate) {
            return;
        }
        lastSeparate = separate;
        for (Client client : clients) {
            client.sendVolume("services-changed");
        }
    }

    // Rotation and size, to tell a real change from the other display
    // changes DisplayManager reports, such as brightness.
    private String displayKey() {
        Display display = defaultDisplay();
        if (display == null) {
            return "";
        }
        Display.Mode mode = display.getMode();
        return degrees(display.getRotation()) + ":" + mode.getPhysicalWidth() + "x" + mode.getPhysicalHeight();
    }

    private Display defaultDisplay() {
        return displayManager == null ? null : displayManager.getDisplay(Display.DEFAULT_DISPLAY);
    }

    private static int degrees(int rotation) {
        switch (rotation) {
            case Surface.ROTATION_90:
                return 90;
            case Surface.ROTATION_180:
                return 180;
            case Surface.ROTATION_270:
                return 270;
            default:
                return 0;
        }
    }

    private JSONObject hello() throws JSONException {
        JSONObject message = new JSONObject();
        message.put("type", "hello");
        message.put("protocol", PROTOCOL);
        String versionName = "";
        long versionCode = 0;
        try {
            PackageInfo info = context.getPackageManager().getPackageInfo(context.getPackageName(), 0);
            versionName = info.versionName == null ? "" : info.versionName;
            versionCode = info.getLongVersionCode();
        } catch (PackageManager.NameNotFoundException ignored) {
            // The app's own package is always found.
        }
        message.put("versionName", versionName);
        message.put("versionCode", versionCode);
        message.put("androidRelease", Build.VERSION.RELEASE);
        message.put("sdk", Build.VERSION.SDK_INT);
        message.put("time", System.nanoTime());
        return message;
    }

    // Null when the phone has no default display to describe.
    private JSONObject display(String reason) throws JSONException {
        Display display = defaultDisplay();
        if (display == null) {
            return null;
        }
        JSONObject message = new JSONObject();
        message.put("type", "display");
        message.put("reason", reason);
        Display.Mode mode = display.getMode();
        message.put("rotation", degrees(display.getRotation()));
        message.put("modeWidth", mode.getPhysicalWidth());
        message.put("modeHeight", mode.getPhysicalHeight());
        message.put("time", System.nanoTime());
        return message;
    }

    private static void closeQuietly(LocalSocket socket) {
        try {
            socket.close();
        } catch (IOException ignored) {
            // Already closed.
        }
    }

    // A setting's notice address, or null for a namespace other than
    // Settings.Secure's, Settings.System's or Settings.Global's.
    private static Uri settingUri(String namespace, String name) {
        if (name == null || name.isEmpty()) {
            return null;
        }
        switch (namespace) {
            case "secure":
                return Settings.Secure.getUriFor(name);
            case "system":
                return Settings.System.getUriFor(name);
            case "global":
                return Settings.Global.getUriFor(name);
            default:
                return null;
        }
    }

    /** One connection from the recorder. */
    private final class Client {
        private final LocalSocket socket;
        private final Object writeLock = new Object();
        // The share of the Accessibility volume this connection asked for,
        // or 0 if it has not asked.
        private volatile int volumeMinimum;
        // True once this connection has asked for accessibility events.
        volatile boolean eventsWanted;
        // The events sent on this connection; used on the sender thread only.
        private long eventIndex;
        // True once this connection has asked for tree snapshots.
        volatile boolean treesWanted;
        // The snapshots sent on this connection; used on the sender thread only.
        private long treeIndex;
        // True once this connection has asked for screenshots.
        volatile boolean screenshotsWanted;
        // The screenshots sent on this connection; used on the screenshot
        // sender's thread only.
        private long screenshotIndex;
        // The settings this connection asked to be told of; used under
        // itself only.
        private final List<ContentObserver> settingObservers = new ArrayList<>();
        private boolean closed;
        // Written and used under writeLock only.
        private OutputStream output;

        Client(LocalSocket socket) {
            this.socket = socket;
        }

        void run() {
            try {
                // Under the write lock, so that a display change cannot be
                // sent before the greeting.
                synchronized (writeLock) {
                    output = socket.getOutputStream();
                    write(hello());
                    JSONObject display = display("initial");
                    if (display != null) {
                        write(display);
                    }
                }
                BufferedReader reader = new BufferedReader(
                        new InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8));
                String line;
                while ((line = reader.readLine()) != null) {
                    // Read before anything else, so the time is as close as
                    // possible to the request's arrival.
                    long received = System.nanoTime();
                    answer(line, received);
                }
            } catch (IOException | JSONException exception) {
                // The recorder closed the connection, or the phone was unplugged.
            } finally {
                clients.remove(this);
                close();
            }
        }

        private void answer(String line, long received) throws IOException, JSONException {
            JSONObject request;
            try {
                request = new JSONObject(line);
            } catch (JSONException exception) {
                sendError("not JSON");
                return;
            }

            String type = request.optString("type");
            if ("clock".equals(type)) {
                JSONObject reply = new JSONObject();
                reply.put("type", "clock");
                reply.put("id", request.optLong("id"));
                reply.put("received", received);
                synchronized (writeLock) {
                    reply.put("sent", System.nanoTime());
                    write(reply);
                }
            } else if ("accessibility-volume".equals(type)) {
                int minimum = request.optInt("minimumPercent", 0);
                if (minimum < 1 || minimum > 100) {
                    sendError("minimumPercent must be from 1 to 100");
                    return;
                }
                volumeMinimum = minimum;
                send(AccessibilityVolume.raise(context, minimum, "requested"));
            } else if ("accessibility-events".equals(type)) {
                eventsWanted = true;
            } else if ("accessibility-trees".equals(type)) {
                treesWanted = true;
                Runnable requests = treeRequests;
                if (requests != null) {
                    requests.run();
                }
            } else if ("screenshots".equals(type)) {
                screenshotsWanted = true;
                Runnable requests = screenshotRequests;
                if (requests != null) {
                    requests.run();
                }
            } else if ("settings".equals(type)) {
                watchSettings(request.optJSONArray("settings"));
            } else {
                sendError("unknown type: " + type);
            }
        }

        // Replaces the settings watched for this connection with those of
        // the request. A notice is sent from the sender thread, with the
        // time it reached the companion; Android may combine several writes
        // in quick succession into one notice.
        private void watchSettings(JSONArray settings) throws IOException, JSONException {
            if (settings == null) {
                sendError("settings must be an array");
                return;
            }
            List<Uri> uris = new ArrayList<>();
            List<String> namespaces = new ArrayList<>();
            List<String> names = new ArrayList<>();
            for (int i = 0; i < settings.length(); i++) {
                JSONObject setting = settings.optJSONObject(i);
                String namespace = setting == null ? "" : setting.optString("namespace");
                String name = setting == null ? "" : setting.optString("name");
                Uri uri = settingUri(namespace, name);
                if (uri == null) {
                    sendError("unknown setting: " + namespace + "/" + name);
                    return;
                }
                uris.add(uri);
                namespaces.add(namespace);
                names.add(name);
            }

            ContentResolver resolver = context.getContentResolver();
            synchronized (settingObservers) {
                if (closed) {
                    return;
                }
                unwatchSettings(resolver);
                for (int i = 0; i < uris.size(); i++) {
                    String namespace = namespaces.get(i);
                    String name = names.get(i);
                    ContentObserver observer = new ContentObserver(mainHandler) {
                        @Override
                        public void onChange(boolean selfChange) {
                            settingChanged(namespace, name, System.nanoTime());
                        }
                    };
                    resolver.registerContentObserver(uris.get(i), false, observer);
                    settingObservers.add(observer);
                }
            }
        }

        // Under settingObservers.
        private void unwatchSettings(ContentResolver resolver) {
            for (ContentObserver observer : settingObservers) {
                resolver.unregisterContentObserver(observer);
            }
            settingObservers.clear();
        }

        // On the main thread; the notice is sent from the sender thread.
        private void settingChanged(String namespace, String name, long time) {
            if (stopping) {
                return;
            }
            try {
                sender.execute(() -> {
                    try {
                        JSONObject message = new JSONObject();
                        message.put("type", "setting-changed");
                        message.put("namespace", namespace);
                        message.put("name", name);
                        message.put("time", time);
                        send(message);
                    } catch (IOException | JSONException exception) {
                        close();
                    }
                });
            } catch (RejectedExecutionException ignored) {
                // The link stopped meanwhile.
            }
        }

        void sendVolume(String reason) {
            int minimum = volumeMinimum;
            if (minimum == 0) {
                return;
            }
            try {
                send(AccessibilityVolume.raise(context, minimum, reason));
            } catch (IOException | JSONException exception) {
                close();
            }
        }

        // On the sender thread, which alone adds the index, so the message
        // can be shared by the connections.
        void sendEvent(JSONObject message) {
            if (!eventsWanted) {
                return;
            }
            try {
                eventIndex++;
                message.put("index", eventIndex);
                send(message);
            } catch (IOException | JSONException exception) {
                close();
            }
        }

        // On the sender thread, like sendEvent.
        void sendTree(JSONObject message) {
            if (!treesWanted) {
                return;
            }
            try {
                treeIndex++;
                message.put("index", treeIndex);
                send(message);
            } catch (IOException | JSONException exception) {
                close();
            }
        }

        // On the screenshot sender's thread, which alone adds the index.
        // The line is a JSON object; the index goes first.
        void sendScreenshot(String line) {
            if (!screenshotsWanted) {
                return;
            }
            try {
                screenshotIndex++;
                String indexed = "{\"index\":" + screenshotIndex + "," + line.substring(1);
                synchronized (writeLock) {
                    if (output == null) {
                        return;
                    }
                    output.write((indexed + "\n").getBytes(StandardCharsets.UTF_8));
                    output.flush();
                }
            } catch (IOException exception) {
                close();
            }
        }

        void sendDisplay(String reason) {
            try {
                JSONObject display = display(reason);
                if (display != null) {
                    send(display);
                }
            } catch (IOException | JSONException exception) {
                close();
            }
        }

        private void sendError(String text) throws IOException, JSONException {
            JSONObject message = new JSONObject();
            message.put("type", "error");
            message.put("message", text);
            send(message);
        }

        private void send(JSONObject message) throws IOException {
            synchronized (writeLock) {
                write(message);
            }
        }

        private void write(JSONObject message) throws IOException {
            if (output == null) {
                return;
            }
            output.write((message.toString() + "\n").getBytes(StandardCharsets.UTF_8));
            output.flush();
        }

        void close() {
            synchronized (settingObservers) {
                if (!closed) {
                    closed = true;
                    unwatchSettings(context.getContentResolver());
                }
            }
            closeQuietly(socket);
        }
    }
}
