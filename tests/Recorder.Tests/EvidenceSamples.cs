using System.Text.Json;

namespace Recorder.Tests;

/// <summary>
/// Payloads of the shapes the recorder's collectors write, one or more for
/// every channel and event type with evidence tables. Each is valid for the
/// payload validator, and each is written the way System.Text.Json
/// writes it, so a payload rebuilt from its tables reads back unchanged.
/// </summary>
internal static class EvidenceSamples
{
    private const string Context =
        @"{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null," +
        @"""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1""," +
        @"""executionWorldId"":null,""documentToken"":""TOKEN-1""}";

    private const string Element =
        @"{""processId"":42,""nativeWindowHandle"":0,""automationId"":""SaveButton"",""name"":""Save"",""className"":""Button""," +
        @"""frameworkId"":""Win32"",""controlType"":""Button"",""localizedControlType"":""button"",""hasKeyboardFocus"":true," +
        @"""isKeyboardFocusable"":true,""isEnabled"":true,""isOffscreen"":false," +
        @"""boundingRectangle"":{""x"":10.5,""y"":20,""width"":75.25,""height"":23},""propertySource"":""event-cache""," +
        @"""qualityFlags"":[""cached-properties"",""name-from-cache""]}";

    private const string BareElement =
        @"{""processId"":null,""nativeWindowHandle"":null,""automationId"":null,""name"":null,""className"":null," +
        @"""frameworkId"":null,""controlType"":null,""localizedControlType"":null,""hasKeyboardFocus"":null," +
        @"""isKeyboardFocusable"":null,""isEnabled"":null,""isOffscreen"":null,""boundingRectangle"":null," +
        @"""qualityFlags"":[]}";

    private const string Monitor =
        @"{""deviceName"":""\\\\.\\DISPLAY1"",""bounds"":{""x"":0,""y"":0,""width"":1920,""height"":1080}," +
        @"""workArea"":{""x"":0,""y"":0,""width"":1920,""height"":1040},""isPrimary"":true}";

    private const string Format =
        @"{""encoding"":""pcm"",""sampleRate"":48000,""channels"":2,""bitsPerSample"":16,""blockAlign"":4," +
        @"""averageBytesPerSecond"":192000}";

    private static string J(string json) => json.Replace('\'', '"');

    // A color-maps-sent record: all three maps in a first one, the forced
    // colors map alone in a later one.
    // From protocol 0.59 a later one names the colors that changed.
    internal static string ColorMapsSample(bool first, IReadOnlyList<string>? changedColors = null)
    {
        static string Map(string color) =>
            "{" + string.Join(",", Recorder.Contracts.BrowserPreferenceSettings.RendererColorNames
                .Select(name => $"\"{name}\":\"{color}\"")) + "}";
        var maps = first
            ? $"{{\"light\":{Map("#FFFFFFFF")},\"dark\":{Map("#FF202124")},\"forcedColors\":{Map("#FF000000")}}}"
            : $"{{\"forcedColors\":{Map("#FFFFFF00")}}}";
        return "{\"context\":{\"browserInstanceId\":\"browser-1\",\"processId\":4000,\"processType\":\"browser\",\"profileId\":null,\"browserContextId\":null,\"pageId\":null,\"frameId\":null,\"documentId\":null,\"executionWorldId\":null,\"documentToken\":null}," +
            $"\"pageFrameTreeNodeId\":12,\"primaryPage\":true,\"rendererProcessId\":7,\"viewId\":\"2199023255552\",\"point\":\"{(first ? "view-created" : "color-providers")}\",\"first\":{(first ? "true" : "false")},\"maps\":{maps}" +
            (changedColors is null
                ? string.Empty
                : $",\"changedColors\":{{\"forcedColors\":[{string.Join(",", changedColors.Select(name => $"\"{name}\""))}]}}") +
            "}";
    }

    private static readonly string WorldContext =
        J("{'browserInstanceId':'browser-1','processId':4100,'processType':'renderer','profileId':null,'browserContextId':'context-1','pageId':'page-1','frameId':'frame-1','documentId':'document-1','executionWorldId':'world-1','documentToken':'TOKEN-1'}");

    private static readonly string WorkerContext =
        J("{'browserInstanceId':'browser-1','processId':4100,'processType':'renderer','profileId':null,'browserContextId':'context-1','pageId':null,'frameId':null,'documentId':null,'executionWorldId':null,'documentToken':null}");

    private static readonly string NavigationContext =
        J("{'browserInstanceId':'browser-1','processId':4000,'processType':'browser','profileId':null,'browserContextId':'context-1','pageId':'frame-12','frameId':'frame-12','documentId':null,'executionWorldId':null,'documentToken':null}");

    private static readonly string CommittedContext =
        J("{'browserInstanceId':'browser-1','processId':4000,'processType':'browser','profileId':null,'browserContextId':'context-1','pageId':'frame-12','frameId':'frame-12','documentId':'document-40','executionWorldId':null,'documentToken':'TOKEN-40'}");

    private static readonly string SubframeContext =
        J("{'browserInstanceId':'browser-1','processId':4000,'processType':'browser','profileId':null,'browserContextId':'context-1','pageId':'frame-12','frameId':'frame-13','documentId':null,'executionWorldId':null,'documentToken':null}");

    private static readonly string ButtonTarget =
        J("{'kind':'node','interfaceName':'HTMLButtonElement','targetId':null,'documentId':'document-1','nodeId':42,'backendNodeId':'blink-node-42','tagName':'BUTTON','elementId':'save','classes':['primary','large']}");

    private static readonly string WindowTarget =
        J("{'kind':'window','interfaceName':'Window','targetId':'window-1','documentId':'document-1','nodeId':null,'backendNodeId':null,'tagName':null,'elementId':null,'classes':[]}");

    private static readonly string WorkerTarget =
        J("{'kind':'other','interfaceName':'DedicatedWorkerGlobalScope','targetId':'worker-global-1','documentId':null,'nodeId':null,'backendNodeId':null,'tagName':null,'elementId':null,'classes':[]}");

    private static readonly string Location =
        J("{'scriptId':'script-2','url':'https://example.test/app.js','line':18,'column':4,'functionName':'activate','sourceHash':'sha256:test'}");

    private static readonly string BareLocation =
        J("{'scriptId':null,'url':null,'line':null,'column':null,'functionName':null,'sourceHash':null}");

    private static readonly string OneStepPath =
        J("'composedPath':[") + ButtonTarget + "," + WindowTarget + "]," +
        J("'pathScopes':[{'treeScopeRootNodeId':19,'shadowRootMode':null,'targetNodeId':42,'relatedTargetNodeId':null,'visiblePathIndexes':[0,1],'unmatchedVisibleTargetCount':0},{'treeScopeRootNodeId':null,'shadowRootMode':null,'targetNodeId':42,'relatedTargetNodeId':null,'visiblePathIndexes':[],'unmatchedVisibleTargetCount':1}]");

    private static readonly string Scheduler =
        J("'queueName':'frame-throttleable','queueType':12,'throttlingType':'background','desiredWakeUpTicks':'123456000','allowedWakeUpTicks':'124000000',");

    private static readonly string AxContext =
        J("{'browserInstanceId':'browser-1','processId':4100,'processType':'renderer','profileId':null,'browserContextId':'context-1','pageId':'page-1','frameId':'frame-1','documentId':null,'executionWorldId':null,'documentToken':'TOKEN-1'}");

    private static readonly string World =
        J("{'kind':'isolated','blinkWorldId':1,'name':'extension','stableId':null}");

    public static IReadOnlyList<(string Channel, string EventType, string Payload)> All { get; } =
    [
        ("collector.lifecycle", "collector-lifecycle",
            @"{""action"":""start"",""state"":""running"",""utc"":""2026-09-25T12:00:00.1234567+00:00""}"),
        ("collector.lifecycle", "collector-lifecycle",
            @"{""action"":""stop"",""state"":""stopped"",""utc"":""2026-09-25T12:00:01+00:00""}"),
        ("collector.lifecycle", "collector-lifecycle",
            @"{""action"":""stop"",""state"":""stopped"",""utc"":""2026-09-25T12:00:01.5+00:00""}"),
        ("session.annotations", "session-marker", @"{""note"":""Before the dialog""}"),
        ("session.annotations", "session-marker", @"{""note"":null}"),
        ("input.keyboard", "raw-keyboard",
            @"{""deviceHandle"":65539,""makeCode"":30,""flags"":0,""virtualKey"":65,""message"":256,""extraInformation"":0}"),
        ("input.mouse", "raw-mouse",
            @"{""deviceHandle"":-4294967291,""movementMode"":""relative"",""deltaX"":-3,""deltaY"":2,""buttonFlags"":1," +
            @"""buttonData"":0,""rawButtons"":0,""extraInformation"":0,""cursorX"":640,""cursorY"":-20,""foregroundProcessId"":42}"),
        ("window.foreground", "foreground-window",
            @"{""reason"":""event"",""eventThreadId"":7,""nativeEventTimeMilliseconds"":123456,""windowHandle"":1311000," +
            @"""processId"":42,""threadId"":7,""processName"":""notepad"",""processPath"":""C:\\Windows\\notepad.exe""," +
            @"""title"":""Untitled - Notepad"",""className"":""Notepad"",""isVisible"":true,""isMinimized"":false," +
            @"""isMaximized"":false,""isCloaked"":false,""dpi"":96," +
            @"""bounds"":{""x"":-8,""y"":10,""width"":800,""height"":600},""monitor"":" + Monitor + "}"),
        ("window.foreground", "foreground-window",
            @"{""reason"":""initial"",""eventThreadId"":null,""nativeEventTimeMilliseconds"":null,""windowHandle"":1311000," +
            @"""processId"":42,""threadId"":7,""processName"":""notepad"",""processPath"":""C:\\Windows\\notepad.exe""," +
            @"""title"":""Untitled - Notepad"",""className"":""Notepad"",""isVisible"":true,""isMinimized"":false," +
            @"""isMaximized"":false,""isCloaked"":null,""dpi"":null,""bounds"":null,""monitor"":" + Monitor + "}"),
        ("window.foreground", "foreground-window",
            @"{""reason"":""event"",""eventThreadId"":8,""nativeEventTimeMilliseconds"":123500,""windowHandle"":0," +
            @"""processId"":0,""threadId"":0,""processName"":null,""processPath"":null,""title"":null,""className"":null," +
            @"""isVisible"":false,""isMinimized"":false,""isMaximized"":false,""isCloaked"":null,""dpi"":null," +
            @"""bounds"":null,""monitor"":null}"),
        // Added with 0021_windows_preferences.sql; the first is as read on the
        // target machine on 2026-10-08.
        ("system.preferences", "windows-preferences",
            """
            {"reason":"start","uiSettingsEvents":{"advancedEffectsEnabledChanged":true,"animationsEnabledChanged":true,"autoHideScrollBarsChanged":true,"textScaleFactorChanged":true,"colorValuesChanged":true},"settings":{"monitors":{"value":[{"deviceName":"\\\\.\\DISPLAY1","bounds":{"x":0,"y":0,"width":1920,"height":1080},"isPrimary":true,"dpiX":96,"dpiY":96}],"problem":null},"textScaleFactor":{"value":1,"problem":null},"appsUseLightTheme":{"value":null,"problem":"The key HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize does not exist."},"transparencyEffects":{"value":false,"problem":null},"colorFilterActive":{"value":null,"problem":"The key HKCU\\Software\\Microsoft\\ColorFiltering does not exist."},"colorFilterType":{"value":null,"problem":"The key HKCU\\Software\\Microsoft\\ColorFiltering does not exist."},"highContrast":{"value":false,"problem":null},"highContrastScheme":{"value":"","problem":null},"accentColor":{"value":"#0078D7","problem":null},"animationsEnabled":{"value":true,"problem":null},"uiEffects":{"value":true,"problem":null},"menuAnimation":{"value":true,"problem":null},"menuFade":{"value":true,"problem":null},"comboBoxAnimation":{"value":true,"problem":null},"cursorWidth":{"value":32,"problem":null},"cursorHeight":{"value":32,"problem":null},"caretWidth":{"value":1,"problem":null},"caretBlinkTime":{"value":530,"problem":null},"focusBorderWidth":{"value":1,"problem":null},"focusBorderHeight":{"value":1,"problem":null},"keyboardCues":{"value":false,"problem":null},"autoHideScrollBars":{"value":true,"problem":null},"messageDuration":{"value":5,"problem":null},"stickyKeys":{"value":false,"problem":null},"filterKeys":{"value":false,"problem":null},"toggleKeys":{"value":false,"problem":null},"mouseKeys":{"value":false,"problem":null}}}
            """),
        ("system.preferences", "windows-preferences",
            """
            {"reason":"stop","uiSettingsEvents":{"advancedEffectsEnabledChanged":true,"animationsEnabledChanged":false,"autoHideScrollBarsChanged":true,"textScaleFactorChanged":true,"colorValuesChanged":true},"settings":{"monitors":{"value":[{"deviceName":"\\\\.\\DISPLAY1","bounds":{"x":0,"y":0,"width":1920,"height":1080},"isPrimary":true,"dpiX":96,"dpiY":96},{"deviceName":"\\\\.\\DISPLAY2","bounds":{"x":1920,"y":-120,"width":2560,"height":1440},"isPrimary":false,"dpiX":144,"dpiY":null}],"problem":null},"textScaleFactor":{"value":1.25,"problem":null},"appsUseLightTheme":{"value":null,"problem":"The key HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize does not exist."},"transparencyEffects":{"value":false,"problem":null},"colorFilterActive":{"value":null,"problem":"The key HKCU\\Software\\Microsoft\\ColorFiltering does not exist."},"colorFilterType":{"value":null,"problem":"The key HKCU\\Software\\Microsoft\\ColorFiltering does not exist."},"highContrast":{"value":false,"problem":null},"highContrastScheme":{"value":null,"problem":null},"accentColor":{"value":"#0078D7","problem":null},"animationsEnabled":{"value":true,"problem":null},"uiEffects":{"value":true,"problem":null},"menuAnimation":{"value":true,"problem":null},"menuFade":{"value":true,"problem":null},"comboBoxAnimation":{"value":true,"problem":null},"cursorWidth":{"value":32,"problem":null},"cursorHeight":{"value":32,"problem":null},"caretWidth":{"value":1,"problem":null},"caretBlinkTime":{"value":530,"problem":null},"focusBorderWidth":{"value":1,"problem":null},"focusBorderHeight":{"value":1,"problem":null},"keyboardCues":{"value":false,"problem":null},"autoHideScrollBars":{"value":true,"problem":null},"messageDuration":{"value":5,"problem":null},"stickyKeys":{"value":false,"problem":null},"filterKeys":{"value":false,"problem":null},"toggleKeys":{"value":false,"problem":null},"mouseKeys":{"value":false,"problem":null}}}
            """),
        ("system.preferences", "windows-preference-changed",
            """
            {"setting":"textScaleFactor","previous":{"textScaleFactor":{"value":1,"problem":null}},"current":{"textScaleFactor":{"value":1.25,"problem":null}},"notice":{"kind":"ui-settings","uiAction":null,"area":null,"source":"textScaleFactorChanged"}}
            """),
        ("system.preferences", "windows-preference-changed",
            """
            {"setting":"highContrast","previous":{"highContrast":{"value":false,"problem":null}},"current":{"highContrast":{"value":true,"problem":null}},"notice":{"kind":"setting-change","uiAction":67,"area":null,"source":null}}
            """),
        ("system.preferences", "windows-preference-changed",
            """
            {"setting":"monitors","previous":{"monitors":{"value":[{"deviceName":"\\\\.\\DISPLAY1","bounds":{"x":0,"y":0,"width":1920,"height":1080},"isPrimary":true,"dpiX":96,"dpiY":96}],"problem":null}},"current":{"monitors":{"value":null,"problem":"GetDpiForMonitor failed with 0x80070057."}},"notice":{"kind":"display-change","uiAction":null,"area":null,"source":null}}
            """),
        ("system.preferences", "windows-preference-changed",
            """
            {"setting":"colorFilterActive","previous":{"colorFilterActive":{"value":null,"problem":"The key HKCU\\Software\\Microsoft\\ColorFiltering does not exist."}},"current":{"colorFilterActive":{"value":true,"problem":null}},"notice":{"kind":"registry","uiAction":null,"area":"ImmersiveColorSet","source":"HKCU\\Software\\Microsoft\\ColorFiltering"}}
            """),
        // Added with 0023_browser_preferences.sql (protocol 0.56): a profile's
        // preferences, a change and a change with no reading before it, a
        // view's first and a later send, and a host and default zoom change.
        ("browser.preferences", "browser-preferences",
            """
            {"context":{"browserInstanceId":"browser-1","processId":4000,"processType":"browser","profileId":null,"browserContextId":null,"pageId":null,"frameId":null,"documentId":null,"executionWorldId":null,"documentToken":null},"profileDirectory":"C:\\Users\\User\\AppData\\Local\\A11yRecorder\\Profile 1","newProfile":false,"preferences":{"standardFontFamily":{"value":"Times New Roman","isDefault":true,"problem":null},"fixedFontFamily":{"value":"Consolas","isDefault":true,"problem":null},"serifFontFamily":{"value":"Times New Roman","isDefault":true,"problem":null},"sansSerifFontFamily":{"value":"Arial","isDefault":true,"problem":null},"cursiveFontFamily":{"value":"Comic Sans MS","isDefault":true,"problem":null},"fantasyFontFamily":{"value":"Impact","isDefault":true,"problem":null},"mathFontFamily":{"value":"Cambria Math","isDefault":true,"problem":null},"defaultFontSize":{"value":20,"isDefault":false,"problem":null},"defaultFixedFontSize":{"value":13,"isDefault":true,"problem":null},"minimumFontSize":{"value":0,"isDefault":true,"problem":null},"minimumLogicalFontSize":{"value":6,"isDefault":true,"problem":null},"colorScheme":{"value":0,"isDefault":true,"problem":null},"focusHighlight":{"value":false,"isDefault":true,"problem":null},"requestedPageColors":{"value":0,"isDefault":true,"problem":null},"pageColorsOnlyOnIncreasedContrast":{"value":true,"isDefault":true,"problem":null},"pageColorsBlockList":{"value":["example.org"],"isDefault":false,"problem":null},"caretBrowsing":{"value":null,"isDefault":null,"problem":"not registered"}}}
            """),
        ("browser.preferences", "browser-preference-changed",
            """
            {"context":{"browserInstanceId":"browser-1","processId":4000,"processType":"browser","profileId":null,"browserContextId":null,"pageId":null,"frameId":null,"documentId":null,"executionWorldId":null,"documentToken":null},"profileDirectory":"C:\\Users\\User\\AppData\\Local\\A11yRecorder\\Profile 1","preference":"focusHighlight","previous":{"focusHighlight":{"value":false,"isDefault":true,"problem":null}},"current":{"focusHighlight":{"value":true,"isDefault":false,"problem":null}}}
            """),
        ("browser.preferences", "browser-preference-changed",
            """
            {"context":{"browserInstanceId":"browser-1","processId":4000,"processType":"browser","profileId":null,"browserContextId":null,"pageId":null,"frameId":null,"documentId":null,"executionWorldId":null,"documentToken":null},"profileDirectory":"C:\\Users\\User\\AppData\\Local\\A11yRecorder\\Profile 1","preference":"pageColorsBlockList","previous":{},"current":{"pageColorsBlockList":{"value":["example.org","example.com"],"isDefault":false,"problem":null}}}
            """),
        ("browser.preferences", "web-preferences-sent",
            """
            {"context":{"browserInstanceId":"browser-1","processId":4000,"processType":"browser","profileId":null,"browserContextId":null,"pageId":null,"frameId":null,"documentId":null,"executionWorldId":null,"documentToken":null},"pageFrameTreeNodeId":12,"primaryPage":true,"rendererProcessId":7,"viewId":"2199023255552","point":"view-created","first":true,"fields":{"standardFontFamily":"Times New Roman","fixedFontFamily":"Consolas","serifFontFamily":"Times New Roman","sansSerifFontFamily":"Arial","cursiveFontFamily":"Comic Sans MS","fantasyFontFamily":"Impact","mathFontFamily":"Cambria Math","defaultFontSize":20,"defaultFixedFontSize":13,"minimumFontSize":0,"minimumLogicalFontSize":6,"prefersReducedMotion":false,"prefersReducedTransparency":false,"invertedColors":false,"textTrackTextSize":"","textTrackFontFamily":"","inForcedColors":false,"isForcedColorsDisabled":false,"preferredRootScrollbarColorScheme":"light","preferredColorScheme":"light","preferredContrast":"no-preference","focusRingColor":"#FFE59700","hasCaretBlinkInterval":true,"caretBlinkIntervalMilliseconds":530,"caretBrowsingEnabled":false,"useOverlayScrollbar":false,"captionFontFamily":"Segoe UI","captionFontHeight":-12,"smallCaptionFontFamily":"Segoe UI","smallCaptionFontHeight":-12,"menuFontFamily":"Segoe UI","menuFontHeight":-12,"statusFontFamily":"Segoe UI","statusFontHeight":-12,"messageFontFamily":"Segoe UI","messageFontHeight":-12}}
            """),
        ("browser.preferences", "web-preferences-sent",
            """
            {"context":{"browserInstanceId":"browser-1","processId":4000,"processType":"browser","profileId":null,"browserContextId":null,"pageId":null,"frameId":null,"documentId":null,"executionWorldId":null,"documentToken":null},"pageFrameTreeNodeId":12,"primaryPage":true,"rendererProcessId":7,"viewId":"2199023255552","point":"web-preferences","first":false,"fields":{"preferredColorScheme":"dark","inForcedColors":true}}
            """),
        ("browser.preferences", "zoom-level-changed",
            """
            {"context":{"browserInstanceId":"browser-1","processId":4000,"processType":"browser","profileId":null,"browserContextId":null,"pageId":null,"frameId":null,"documentId":null,"executionWorldId":null,"documentToken":null},"mode":"host","followsDefault":false,"host":"example.org","scheme":"","zoomLevel":1,"zoomPercent":120}
            """),
        ("browser.preferences", "zoom-level-changed",
            """
            {"context":{"browserInstanceId":"browser-1","processId":4000,"processType":"browser","profileId":null,"browserContextId":null,"pageId":null,"frameId":null,"documentId":null,"executionWorldId":null,"documentToken":null},"mode":"default","followsDefault":false,"host":"","scheme":"","zoomLevel":2.223901085741545,"zoomPercent":150}
            """),
        // Added with 0024_browser_color_maps.sql (protocol 0.57): a view's
        // first color maps and a later send of its forced colors map.
        ("browser.preferences", "color-maps-sent", ColorMapsSample(true)),
        ("browser.preferences", "color-maps-sent", ColorMapsSample(false)),
        // Added with 0025_browser_theme.sql (protocol 0.59): a profile's
        // preferences with the browser theme, a change of its color, and a
        // later color maps send naming the colors that changed.
        ("browser.preferences", "browser-preferences",
            """
            {"context":{"browserInstanceId":"browser-1","processId":4000,"processType":"browser","profileId":null,"browserContextId":null,"pageId":null,"frameId":null,"documentId":null,"executionWorldId":null,"documentToken":null},"profileDirectory":"C:\\Users\\User\\AppData\\Local\\A11yRecorder\\Profile 1","newProfile":false,"preferences":{"standardFontFamily":{"value":"Times New Roman","isDefault":true,"problem":null},"fixedFontFamily":{"value":"Consolas","isDefault":true,"problem":null},"serifFontFamily":{"value":"Times New Roman","isDefault":true,"problem":null},"sansSerifFontFamily":{"value":"Arial","isDefault":true,"problem":null},"cursiveFontFamily":{"value":"Comic Sans MS","isDefault":true,"problem":null},"fantasyFontFamily":{"value":"Impact","isDefault":true,"problem":null},"mathFontFamily":{"value":"Cambria Math","isDefault":true,"problem":null},"defaultFontSize":{"value":20,"isDefault":false,"problem":null},"defaultFixedFontSize":{"value":13,"isDefault":true,"problem":null},"minimumFontSize":{"value":0,"isDefault":true,"problem":null},"minimumLogicalFontSize":{"value":6,"isDefault":true,"problem":null},"colorScheme":{"value":0,"isDefault":true,"problem":null},"focusHighlight":{"value":false,"isDefault":true,"problem":null},"requestedPageColors":{"value":0,"isDefault":true,"problem":null},"pageColorsOnlyOnIncreasedContrast":{"value":true,"isDefault":true,"problem":null},"pageColorsBlockList":{"value":["example.org"],"isDefault":false,"problem":null},"caretBrowsing":{"value":null,"isDefault":null,"problem":"not registered"},"userColor":{"value":-1543926,"isDefault":false,"problem":null},"colorVariant":{"value":1,"isDefault":false,"problem":null},"grayscaleTheme":{"value":false,"isDefault":true,"problem":null},"themeId":{"value":"","isDefault":true,"problem":null}}}
            """),
        ("browser.preferences", "browser-preference-changed",
            """
            {"context":{"browserInstanceId":"browser-1","processId":4000,"processType":"browser","profileId":null,"browserContextId":null,"pageId":null,"frameId":null,"documentId":null,"executionWorldId":null,"documentToken":null},"profileDirectory":"C:\\Users\\User\\AppData\\Local\\A11yRecorder\\Profile 1","preference":"userColor","previous":{"userColor":{"value":0,"isDefault":true,"problem":null}},"current":{"userColor":{"value":-1543926,"isDefault":false,"problem":null}}}
            """),
        ("browser.preferences", "color-maps-sent", ColorMapsSample(false, ["kColorMenuBackground", "kColorMenuSeparator"])),
        ("browser.preferences", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        // Added with 0029_assistive_technology_settings.sql: NVDA's portable
        // copy with Caps Lock as an NVDA key, as on the target machine; one
        // with a profile and a custom key command; and a copy whose folder
        // is not known.
        ("system.assistive-technology", "assistive-technology-settings",
            J("{'product':'NVDA','processId':6688,'copy':'portable','configFolder':'C:\\\\Users\\\\User\\\\Desktop\\\\NVDA\\\\userConfig','read':true,'problem':null,'keyboardLayout':null,'nvdaModifierKeys':'7','multiPressTimeout':null,'autoPassThroughOnFocusChange':null,'autoPassThroughOnCaretMove':null,'trapNonCommandGestures':null,'enableOnPageLoad':null,'profiles':[],'profileTriggers':false,'customGestures':[],'gesturesProblem':null}")),
        ("system.assistive-technology", "assistive-technology-settings",
            J("{'product':'NVDA','processId':6690,'copy':'installed','configFolder':'C:\\\\Users\\\\User\\\\AppData\\\\Roaming\\\\nvda','read':true,'problem':null,'keyboardLayout':'laptop','nvdaModifierKeys':'1','multiPressTimeout':'700','autoPassThroughOnFocusChange':'False','autoPassThroughOnCaretMove':'True','trapNonCommandGestures':'False','enableOnPageLoad':'True','profiles':['Reading'],'profileTriggers':true,'customGestures':[{'section':'globalCommands.GlobalCommands','script':'reportCurrentFocus','gesture':'kb:NVDA+shift+tab'}],'gesturesProblem':null}")),
        ("system.assistive-technology", "assistive-technology-settings",
            J("{'product':'NVDA','processId':6692,'copy':'unknown','configFolder':null,'read':false,'problem':'The configuration folder is not known for this copy.','keyboardLayout':null,'nvdaModifierKeys':null,'multiPressTimeout':null,'autoPassThroughOnFocusChange':null,'autoPassThroughOnCaretMove':null,'trapNonCommandGestures':null,'enableOnPageLoad':null,'profiles':[],'profileTriggers':false,'customGestures':[],'gesturesProblem':null}")),
        // Added with 0028_input_recordability.sql: the recorder at medium; an
        // elevated window, and one whose level could not be read; the user's
        // desktop, and the secure desktop that could not be opened.
        ("window.foreground", "recorder-integrity",
            J("{'processId':4321,'integrityLevel':'medium','integrityRid':8192,'uiAccess':false,'problem':null}")),
        ("window.foreground", "foreground-integrity",
            J("{'windowHandle':7800334,'processId':22444,'processName':'powershell','integrityLevel':'high','integrityRid':12288,'uiAccess':false,'problem':null,'inputRecordable':false}")),
        ("window.foreground", "foreground-integrity",
            J("{'windowHandle':65748,'processId':8,'processName':null,'integrityLevel':null,'integrityRid':null,'uiAccess':null,'problem':'OpenProcessToken failed with error 5.','inputRecordable':null}")),
        ("window.foreground", "input-desktop",
            J("{'reason':'start','desktopName':'Default','problem':null,'inputRecordable':true}")),
        ("window.foreground", "input-desktop",
            J("{'reason':'switch','desktopName':null,'problem':'OpenInputDesktop failed with error 5.','inputRecordable':false}")),
        // Added with 0027_keyboard_hook.sql: a key NVDA kept, the injected Tab
        // NVDA put in place of a physical one, an extended key up; the first
        // installation, a refresh, and one after a loss.
        ("input.keyboard-hook", "hook-keyboard",
            J("{'installation':1,'virtualKey':72,'scanCode':35,'flags':0,'up':false,'extended':false,'injected':false,'lowerIntegrityInjected':false,'altDown':false,'extraInformation':0,'eventTimeMilliseconds':123456789}")),
        ("input.keyboard-hook", "hook-keyboard",
            J("{'installation':2,'virtualKey':9,'scanCode':15,'flags':16,'up':false,'extended':false,'injected':true,'lowerIntegrityInjected':false,'altDown':false,'extraInformation':-1,'eventTimeMilliseconds':123456800}")),
        ("input.keyboard-hook", "hook-keyboard",
            J("{'installation':2,'virtualKey':40,'scanCode':80,'flags':129,'up':true,'extended':true,'injected':false,'lowerIntegrityInjected':false,'altDown':false,'extraInformation':0,'eventTimeMilliseconds':123456900}")),
        ("input.keyboard-hook", "hook-installed",
            J("{'installation':1,'reason':'recording-started','installed':true,'problem':null,'previousInstallation':null,'previousKeys':null,'previousMaxCallbackMicroseconds':null,'keysDropped':0,'lastHookKeyAt':null,'unmatchedRawKeyAt':null,'unmatchedScanCode':null}")),
        ("input.keyboard-hook", "hook-installed",
            J("{'installation':2,'reason':'refresh','installed':true,'problem':null,'previousInstallation':1,'previousKeys':12,'previousMaxCallbackMicroseconds':38.5,'keysDropped':0,'lastHookKeyAt':null,'unmatchedRawKeyAt':null,'unmatchedScanCode':null}")),
        ("input.keyboard-hook", "hook-installed",
            J("{'installation':3,'reason':'hook-lost','installed':false,'problem':'SetWindowsHookEx failed with error 5.','previousInstallation':null,'previousKeys':null,'previousMaxCallbackMicroseconds':null,'keysDropped':2,'lastHookKeyAt':1200,'unmatchedRawKeyAt':1500,'unmatchedScanCode':30}")),
        // Added with 0026_assistive_technology.sql: the watch, with a browser
        // and without one and with the meters unavailable; NVDA's portable
        // copy running at the start and its helper; one started later whose
        // path could not be read; their exits; its module seen in the
        // browser, then gone, once with the browser; and a period of sound
        // ending each way.
        ("system.assistive-technology", "assistive-technology-watch",
            J("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'processPollMilliseconds':250,'modulePollMilliseconds':1000,'soundSampleMilliseconds':20,'soundThreshold':0.001,'soundGapMilliseconds':250,'browserExecutablePath':'C:\\\\Recorder\\\\browser\\\\chrome.exe','soundProblem':null}")),
        ("system.assistive-technology", "assistive-technology-watch",
            J("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'processPollMilliseconds':250,'modulePollMilliseconds':1000,'soundSampleMilliseconds':20,'soundThreshold':0.001,'soundGapMilliseconds':250,'browserExecutablePath':null,'soundProblem':'The audio devices could not be listed: Class not registered'}")),
        ("system.assistive-technology", "assistive-technology-process-started",
            J("{'product':'NVDA','role':'screen-reader','basis':'known-executable','executablePath':'C:\\\\Users\\\\User\\\\Desktop\\\\NVDA\\\\nvda.exe','fileVersion':'2026.2.0.12345','productVersion':'2026.2','processId':5120,'parentProcessId':4100,'startedUtc':'2026-10-10T17:59:12.1234567+00:00','runningAtStart':true,'copy':'portable','problem':null}")),
        ("system.assistive-technology", "assistive-technology-process-started",
            J("{'product':'NVDA','role':'helper','basis':'child-in-folder','executablePath':'C:\\\\Users\\\\User\\\\Desktop\\\\NVDA\\\\nvda_slave.exe','fileVersion':null,'productVersion':null,'processId':5200,'parentProcessId':5120,'startedUtc':'2026-10-10T17:59:13+00:00','runningAtStart':false,'copy':'portable','problem':null}")),
        ("system.assistive-technology", "assistive-technology-process-started",
            J("{'product':'NVDA','role':'screen-reader','basis':'known-executable','executablePath':null,'fileVersion':null,'productVersion':null,'processId':6100,'parentProcessId':null,'startedUtc':null,'runningAtStart':false,'copy':'unknown','problem':'The process\\u0027s executable path could not be read.'}")),
        ("system.assistive-technology", "assistive-technology-process-exited",
            J("{'product':'NVDA','role':'screen-reader','processId':5120,'startedUtc':'2026-10-10T17:59:12.1234567+00:00','exitedUtc':'2026-10-10T18:05:00.5+00:00','exitCode':0}")),
        ("system.assistive-technology", "assistive-technology-process-exited",
            J("{'product':'NVDA','role':'helper','processId':5200,'startedUtc':null,'exitedUtc':null,'exitCode':null}")),
        ("system.assistive-technology", "assistive-technology-module-loaded",
            J("{'product':'NVDA','moduleName':'nvdahelperremote.dll','modulePath':'C:\\\\Users\\\\User\\\\Desktop\\\\NVDA\\\\lib64\\\\2026.2\\\\nvdaHelperRemote.dll','fileVersion':'2026.2.0.12345','hostProcessId':4000,'hostExecutablePath':'C:\\\\Recorder\\\\browser\\\\chrome.exe','hostExited':false}")),
        ("system.assistive-technology", "assistive-technology-module-unloaded",
            J("{'product':'NVDA','moduleName':'nvdahelperremote.dll','modulePath':'C:\\\\Users\\\\User\\\\Desktop\\\\NVDA\\\\lib64\\\\2026.2\\\\nvdaHelperRemote.dll','fileVersion':null,'hostProcessId':4000,'hostExecutablePath':'C:\\\\Recorder\\\\browser\\\\chrome.exe','hostExited':true}")),
        ("system.assistive-technology", "assistive-technology-sound-started",
            J("{'product':'NVDA','processId':5120,'peak':0.0421}")),
        ("system.assistive-technology", "assistive-technology-sound-ended",
            J("{'product':'NVDA','processId':5120,'startedAt':1000,'lastSoundAt':1500,'maxPeak':0.31,'endedBy':'silence'}")),
        ("system.assistive-technology", "assistive-technology-sound-ended",
            J("{'product':'NVDA','processId':5120,'startedAt':1200,'lastSoundAt':1200,'maxPeak':0.002,'endedBy':'stop'}")),
        // Added with 0022_magnifier_changes.sql: a level and position change,
        // a pan, a color effect turned on, and readings that failed.
        ("graphics.magnifier", "magnifier-changed",
            """
            {"frameSequence":12,"previousFrameAt":2200000000,"changed":{"level":true,"position":true,"colorEffect":false},"previous":{"fullscreenMagnification":{"level":1,"x":0,"y":0,"problem":null},"fullscreenColorEffect":{"matrix":[1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1],"problem":null}},"current":{"fullscreenMagnification":{"level":2,"x":480,"y":270,"problem":null},"fullscreenColorEffect":{"matrix":[1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1],"problem":null}}}
            """),
        ("graphics.magnifier", "magnifier-changed",
            """
            {"frameSequence":13,"previousFrameAt":2400000000,"changed":{"level":false,"position":true,"colorEffect":false},"previous":{"fullscreenMagnification":{"level":2,"x":480,"y":270,"problem":null},"fullscreenColorEffect":{"matrix":[1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1],"problem":null}},"current":{"fullscreenMagnification":{"level":2,"x":500,"y":270,"problem":null},"fullscreenColorEffect":{"matrix":[1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1],"problem":null}}}
            """),
        ("graphics.magnifier", "magnifier-changed",
            """
            {"frameSequence":30,"previousFrameAt":6000000000,"changed":{"level":false,"position":false,"colorEffect":true},"previous":{"fullscreenMagnification":{"level":2,"x":500,"y":270,"problem":null},"fullscreenColorEffect":{"matrix":[1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,0,0,1],"problem":null}},"current":{"fullscreenMagnification":{"level":2,"x":500,"y":270,"problem":null},"fullscreenColorEffect":{"matrix":[-1,0,0,0,0,0,-1,0,0,0,0,0,-1,0,0,0,0,0,0,0,1,1,1,0,1],"problem":null}}}
            """),
        ("graphics.magnifier", "magnifier-changed",
            """
            {"frameSequence":31,"previousFrameAt":6200000000,"changed":{"level":true,"position":true,"colorEffect":true},"previous":{"fullscreenMagnification":{"level":2,"x":500,"y":270,"problem":null},"fullscreenColorEffect":{"matrix":[-1,0,0,0,0,0,-1,0,0,0,0,0,-1,0,0,0,0,0,0,0,1,1,1,0,1],"problem":null}},"current":{"fullscreenMagnification":{"level":null,"x":null,"y":null,"problem":"MagGetFullscreenTransform failed with error 21."},"fullscreenColorEffect":{"matrix":null,"problem":"MagGetFullscreenColorEffect failed with error 21."}}}
            """),
        ("accessibility.uia.events", "focus-changed",
            @"{""eventId"":""UIA_AutomationFocusChangedEventId"",""changeType"":null,""runtimeId"":[42,1311000,-4]," +
            @"""newValue"":null,""element"":" + Element + "}"),
        ("accessibility.uia.events", "property-changed",
            @"{""eventId"":""UIA_NamePropertyId"",""changeType"":null,""runtimeId"":[],""newValue"":""Saved\ttwice""," +
            @"""element"":" + Element + "}"),
        ("accessibility.uia.events", "structure-changed",
            @"{""eventId"":""UIA_StructureChangedEventId"",""changeType"":""ChildAdded"",""runtimeId"":null," +
            @"""newValue"":null,""element"":" + BareElement + "}"),
        ("accessibility.uia.events", "automation-event",
            @"{""eventId"":""UIA_Window_WindowOpenedEventId"",""changeType"":null,""runtimeId"":[42],""newValue"":null," +
            @"""element"":" + Element + "}"),
        ("accessibility.uia.events", "collector-omission",
            @"{""reason"":""uia-observation-queue-full"",""count"":5,""firstDroppedAtNanoseconds"":100," +
            @"""lastDroppedAtNanoseconds"":2000,""droppedByObservationType"":{""focus-changed"":2,""property-changed"":3}}"),
        ("accessibility.uia.events", "collector-omission", @"{""reason"":""uia-observation-queue-full"",""count"":4}"),
        ("window.foreground", "collector-omission", @"{""reason"":""foreground-queue-full"",""count"":1}"),
        ("graphics.desktop.frames", "desktop-frame",
            @"{""path"":""frames/desktop/0000000001.png"",""x"":-1920,""y"":0,""width"":3840,""height"":1080,""stride"":15360," +
            @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":123456,""captureDurationNanoseconds"":8000000," +
            @"""framesPerSecond"":5,""backend"":""windows-graphics-capture"",""monitorCount"":2,""fallbackReason"":null," +
            @"""gdiFallbackFrameCount"":0,""frameSelection"":""newest-arrived"",""monitorFrames"":[" +
            @"{""monitorHandle"":65537,""x"":-1920,""y"":0,""width"":1920,""height"":1080,""systemRelativeTimeTicks"":900," +
            @"""compositedAtNanoseconds"":-5,""dequeuedAtNanoseconds"":100,""tryGetNextFrameAttempts"":1," +
            @"""supersededFrameCount"":2,""reusedPreviousImage"":false}," +
            @"{""monitorHandle"":65539,""x"":0,""y"":0,""width"":1920,""height"":1080,""systemRelativeTimeTicks"":901," +
            @"""compositedAtNanoseconds"":6,""dequeuedAtNanoseconds"":101,""tryGetNextFrameAttempts"":2," +
            @"""supersededFrameCount"":0,""reusedPreviousImage"":true}]}"),
        ("graphics.desktop.frames", "desktop-frame",
            @"{""path"":""frames/desktop/0000000002.png"",""x"":0,""y"":0,""width"":1920,""height"":1080,""stride"":7680," +
            @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":0,""captureDurationNanoseconds"":0," +
            @"""framesPerSecond"":5,""backend"":""gdi-bitblt"",""monitorCount"":null,""fallbackReason"":""capture item lost""," +
            @"""gdiFallbackFrameCount"":1,""monitorFrames"":[" +
            @"{""monitorHandle"":65537,""x"":0,""y"":0,""width"":1920,""height"":1080,""systemRelativeTimeTicks"":null," +
            @"""compositedAtNanoseconds"":null,""dequeuedAtNanoseconds"":null,""tryGetNextFrameAttempts"":null}]}"),
        ("graphics.desktop.frames", "desktop-frame",
            @"{""path"":""frames/desktop/0000000003.png"",""x"":0,""y"":0,""width"":1920,""height"":1080,""stride"":7680," +
            @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":5,""captureDurationNanoseconds"":1," +
            @"""framesPerSecond"":5,""backend"":""windows-graphics-capture"",""monitorCount"":1,""fallbackReason"":null," +
            @"""gdiFallbackFrameCount"":0}"),
        ("graphics.desktop.frames", "desktop-frame",
            @"{""path"":""frames/desktop/0000000004.png"",""x"":0,""y"":0,""width"":1920,""height"":1080,""stride"":7680," +
            @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":5,""captureDurationNanoseconds"":1," +
            @"""framesPerSecond"":5,""backend"":""windows-graphics-capture"",""monitorCount"":1,""fallbackReason"":null," +
            @"""gdiFallbackFrameCount"":0,""fullscreenMagnification"":{""level"":2.5,""x"":3,""y"":-2,""problem"":null}," +
            @"""fullscreenColorEffect"":{""matrix"":[-1,0,0,0,0,0,-1,0,0,0,0,0,-1,0,0,0,0,0,1,0,1,1,1,0,1],""problem"":null}}"),
        ("graphics.desktop.frames", "desktop-frame",
            @"{""path"":""frames/desktop/0000000005.png"",""x"":0,""y"":0,""width"":1920,""height"":1080,""stride"":7680," +
            @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":5,""captureDurationNanoseconds"":1," +
            @"""framesPerSecond"":5,""backend"":""windows-graphics-capture"",""monitorCount"":1,""fallbackReason"":null," +
            @"""gdiFallbackFrameCount"":0,""fullscreenMagnification"":{""level"":null,""x"":null,""y"":null," +
            @"""problem"":""MagInitialize failed with error 5.""}," +
            @"""fullscreenColorEffect"":{""matrix"":null,""problem"":""MagInitialize failed with error 5.""}}"),
        ("graphics.desktop.frames", "collector-omission", @"{""reason"":""frame-write-failed""}"),
        ("audio.microphone", "audio-stream-started",
            @"{""stream"":""microphone"",""path"":""audio/microphone.wav"",""device"":""Microphone (USB)""," +
            @"""endpointVolumeScalar"":0.75,""format"":" + Format + @",""dataBytes"":0,""buffersObserved"":0," +
            @"""buffersDropped"":0}"),
        ("audio.microphone", "audio-stream-stopped",
            @"{""stream"":""microphone"",""path"":""audio/microphone.wav"",""device"":""Microphone (USB)""," +
            @"""endpointVolumeScalar"":0.75,""format"":" + Format + @",""dataBytes"":192000,""buffersObserved"":100," +
            @"""buffersDropped"":1,""peakAmplitude"":0.5,""peakDbfs"":-6.020599913279624,""rmsAmplitude"":0.125," +
            @"""rmsDbfs"":-18.06179973983887}"),
        ("audio.system", "audio-stream-started",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""device"":""Speakers"",""format"":" + Format + "," +
            @"""dataBytes"":0,""buffersObserved"":0,""buffersDropped"":0}"),
        ("audio.system", "audio-stream-stopped",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""device"":""Speakers"",""format"":" + Format + "," +
            @"""dataBytes"":384000,""buffersObserved"":200,""buffersDropped"":0}"),
        ("audio.system", "audio-buffer",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""bufferSequence"":0,""dataByteOffset"":0," +
            @"""byteLength"":0,""sampleFrames"":0,""durationNanoseconds"":0," +
            @"""estimatedFirstSampleMonotonicNanoseconds"":0,""callbackMonotonicNanoseconds"":0}"),
        ("audio.microphone", "audio-buffer",
            @"{""stream"":""microphone"",""path"":""audio/microphone.wav"",""bufferSequence"":0,""dataByteOffset"":0," +
            @"""byteLength"":1920,""sampleFrames"":480,""durationNanoseconds"":10000000," +
            @"""estimatedFirstSampleMonotonicNanoseconds"":1000,""callbackMonotonicNanoseconds"":11000000}"),
        ("audio.microphone", "audio-buffer",
            @"{""stream"":""microphone"",""path"":""audio/microphone.wav"",""bufferSequence"":1,""dataByteOffset"":1920," +
            @"""byteLength"":1920,""sampleFrames"":480,""durationNanoseconds"":10000000," +
            @"""estimatedFirstSampleMonotonicNanoseconds"":10001000,""callbackMonotonicNanoseconds"":21000000}"),
        ("audio.system", "audio-stream-error",
            @"{""stream"":""system"",""errorType"":""COMException"",""message"":""The device was removed.""}"),
        ("audio.microphone", "audio-stream-error", @"{""stream"":""microphone"",""errorType"":null,""message"":null}"),
        ("audio.system", "collector-omission", @"{""reason"":""audio-buffer-dropped"",""count"":3,""stream"":""system""}"),
        ("audio.microphone", "collector-omission", @"{""reason"":""audio-device-missing""}"),
        ("browser.lifecycle", "browser-connected",
            @"{""protocolVersion"":""1"",""browserInstanceId"":""browser-1"",""processId"":4000,""processType"":""browser""," +
            @"""chromiumVersion"":""142.0.7400.1"",""parentProcessId"":null,""childProcessId"":null}"),
        ("browser.lifecycle", "browser-connected",
            @"{""protocolVersion"":""1"",""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer""," +
            @"""chromiumVersion"":""142.0.7400.1"",""parentProcessId"":4000,""childProcessId"":7}"),
        ("browser.lifecycle", "browser-exited",
            @"{""browserInstanceId"":""browser-1"",""processId"":4000,""exitCode"":-1073741510," +
            @"""exitCodeHex"":""0xC000013A"",""exitedUtc"":""2026-09-25T12:00:02.0000001+00:00"",""requestedByRecorder"":true}"),
        ("browser.lifecycle", "browser-exited",
            @"{""browserInstanceId"":""browser-1"",""processId"":4000,""exitCode"":0,""exitCodeHex"":""0x00000000""," +
            @"""exitedUtc"":null,""requestedByRecorder"":false}"),
        ("browser.lifecycle", "browser-clock-synchronized",
            @"{""protocolVersion"":""1"",""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer""," +
            @"""parentProcessId"":4000,""childProcessId"":7,""clockMappingId"":""chromium:browser-1:4100""," +
            @"""monotonicFrequency"":""10000000"",""uncertaintyNanoseconds"":250000}"),
        ("browser.lifecycle", "collector-omission",
            @"{""reason"":""browser-queue-full"",""count"":2,""context"":" + Context + "}"),
        ("browser.lifecycle", "collector-omission", @"{""reason"":""browser-queue-full"",""count"":1}"),
        ("browser.listener", "listener-registered",
            J("{'context':") + Context + J(",'listenerId':'listener-7','eventName':'click','registrationKind':'add-event-listener','target':") +
            ButtonTarget + J(",'capture':false,'passive':false,'once':false,'location':") + Location + J(",'world':null}")),
        ("browser.listener", "listener-removed",
            J("{'context':") + WorldContext + J(",'listenerId':'listener-8','eventName':'keydown','registrationKind':'inline-attribute','target':") +
            WindowTarget + J(",'capture':true,'passive':true,'once':true,'location':") + BareLocation +
            J(",'world':{'kind':'isolated','blinkWorldId':1,'name':'extension','stableId':null},") +
            J("'scope':{'contextKind':'window','workerToken':null,'globalObjectUrl':'https://example.test/'}}")),
        ("browser.listener", "listener-callback-replaced",
            J("{'context':") + WorkerContext + J(",'listenerId':'listener-9','eventName':'message','registrationKind':'event-handler-property','target':") +
            WorkerTarget + J(",'capture':false,'passive':false,'once':false,'location':null,'world':null,") +
            J("'scope':{'contextKind':'dedicated-worker','workerToken':'WORKER-1','globalObjectUrl':'https://example.test/worker.js'}}")),
        ("browser.dispatch", "dispatch-started", BrowserShadowDomPayloads.DispatchStarted),
        ("browser.dispatch", "listener-invoked",
            J("{'context':") + Context + J(",'dispatchId':'dispatch-5','eventName':'click','trusted':true,'originalTarget':") + ButtonTarget + "," +
            OneStepPath + J(",'phase':'at-target','listenerId':'listener-7','defaultPrevented':true,'propagationStopped':false,") +
            J("'immediatePropagationStopped':false,'defaultAction':null,'outcome':null,'currentTarget':") + ButtonTarget + "}"),
        ("browser.dispatch", "dispatch-completed",
            J("{'context':") + WorkerContext + J(",'dispatchId':'dispatch-6','eventName':'message','trusted':false,'originalTarget':null,") +
            J("'composedPath':[") + WorkerTarget + J("],'pathScopes':[{'treeScopeRootNodeId':null,'shadowRootMode':null,'targetNodeId':null,") +
            J("'relatedTargetNodeId':null,'visiblePathIndexes':[0],'unmatchedVisibleTargetCount':0}],'phase':'none','listenerId':null,") +
            J("'defaultPrevented':false,'propagationStopped':true,'immediatePropagationStopped':true,'defaultAction':null,'outcome':null,") +
            J("'scope':{'contextKind':'dedicated-worker','workerToken':'WORKER-1','globalObjectUrl':'https://example.test/worker.js'}}")),
        ("browser.dispatch", "default-action",
            J("{'context':") + Context + J(",'dispatchId':'dispatch-5','eventName':'click','trusted':true,'originalTarget':") + ButtonTarget + "," +
            OneStepPath + J(",'phase':'none','listenerId':null,'defaultPrevented':false,'propagationStopped':false,") +
            J("'immediatePropagationStopped':false,'defaultAction':'blink-default-event-handler','outcome':'invoked','currentTarget':") +
            ButtonTarget + "}"),
        ("browser.timer", "timer-scheduled",
            J("{'context':") + Context + J(",'timerId':'timer-1','timerKind':'timeout','requestedDelayMilliseconds':100,") +
            J("'effectiveDelayMilliseconds':100.5,'nestingLevel':0,'throttled':null,'pageLifecycleState':'visible','callbackLocation':") +
            Location + J(",'cancellationReason':null}")),
        ("browser.timer", "timer-fired",
            J("{'context':") + Context + J(",'timerId':'timer-2','timerKind':'idle-callback','requestedDelayMilliseconds':null,") +
            J("'effectiveDelayMilliseconds':null,'nestingLevel':3,'throttled':true,'pageLifecycleState':'hidden','callbackLocation':null,") +
            J("'cancellationReason':null,'didTimeout':true}")),
        ("browser.timer", "timer-cancelled",
            J("{'context':") + Context + J(",'timerId':'timer-1','timerKind':'timeout','requestedDelayMilliseconds':100,") +
            J("'effectiveDelayMilliseconds':100.5,'nestingLevel':0,'throttled':false,'pageLifecycleState':'visible','callbackLocation':") +
            Location + J(",'cancellationReason':'clear-timeout','didTimeout':false}")),
        ("browser.scheduler", "wake-up-deferred",
            J("{'context':") + WorkerContext + "," + Scheduler +
            J("'deferralMilliseconds':544.25,'hasReadyTask':false,'blockType':'all-tasks','decisionBoundary':'task-queue-throttler'}")),
        ("browser.navigation", "navigation-started",
            J("{'context':") + NavigationContext + J(",'parentFrameId':null,'parentOrOuterDocumentFrameId':null,'frameType':'primary-main-frame',") +
            J("'primaryPage':true,'navigationId':'navigation-40','url':'https://example.test/','navigationKind':'cross-document',") +
            J("'rendererInitiated':false,'sameDocument':false,'committed':null,'errorPage':null,'netErrorCode':null,'outcome':null,") +
            J("'rendererProcessId':null}")),
        ("browser.navigation", "navigation-completed",
            J("{'context':") + CommittedContext + J(",'parentFrameId':null,'parentOrOuterDocumentFrameId':null,'frameType':'primary-main-frame',") +
            J("'primaryPage':true,'navigationId':'navigation-40','url':'https://example.test/','navigationKind':'cross-document',") +
            J("'rendererInitiated':false,'sameDocument':false,'committed':true,'errorPage':false,'netErrorCode':0,'outcome':'committed',") +
            J("'rendererProcessId':4100}")),
        ("browser.navigation", "navigation-completed",
            J("{'context':") + SubframeContext + J(",'parentFrameId':'frame-12','parentOrOuterDocumentFrameId':'frame-12','frameType':'subframe',") +
            J("'primaryPage':true,'navigationId':'navigation-41','url':'https://ads.example.test/','navigationKind':'cross-document',") +
            J("'rendererInitiated':true,'sameDocument':false,'committed':false,'errorPage':false,'netErrorCode':-3,'outcome':'not-committed',") +
            J("'rendererProcessId':null}")),
        ("browser.listener", "collector-omission", J("{'reason':'browser-queue-full','count':3,'context':") + Context + "}"),
        ("browser.dispatch", "collector-omission", J("{'reason':'browser-queue-full'}")),
        ("browser.timer", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.scheduler", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.navigation", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.accessibility", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.dom", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.cookie", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.interaction", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.layout", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.presentation", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.network", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.resources", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.compositor", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.animation", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.script", "collector-omission", J("{'reason':'browser-queue-full','count':1}")),
        ("browser.accessibility", "accessibility-checkpoint-started",
            J("{'context':") + AxContext + J(",'checkpointId':'accessibility-checkpoint-1','reason':'renderer-serialization','maximumNodes':5000,'updateCount':3,'eventCount':7}")),
        ("browser.accessibility", "accessibility-checkpoint-node",
            J("{'context':") + AxContext + J(",'checkpointId':'accessibility-checkpoint-1','nodeIndex':0,'accessibilityNodeId':1,'parentAccessibilityNodeId':null,'domNodeId':null,'role':144,'roleName':'rootWebArea','name':'Example','description':'','serializedProperties':'{}','focused':false}")),
        ("browser.accessibility", "accessibility-checkpoint-node",
            J("{'context':") + AxContext + J(",'checkpointId':'accessibility-checkpoint-1','nodeIndex':1,'accessibilityNodeId':-7,'parentAccessibilityNodeId':1,'domNodeId':42,'role':9,'roleName':'button','name':'Save','description':'Saves the form','serializedProperties':'{\\'hasPopup\\':false}','focused':true}")),
        ("browser.accessibility", "accessibility-checkpoint-completed",
            J("{'context':") + AxContext + J(",'checkpointId':'accessibility-checkpoint-1','reason':'renderer-serialization','nodeCount':2,'truncated':false,'maximumNodes':5000,'updateCount':3,'eventCount':7}")),
        ("browser.dom", "dom-checkpoint-started",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','reason':'finished-parsing','walkReason':'first','maximumNodes':10000}")),
        ("browser.dom", "dom-checkpoint-started",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-2','reason':'post-mutation','walkReason':'first','maximumNodes':10000,'frameToken':'0123456789ABCDEF0123456789ABCDEF','mainFrame':false}")),
        ("browser.dom", "dom-checkpoint-frame-owner",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-2','ownerNodeId':44,'frameToken':'FEDCBA9876543210FEDCBA9876543210','frameLocation':'remote'}")),
        ("browser.dom", "dom-frame-owner-changed",
            J("{'context':") + Context + J(",'ownerNodeId':44,'frameToken':'FEDCBA9876543210FEDCBA9876543210','frameLocation':'local'}")),
        ("browser.dom", "dom-frame-owner-changed",
            J("{'context':") + Context + J(",'ownerNodeId':44,'frameToken':null,'frameLocation':null}")),
        ("browser.dom", "dom-checkpoint-node",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','nodeIndex':0,'nodeId':19,'parentNodeId':null,'nodeType':'document','nodeName':'#document'}")),
        ("browser.dom", "dom-checkpoint-node",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','nodeIndex':1,'nodeId':42,'parentNodeId':19,'nodeType':'element','nodeName':'BUTTON'}")),
        ("browser.dom", "dom-checkpoint-node-attribute",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','nodeId':42,'attributeIndex':0,'attributeNamespace':null,'attributeName':'aria-label','attributeValue':'Sa','attributeValueLength':4,'attributeValueTruncated':true,'maximumValueLength':2}")),
        ("browser.dom", "dom-checkpoint-node-attribute",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','nodeId':43,'attributeIndex':1,'attributeNamespace':'http://www.w3.org/1999/xlink','attributeName':'href','attributeValue':'','attributeValueLength':0,'attributeValueTruncated':false,'maximumValueLength':1024}")),
        ("browser.dom", "dom-checkpoint-shadow-root",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','nodeId':61,'hostNodeId':60,'mode':'closed','delegatesFocus':true,'slotAssignment':'named','clonable':false,'serializable':false,'declarative':false,'availableToElementInternals':true,'referenceTarget':null}")),
        ("browser.dom", "dom-checkpoint-shadow-root",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','nodeId':71,'hostNodeId':70,'mode':'open','delegatesFocus':false,'slotAssignment':'manual','clonable':true,'serializable':true,'declarative':true,'availableToElementInternals':false,'referenceTarget':'inner'}")),
        ("browser.dom", "dom-checkpoint-slot-assignment",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','nodeId':63,'assignedNodeIds':[64,null,66],'assignedNodeCount':4,'assignedNodesTruncated':true,'maximumAssignedNodes':3,'assignmentCurrent':true}")),
        ("browser.dom", "dom-checkpoint-slot-assignment",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','nodeId':73,'assignedNodeIds':[],'assignedNodeCount':0,'assignedNodesTruncated':false,'maximumAssignedNodes':3,'assignmentCurrent':false}")),
        ("browser.dom", "dom-checkpoint-completed",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-1','reason':'finished-parsing','nodeCount':2,'truncated':false,'maximumNodes':10000,'attributeCount':2,'attributesTruncated':false,'maximumAttributesPerNode':64,'maximumValueLength':1024,'coveredTransitionCount':0,'coveredTransitionFirstId':null,'coveredTransitionLastId':null,'shadowRootCount':2,'slotCount':2}")),
        ("browser.dom", "dom-checkpoint-completed",
            J("{'context':") + Context + J(",'checkpointId':'dom-checkpoint-2','reason':'post-mutation','nodeCount':10000,'truncated':true,'maximumNodes':10000,'attributeCount':900,'attributesTruncated':true,'maximumAttributesPerNode':64,'maximumValueLength':1024,'coveredTransitionCount':2,'coveredTransitionFirstId':'dom-transition-1','coveredTransitionLastId':'dom-transition-2','shadowRootCount':0,'slotCount':0}")),
        ("browser.dom", "dom-attribute-changed",
            J("{'context':") + Context + J(",'transitionId':'dom-transition-1','nodeId':42,'nodeName':'BUTTON','attributeNamespace':null,'attributeName':'aria-expanded','changeType':'changed','attributeValue':'true','attributeValueLength':4,'attributeValueTruncated':false,'previousAttributeValue':'false','previousAttributeValueLength':5,'previousAttributeValueTruncated':false,'maximumValueLength':1024}")),
        ("browser.dom", "dom-attribute-changed",
            J("{'context':") + Context + J(",'transitionId':'dom-transition-2','nodeId':42,'nodeName':'BUTTON','attributeNamespace':null,'attributeName':'hidden','changeType':'removed','attributeValue':null,'attributeValueLength':null,'attributeValueTruncated':false,'previousAttributeValue':'','previousAttributeValueLength':0,'previousAttributeValueTruncated':false,'maximumValueLength':1024}")),
        ("browser.dom", "dom-character-data-changed",
            J("{'context':") + Context + J(",'transitionId':'dom-transition-3','nodeId':44,'parentNodeId':42,'nodeType':'text','text':'Saved','textLength':5,'textTruncated':false,'previousText':'Sav','previousTextLength':4,'previousTextTruncated':true,'maximumValueLength':3}")),
        ("browser.interaction", "focus-changed",
            J("{'context':") + Context + J(",'previousNodeId':null,'requestedNodeId':42,'focusedNodeId':42,'outcome':'focused','activeDescendantNodeId':null,'focusType':'forward','focusTrigger':'user-gesture','preventScroll':false,'focusVisible':true,'location':null,'world':null}")),
        ("browser.interaction", "focus-changed",
            J("{'context':") + WorldContext + J(",'previousNodeId':42,'requestedNodeId':50,'focusedNodeId':51,'outcome':'redirected','activeDescendantNodeId':52,'focusType':'script','focusTrigger':'script','preventScroll':true,'focusVisible':null,'location':") + Location + J(",'world':") + World + "}"),
        ("browser.interaction", "selection-changed",
            J("{'context':") + Context + J(",'setBy':'user','selectionType':'range','anchorNodeId':44,'anchorOffset':0,'focusNodeId':44,'focusOffset':5,'directional':true,'textControlNodeId':45,'textControlSelectionStart':1,'textControlSelectionEnd':3,'textControlSelectionDirection':'backward','location':null,'world':null}")),
        ("browser.interaction", "selection-changed",
            J("{'context':") + WorldContext + J(",'setBy':'system','selectionType':'none','anchorNodeId':null,'anchorOffset':null,'focusNodeId':null,'focusOffset':null,'directional':false,'textControlNodeId':null,'textControlSelectionStart':null,'textControlSelectionEnd':null,'textControlSelectionDirection':null,'location':") + Location + J(",'world':") + World + "}"),
        ("browser.interaction", "text-control-value-changed",
            J("{'context':") + Context + J(",'nodeId':45,'controlType':'text','source':'user-edit','value':'hel','valueLength':5,'valueTruncated':true,'maximumValueLength':3,'selectionStart':3,'selectionEnd':3,'selectionDirection':'none','location':null,'world':null}")),
        ("browser.interaction", "active-descendant-reference-set",
            J("{'context':") + WorldContext + J(",'nodeId':50,'referencedNodeId':52,'location':") + Location + J(",'world':") + World + "}"),
        ("browser.interaction", "interaction-checkpoint-started",
            J("{'context':") + Context + J(",'checkpointId':'interaction-checkpoint-1','sourceCheckpointId':'dom-checkpoint-1','sourceChangeSetId':null,'sourceChannel':'browser.dom','reason':'finished-parsing','documentHasFocus':true,'focusedNodeId':42,'focusVisible':true,'activeDescendantNodeId':52,'lastFocusType':'forward','selectionType':'caret','anchorNodeId':44,'anchorOffset':2,'focusNodeId':44,'focusOffset':2,'directional':false,'maximumTextControls':32,'maximumValueLength':1024}")),
        ("browser.interaction", "interaction-checkpoint-started",
            J("{'context':") + Context + J(",'checkpointId':'interaction-checkpoint-2','sourceCheckpointId':'layout-checkpoint-3','sourceChangeSetId':null,'sourceChannel':'browser.layout','reason':'rendering-update','documentHasFocus':false,'focusedNodeId':null,'focusVisible':false,'activeDescendantNodeId':null,'lastFocusType':'none','selectionType':'none','anchorNodeId':null,'anchorOffset':null,'focusNodeId':null,'focusOffset':null,'directional':false,'maximumTextControls':32,'maximumValueLength':1024}")),
        ("browser.interaction", "interaction-checkpoint-text-control",
            J("{'context':") + Context + J(",'checkpointId':'interaction-checkpoint-1','textControlIndex':0,'nodeId':45,'controlType':'textarea','value':'hello','valueLength':5,'valueTruncated':false,'selectionStart':0,'selectionEnd':5,'selectionDirection':'forward'}")),
        ("browser.interaction", "interaction-checkpoint-completed",
            J("{'context':") + Context + J(",'checkpointId':'interaction-checkpoint-1','textControlCount':1,'truncated':false,'maximumTextControls':32}")),
        ("browser.layout", "layout-checkpoint-started",
            J("{'context':") + Context + J(",'checkpointId':'layout-checkpoint-3','reason':'rendering-update','walkReason':'first','previousCheckpointId':null,'styleResolutionCount':4,'layoutCount':2,'viewport':{'width':1280,'height':720.5},'scrollOffset':{'x':0,'y':-12.25},'devicePixelRatio':1.25,'layoutZoomFactor':1,'maximumNodes':5000,'styleProperties':['display','visibility','outline-style']}")),
        ("browser.layout", "layout-checkpoint-started",
            J("{'context':") + Context + J(",'checkpointId':'layout-checkpoint-4','reason':'rendering-update','walkReason':'first','previousCheckpointId':'layout-checkpoint-3','styleResolutionCount':0,'layoutCount':0,'viewport':{'width':0,'height':0},'scrollOffset':{'x':10,'y':0},'devicePixelRatio':2,'layoutZoomFactor':0.5,'maximumNodes':5000,'styleProperties':['display']}")),
        ("browser.layout", "layout-checkpoint-node",
            J("{'context':") + Context + J(",'checkpointId':'layout-checkpoint-3','nodeIndex':0,'nodeId':42,'nodeType':'element','nodeName':'BUTTON','layoutObjectPresent':true,'displayLocked':false,'boundingClientRect':{'x':8,'y':16.5,'width':120,'height':32},'computedStyle':{'display':'inline-block','visibility':'visible','outline-style':null},'pseudoElement':null,'shadowHostNodeId':null,'shadowRootMode':null}")),
        ("browser.layout", "layout-checkpoint-node",
            J("{'context':") + Context + J(",'checkpointId':'layout-checkpoint-3','nodeIndex':1,'nodeId':44,'nodeType':'text','nodeName':'#text','layoutObjectPresent':true,'displayLocked':false,'boundingClientRect':{'x':10,'y':20,'width':40,'height':18},'computedStyle':null,'pseudoElement':null,'shadowHostNodeId':60,'shadowRootMode':'closed'}")),
        ("browser.layout", "layout-checkpoint-node",
            J("{'context':") + Context + J(",'checkpointId':'layout-checkpoint-3','nodeIndex':2,'nodeId':80,'nodeType':'pseudo-element','nodeName':'::before','layoutObjectPresent':false,'displayLocked':true,'boundingClientRect':null,'computedStyle':{},'pseudoElement':{'originatingNodeId':42,'pseudoType':'before','generatedText':'\\u2192 ','generatedTextLength':2,'generatedTextTruncated':false},'shadowHostNodeId':null,'shadowRootMode':null}")),
        ("browser.layout", "layout-checkpoint-completed",
            J("{'context':") + Context + J(",'checkpointId':'layout-checkpoint-3','reason':'rendering-update','nodeCount':3,'truncated':false,'maximumNodes':5000,'pseudoElementCount':1,'shadowRootCount':1}")),
        ("browser.presentation", "presentation-requested",
            J("{'context':") + Context + J(",'requestId':'presentation-request-1','widgetKind':'frame','frameSinkId':'3:5','localRootFrameToken':'5E1B0A4C2D7F4E3A9B8C6D5E4F3A2B1C','layoutCheckpointId':'layout-checkpoint-3','layoutChangeSetId':null,'queued':true,'notQueuedReason':null,'sourceFrameNumber':88,'isMainFrameWidget':true,'highResolutionTicks':true,'maximumNotSwappedRecords':8}")),
        ("browser.presentation", "presentation-requested",
            J("{'context':") + Context + J(",'requestId':'presentation-request-2','widgetKind':null,'frameSinkId':null,'localRootFrameToken':null,'layoutCheckpointId':'layout-checkpoint-4','layoutChangeSetId':null,'queued':false,'notQueuedReason':'no-widget','sourceFrameNumber':null,'isMainFrameWidget':null,'highResolutionTicks':false,'maximumNotSwappedRecords':8}")),
        ("browser.presentation", "presentation-not-swapped",
            J("{'context':") + Context + J(",'requestId':'presentation-request-1','widgetKind':'frame','frameSinkId':'3:5','localRootFrameToken':'5E1B0A4C2D7F4E3A9B8C6D5E4F3A2B1C','reason':'commit-fails','action':'kept-active','notSwappedIndex':0,'notSwappedCount':1,'timestampTicks':null,'timestampTimeTicksMicroseconds':'1234567890'}")),
        ("browser.presentation", "presentation-swapped",
            J("{'context':") + Context + J(",'requestId':'presentation-request-1','widgetKind':'frame','frameSinkId':'3:5','localRootFrameToken':'5E1B0A4C2D7F4E3A9B8C6D5E4F3A2B1C','frameToken':'4294967295','notSwappedCount':1}")),
        ("browser.presentation", "presentation-feedback",
            J("{'context':") + Context + J(",'requestId':'presentation-request-1','widgetKind':'frame','frameSinkId':'3:5','localRootFrameToken':'5E1B0A4C2D7F4E3A9B8C6D5E4F3A2B1C','frameToken':'17','presentedTicks':'98765432109','presentedTimeTicksMicroseconds':'1234567999','intervalMicroseconds':'16667','flags':['vsync','hw-completion'],'receivedCompositorFrameTicks':'98765000000','drawStartTicks':null,'swapStartTicks':'98765100000','swapEndTicks':'98765200000','highResolutionTicks':true,'notSwappedCount':1}")),
        ("browser.presentation", "presentation-feedback",
            J("{'context':") + Context + J(",'requestId':'presentation-request-2','widgetKind':'frame','frameSinkId':'3:5','localRootFrameToken':'5E1B0A4C2D7F4E3A9B8C6D5E4F3A2B1C','frameToken':'18','presentedTicks':null,'presentedTimeTicksMicroseconds':null,'intervalMicroseconds':'0','flags':[],'receivedCompositorFrameTicks':null,'drawStartTicks':null,'swapStartTicks':null,'swapEndTicks':null,'highResolutionTicks':false,'notSwappedCount':0}")),
        ("browser.cookie", "document-cookie-read",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""accessId"":""cookie-access-1"",""cookieUrl"":""https://example.test/"",""outcome"":""returned"",""servedFrom"":""renderer-cache"",""cookieCount"":3,""cookieNames"":[""sid"",""theme""],""cookieNamesTruncated"":true,""location"":{""scriptId"":""script-2"",""url"":""https://example.test/app.js"",""line"":18,""column"":4,""functionName"":""activate"",""sourceHash"":""sha256:test""},""world"":null}"),
        ("browser.cookie", "document-cookie-read",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":""world-1"",""documentToken"":""TOKEN-1""},""accessId"":""cookie-access-2"",""cookieUrl"":null,""outcome"":""not-attempted-no-cookie-url"",""servedFrom"":null,""cookieCount"":0,""cookieNames"":[],""cookieNamesTruncated"":false,""location"":null,""world"":{""kind"":""isolated"",""blinkWorldId"":1,""name"":""extension"",""stableId"":null}}"),
        ("browser.cookie", "document-cookie-write",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""accessId"":""cookie-access-3"",""cookieUrl"":""https://example.test/"",""outcome"":""sent-to-cookie-manager"",""name"":""theme"",""attributes"":{""domain"":""example.test"",""path"":""/"",""sameSite"":null,""partitioned"":false,""expiresPresent"":false,""secure"":true,""httpOnly"":false,""maxAgePresent"":true,""attributeNames"":[""path"",""secure"",""max-age""]},""location"":{""scriptId"":""script-2"",""url"":""https://example.test/app.js"",""line"":18,""column"":4,""functionName"":""activate"",""sourceHash"":""sha256:test""},""world"":null}"),
        ("browser.cookie", "cookie-store-request",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":""world-1"",""documentToken"":""TOKEN-1""},""requestId"":""cookie-store-1"",""method"":""set"",""contextKind"":""window"",""outcome"":""sent-to-cookie-manager"",""name"":""theme"",""url"":null,""attributes"":{""domain"":null,""path"":""/"",""sameSite"":""strict"",""partitioned"":true,""expiresPresent"":true},""location"":{""scriptId"":""script-2"",""url"":""https://example.test/app.js"",""line"":18,""column"":4,""functionName"":""activate"",""sourceHash"":""sha256:test""},""world"":{""kind"":""isolated"",""blinkWorldId"":1,""name"":""extension"",""stableId"":null}}"),
        ("browser.cookie", "cookie-store-request",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""requestId"":""cookie-store-2"",""method"":""getAll"",""contextKind"":""service-worker"",""outcome"":""threw"",""name"":null,""url"":""https://example.test/"",""attributes"":null,""location"":null,""world"":null}"),
        ("browser.cookie", "cookie-store-result",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""requestId"":""cookie-store-2"",""method"":""getAll"",""outcome"":""resolved"",""success"":null,""cookieCount"":3,""cookieNames"":[""sid"",""theme""],""cookieNamesTruncated"":true}"),
        ("browser.cookie", "cookie-store-result",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""requestId"":""cookie-store-1"",""method"":""set"",""outcome"":""rejected"",""success"":false,""cookieCount"":null,""cookieNames"":null,""cookieNamesTruncated"":null}"),
        ("browser.cookie", "cookie-store-change",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""contextKind"":""window"",""name"":""theme"",""domain"":""example.test"",""path"":""/"",""cause"":""inserted"",""dispatched"":true}"),
        ("browser.cookie", "cookie-access",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""observer"":""navigation"",""navigationId"":""navigation-7"",""rendererProcessId"":null,""accessType"":""change"",""url"":""https://example.test/"",""frameOrigin"":null,""topFrameOrigin"":""https://example.test"",""requestId"":""1000.5"",""adTagged"":false,""cookieCount"":2,""cookies"":[{""name"":""sid"",""parsed"":true,""domain"":""example.test"",""path"":""/"",""sameSite"":""lax"",""secure"":true,""httpOnly"":true,""hostOnly"":true,""partitioned"":false,""persistent"":false,""expired"":false,""included"":true,""exclusionReasons"":[],""warningReasons"":[""warn-same-site-unspecified-lax-allow-unsafe""],""exemptionReason"":null},{""name"":""bad"",""parsed"":false,""domain"":null,""path"":null,""sameSite"":null,""secure"":null,""httpOnly"":null,""hostOnly"":null,""partitioned"":null,""persistent"":null,""expired"":null,""included"":false,""exclusionReasons"":[""exclude-invalid"",""exclude-secure-only""],""warningReasons"":[],""exemptionReason"":""user-setting""}],""cookiesTruncated"":false}"),
        ("browser.cookie", "cookie-access",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""observer"":""frame"",""navigationId"":null,""rendererProcessId"":4100,""accessType"":""read"",""url"":""https://example.test/"",""frameOrigin"":""https://example.test"",""topFrameOrigin"":null,""requestId"":null,""adTagged"":true,""cookieCount"":5,""cookies"":[{""name"":""sid"",""parsed"":true,""domain"":""example.test"",""path"":""/"",""sameSite"":""lax"",""secure"":true,""httpOnly"":true,""hostOnly"":true,""partitioned"":false,""persistent"":false,""expired"":false,""included"":true,""exclusionReasons"":[],""warningReasons"":[""warn-same-site-unspecified-lax-allow-unsafe""],""exemptionReason"":null}],""cookiesTruncated"":true}"),
        ("browser.network", "request-will-be-sent",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""request"":{""inspectorId"":""17"",""requestId"":""1000.5"",""url"":""https://example.test/api"",""method"":""GET"",""resourceType"":""fetch"",""initiator"":{""type"":""script"",""url"":""https://example.test/app.js"",""line"":18,""column"":4,""linkPreload"":false},""internal"":false,""destination"":""empty"",""mode"":""cors"",""credentialsMode"":""same-origin"",""redirectMode"":""follow"",""cacheMode"":""default"",""priority"":""high"",""initialPriority"":""medium"",""fetchPriorityHint"":""auto"",""renderBlocking"":""non-blocking"",""referrer"":""https://example.test/"",""referrerPolicy"":""strict-origin-when-cross-origin"",""keepalive"":false,""userGesture"":true,""adResource"":false,""formSubmission"":false,""headerCount"":3,""headers"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null},{""name"":""cookie"",""value"":null,""valueRedacted"":true,""redactionReason"":""credential-header""}],""headersTruncated"":true},""redirect"":false,""redirectResponse"":null,""location"":{""scriptId"":""script-2"",""url"":""https://example.test/app.js"",""line"":18,""column"":4,""functionName"":""activate"",""sourceHash"":""sha256:test""},""world"":null}"),
        ("browser.network", "request-will-be-sent",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":""world-1"",""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""dedicated-worker"",""workerToken"":""WORKER-1"",""globalObjectUrl"":""https://example.test/worker.js""},""request"":{""inspectorId"":""18"",""requestId"":null,""url"":""https://example.test/api"",""method"":""POST"",""resourceType"":""document"",""initiator"":{""type"":null,""url"":null,""line"":null,""column"":null,""linkPreload"":true},""internal"":false,""destination"":""empty"",""mode"":""cors"",""credentialsMode"":""same-origin"",""redirectMode"":""follow"",""cacheMode"":""default"",""priority"":""high"",""initialPriority"":""medium"",""fetchPriorityHint"":""auto"",""renderBlocking"":""non-blocking"",""referrer"":null,""referrerPolicy"":""strict-origin-when-cross-origin"",""keepalive"":false,""userGesture"":false,""adResource"":false,""formSubmission"":true,""headerCount"":0,""headers"":[],""headersTruncated"":false},""redirect"":true,""redirectResponse"":{""url"":""https://example.test/api"",""responseUrl"":""https://example.test/moved"",""status"":301,""statusText"":""Moved Permanently"",""mimeType"":""text/html"",""charset"":null,""alpnProtocol"":null,""connectionInfo"":null,""remoteAddress"":null,""connectionId"":0,""connectionReused"":false,""wasCached"":false,""fetchedViaServiceWorker"":false,""serviceWorkerResponseSource"":""unspecified"",""inPrefetchCache"":false,""networkAccessed"":true,""fromArchive"":false,""cookieInRequest"":false,""responseType"":""basic"",""encodedDataLength"":null,""expectedContentLength"":2048.5,""headerCount"":2,""headers"":[],""headersTruncated"":true,""timing"":null},""location"":null,""world"":{""kind"":""isolated"",""blinkWorldId"":1,""name"":""extension"",""stableId"":null}}"),
        ("browser.network", "response-received",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""17"",""requestId"":""1000.5"",""responseSource"":""loader"",""response"":{""url"":""https://example.test/api"",""responseUrl"":null,""status"":200,""statusText"":""OK"",""mimeType"":""application/json"",""charset"":""utf-8"",""alpnProtocol"":""h2"",""connectionInfo"":""h2"",""remoteAddress"":{""ip"":""203.0.113.5"",""port"":443},""connectionId"":12,""connectionReused"":true,""wasCached"":false,""fetchedViaServiceWorker"":false,""serviceWorkerResponseSource"":""unspecified"",""inPrefetchCache"":false,""networkAccessed"":true,""fromArchive"":false,""cookieInRequest"":true,""responseType"":""cors"",""encodedDataLength"":512,""expectedContentLength"":-1,""headerCount"":2,""headers"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null},{""name"":""x-api-key"",""value"":null,""valueRedacted"":true,""redactionReason"":""credential-name""}],""headersTruncated"":false,""timing"":{""requestStartBeforeRecordMilliseconds"":1520.25,""proxyStart"":0.125,""proxyEnd"":0.625,""domainLookupStart"":null,""domainLookupEnd"":1.625,""connectStart"":2.125,""connectEnd"":null,""sslStart"":3.125,""sslEnd"":3.625,""workerStart"":null,""workerReady"":4.625,""workerFetchStart"":5.125,""workerRespondWithSettled"":null,""workerRouterEvaluationStart"":6.125,""workerCacheLookupStart"":6.625,""sendStart"":null,""sendEnd"":7.625,""receiveHeadersStart"":8.125,""receiveHeadersEnd"":null,""receiveNonInformationalHeadersStart"":9.125,""receiveEarlyHintsStart"":9.625,""pushStart"":null,""pushEnd"":10.625,""responseEnd"":11.125}}}"),
        ("browser.network", "request-finished",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""17"",""encodedDataLength"":640,""decodedBodyLength"":2048,""finishBeforeRecordMilliseconds"":3.5}"),
        ("browser.network", "request-finished",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""dedicated-worker"",""workerToken"":""WORKER-1"",""globalObjectUrl"":""https://example.test/worker.js""},""inspectorId"":""18"",""encodedDataLength"":null,""decodedBodyLength"":0,""finishBeforeRecordMilliseconds"":null}"),
        ("browser.network", "request-failed",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""18"",""url"":""https://other.test/data"",""netError"":-3,""netErrorName"":""ERR_ABORTED"",""cancellation"":true,""timeout"":false,""accessCheck"":false,""blockedByResponse"":false,""blockedByOrb"":false,""hasCopyInCache"":false,""cancelledFromHttpError"":false,""internal"":false,""blockedReason"":null,""corsError"":null}"),
        ("browser.network", "request-failed",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""19"",""url"":""https://other.test/cors"",""netError"":-2,""netErrorName"":null,""cancellation"":false,""timeout"":true,""accessCheck"":true,""blockedByResponse"":true,""blockedByOrb"":true,""hasCopyInCache"":true,""cancelledFromHttpError"":true,""internal"":true,""blockedReason"":""coep-frame-resource-needs-coep-header"",""corsError"":{""error"":""missing-allow-origin-header"",""failedParameter"":null}}"),
        ("browser.network", "memory-cache-hit",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""staticData"":true,""request"":{""inspectorId"":""18"",""requestId"":null,""url"":""https://example.test/api"",""method"":""POST"",""resourceType"":""document"",""initiator"":{""type"":null,""url"":null,""line"":null,""column"":null,""linkPreload"":true},""internal"":false,""destination"":""empty"",""mode"":""cors"",""credentialsMode"":""same-origin"",""redirectMode"":""follow"",""cacheMode"":""default"",""priority"":""high"",""initialPriority"":""medium"",""fetchPriorityHint"":""auto"",""renderBlocking"":""non-blocking"",""referrer"":null,""referrerPolicy"":""strict-origin-when-cross-origin"",""keepalive"":false,""userGesture"":false,""adResource"":false,""formSubmission"":true,""headerCount"":0,""headers"":[],""headersTruncated"":false},""response"":{""url"":""https://example.test/api"",""responseUrl"":null,""status"":200,""statusText"":""OK"",""mimeType"":""application/json"",""charset"":""utf-8"",""alpnProtocol"":""h2"",""connectionInfo"":""h2"",""remoteAddress"":{""ip"":""203.0.113.5"",""port"":443},""connectionId"":12,""connectionReused"":true,""wasCached"":false,""fetchedViaServiceWorker"":false,""serviceWorkerResponseSource"":""unspecified"",""inPrefetchCache"":false,""networkAccessed"":true,""fromArchive"":false,""cookieInRequest"":true,""responseType"":""cors"",""encodedDataLength"":512,""expectedContentLength"":-1,""headerCount"":2,""headers"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null},{""name"":""x-api-key"",""value"":null,""valueRedacted"":true,""redactionReason"":""credential-name""}],""headersTruncated"":false,""timing"":{""requestStartBeforeRecordMilliseconds"":1520.25,""proxyStart"":0.125,""proxyEnd"":0.625,""domainLookupStart"":null,""domainLookupEnd"":1.625,""connectStart"":2.125,""connectEnd"":null,""sslStart"":3.125,""sslEnd"":3.625,""workerStart"":null,""workerReady"":4.625,""workerFetchStart"":5.125,""workerRespondWithSettled"":null,""workerRouterEvaluationStart"":6.125,""workerCacheLookupStart"":6.625,""sendStart"":null,""sendEnd"":7.625,""receiveHeadersStart"":8.125,""receiveHeadersEnd"":null,""receiveNonInformationalHeadersStart"":9.125,""receiveEarlyHintsStart"":9.625,""pushStart"":null,""pushEnd"":10.625,""responseEnd"":11.125}}}"),
        ("browser.network", "request-headers-sent",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""devtoolsAgentId"":null,""requestId"":""1000.5"",""headerCount"":2,""headers"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null},{""name"":""cookie"",""value"":null,""valueRedacted"":true,""redactionReason"":""credential-header""}],""headersTruncated"":false,""cookieCount"":2,""cookies"":[{""name"":""sid"",""parsed"":true,""domain"":""example.test"",""path"":""/"",""sameSite"":""lax"",""secure"":true,""httpOnly"":true,""hostOnly"":true,""partitioned"":false,""persistent"":false,""expired"":false,""included"":true,""exclusionReasons"":[],""warningReasons"":[""warn-same-site-unspecified-lax-allow-unsafe""],""exemptionReason"":null},{""name"":""bad"",""parsed"":false,""domain"":null,""path"":null,""sameSite"":null,""secure"":null,""httpOnly"":null,""hostOnly"":null,""partitioned"":null,""persistent"":null,""expired"":null,""included"":false,""exclusionReasons"":[""exclude-invalid"",""exclude-secure-only""],""warningReasons"":[],""exemptionReason"":""user-setting""}],""cookiesTruncated"":false,""sentBeforeRecordMilliseconds"":12.5}"),
        ("browser.network", "request-headers-sent",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""devtoolsAgentId"":""agent-1"",""requestId"":""1000.6"",""headerCount"":0,""headers"":[],""headersTruncated"":false,""cookieCount"":0,""cookies"":[],""cookiesTruncated"":false,""sentBeforeRecordMilliseconds"":null}"),
        ("browser.network", "response-headers-received",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""devtoolsAgentId"":""agent-1"",""requestId"":""1000.5"",""headerCount"":4,""headers"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null}],""headersTruncated"":true,""cookieCount"":3,""cookies"":[{""name"":""bad"",""parsed"":false,""domain"":null,""path"":null,""sameSite"":null,""secure"":null,""httpOnly"":null,""hostOnly"":null,""partitioned"":null,""persistent"":null,""expired"":null,""included"":false,""exclusionReasons"":[""exclude-invalid"",""exclude-secure-only""],""warningReasons"":[],""exemptionReason"":""user-setting""}],""cookiesTruncated"":true,""status"":200}"),
        ("browser.network", "navigation-response",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""navigationId"":""navigation-7"",""requestId"":""1000.5"",""url"":""https://example.test/"",""method"":""GET"",""committed"":true,""errorPage"":false,""sameDocument"":false,""download"":false,""backForwardCache"":false,""netError"":0,""netErrorName"":null,""redirectChain"":[""https://example.test/old"",""https://example.test/""],""requestHeaderCount"":2,""requestHeaders"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null},{""name"":""cookie"",""value"":null,""valueRedacted"":true,""redactionReason"":""credential-header""}],""requestHeadersTruncated"":false,""response"":{""status"":200,""statusText"":""OK"",""mimeType"":""text/html"",""wasCached"":false,""remoteAddress"":{""ip"":""2001:db8::1"",""port"":443},""connectionInfo"":""h3"",""headerCount"":5,""headers"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null},{""name"":""x-api-key"",""value"":null,""valueRedacted"":true,""redactionReason"":""credential-name""}],""headersTruncated"":true},""timing"":{""navigationStartBeforeRecordMilliseconds"":1520.25,""loaderStart"":0.125,""firstRequestStart"":0.625,""firstResponseStart"":null,""firstLoaderCallback"":1.625,""finalRequestStart"":2.125,""finalResponseStart"":null,""finalNonInformationalResponseStart"":3.125,""finalLoaderCallback"":3.625,""requestFailed"":null,""commitSent"":4.625,""commitReceived"":5.125,""commitReplySent"":null,""didCommit"":6.125,""finalRequestDomainLookupStart"":6.625,""finalRequestDomainLookupEnd"":null,""finalRequestConnectStart"":7.625,""finalRequestConnectEnd"":8.125,""finalRequestSslStart"":null}}"),
        ("browser.network", "navigation-response",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""navigationId"":""navigation-8"",""requestId"":null,""url"":""https://example.test/gone"",""method"":""GET"",""committed"":false,""errorPage"":true,""sameDocument"":false,""download"":false,""backForwardCache"":false,""netError"":-105,""netErrorName"":""ERR_NAME_NOT_RESOLVED"",""redirectChain"":[],""requestHeaderCount"":0,""requestHeaders"":[],""requestHeadersTruncated"":false,""response"":null,""timing"":null}"),
        ("browser.network", "websocket-created",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""location"":{""scriptId"":""script-2"",""url"":""https://example.test/app.js"",""line"":18,""column"":4,""functionName"":""activate"",""sourceHash"":""sha256:test""},""world"":null,""inspectorId"":""21"",""url"":""wss://example.test/socket"",""requestedProtocols"":""chat, superchat""}"),
        ("browser.network", "websocket-handshake-request",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""21"",""url"":""wss://example.test/socket"",""cookieNames"":[""sid""],""headerCount"":2,""headers"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null},{""name"":""cookie"",""value"":null,""valueRedacted"":true,""redactionReason"":""credential-header""}],""headersTruncated"":false}"),
        ("browser.network", "websocket-handshake-response",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""21"",""url"":""wss://example.test/socket"",""httpVersion"":""HTTP/1.1"",""status"":101,""statusText"":""Switching Protocols"",""remoteAddress"":{""ip"":""203.0.113.5"",""port"":443},""selectedProtocol"":""chat"",""setCookieNames"":[""sid""],""headerCount"":1,""headers"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null}],""headersTruncated"":false,""extensions"":""permessage-deflate""}"),
        ("browser.network", "websocket-handshake-response",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""dedicated-worker"",""workerToken"":""WORKER-1"",""globalObjectUrl"":""https://example.test/worker.js""},""inspectorId"":""22"",""url"":null,""httpVersion"":null,""status"":0,""statusText"":null,""remoteAddress"":null,""selectedProtocol"":null,""setCookieNames"":[],""headerCount"":1,""headers"":[],""headersTruncated"":true,""extensions"":null}"),
        ("browser.network", "websocket-message-sent",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":""world-1"",""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""location"":null,""world"":{""kind"":""isolated"",""blinkWorldId"":1,""name"":""extension"",""stableId"":null},""inspectorId"":""21"",""opcode"":""text"",""payloadLength"":20,""payload"":{""text"":""token=[withheld]&x=1"",""truncated"":false,""withheld"":[{""offset"":6,""reason"":""credential-value""}]}}"),
        ("browser.network", "websocket-message-received",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""21"",""opcode"":""binary"",""payloadLength"":65536,""payload"":null}"),
        ("browser.network", "websocket-message-received",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""21"",""opcode"":""text"",""payloadLength"":9000,""payload"":{""text"":""xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"",""truncated"":true,""withheld"":[]}}"),
        ("browser.network", "websocket-close-requested",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""location"":{""scriptId"":""script-2"",""url"":""https://example.test/app.js"",""line"":18,""column"":4,""functionName"":""activate"",""sourceHash"":""sha256:test""},""world"":null,""inspectorId"":""21"",""code"":null,""reason"":{""text"":"""",""truncated"":false,""withheld"":[]}}"),
        ("browser.network", "websocket-error",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""21"",""message"":""Connection closed before receiving a handshake response""}"),
        ("browser.network", "websocket-closed",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""21"",""cause"":""dropped"",""wasClean"":true,""code"":1000,""reason"":{""text"":""done"",""truncated"":false,""withheld"":[]}}"),
        ("browser.network", "websocket-closed",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""22"",""cause"":""disconnected"",""wasClean"":null,""code"":null,""reason"":null}"),
        ("browser.network", "event-source-message",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""inspectorId"":""23"",""url"":""https://example.test/events"",""eventType"":""update"",""lastEventId"":{""text"":""42"",""truncated"":false,""withheld"":[]},""dataLength"":20,""data"":{""text"":""token=[withheld]&x=1"",""truncated"":false,""withheld"":[{""offset"":6,""reason"":""credential-value""}]}}"),
        ("browser.network", "web-transport-created",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":""world-1"",""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""location"":{""scriptId"":""script-2"",""url"":""https://example.test/app.js"",""line"":18,""column"":4,""functionName"":""activate"",""sourceHash"":""sha256:test""},""world"":{""kind"":""isolated"",""blinkWorldId"":1,""name"":""extension"",""stableId"":null},""transportId"":""5"",""url"":""https://example.test:4433/wt""}"),
        ("browser.network", "web-transport-established",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""transportId"":""5"",""url"":""https://example.test:4433/wt"",""httpVersion"":""h3"",""status"":200,""statusText"":null,""remoteAddress"":{""ip"":""203.0.113.5"",""port"":4433},""selectedProtocol"":null,""setCookieNames"":[],""headerCount"":1,""headers"":[{""name"":""accept"",""value"":""text/html"",""valueRedacted"":false,""redactionReason"":null}],""headersTruncated"":false,""maxDatagramSize"":1200}"),
        ("browser.network", "web-transport-established",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""transportId"":""6"",""url"":null,""httpVersion"":null,""status"":0,""statusText"":null,""remoteAddress"":null,""selectedProtocol"":null,""setCookieNames"":[""sid""],""headerCount"":0,""headers"":[],""headersTruncated"":false,""maxDatagramSize"":null}"),
        ("browser.network", "web-transport-close-requested",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""location"":null,""world"":null,""transportId"":""5"",""code"":7,""reason"":{""text"":""bye"",""truncated"":false,""withheld"":[]}}"),
        ("browser.network", "web-transport-close-requested",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""location"":{""scriptId"":""script-2"",""url"":""https://example.test/app.js"",""line"":18,""column"":4,""functionName"":""activate"",""sourceHash"":""sha256:test""},""world"":null,""transportId"":""6"",""code"":null,""reason"":null}"),
        ("browser.network", "web-transport-closed",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""transportId"":""5"",""abrupt"":false,""code"":0,""reason"":{""text"":"""",""truncated"":false,""withheld"":[]}}"),
        ("browser.network", "web-transport-closed",
            @"{""context"":{""browserInstanceId"":""browser-1"",""processId"":4100,""processType"":""renderer"",""profileId"":null,""browserContextId"":""context-1"",""pageId"":""page-1"",""frameId"":""frame-1"",""documentId"":""document-1"",""executionWorldId"":null,""documentToken"":""TOKEN-1""},""scope"":{""contextKind"":""window"",""workerToken"":null,""globalObjectUrl"":""https://example.test/""},""transportId"":""6"",""abrupt"":true,""code"":null,""reason"":null}")
    ];

    /// <summary>
    /// Payloads the tables store in a normal form, with the payload read
    /// back: an optional member written as null reads back absent, a UTC
    /// time reads back without trailing fractional zeros, and a number held
    /// as a double reads back in its shortest form.
    /// </summary>
    public static IReadOnlyList<(string Channel, string EventType, string Payload, string Expected)> Normalized { get; } =
    [
        ("audio.system", "audio-stream-stopped",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""device"":""Speakers"",""endpointVolumeScalar"":null,""format"":" + Format + "," +
            @"""dataBytes"":384000,""buffersObserved"":200,""buffersDropped"":0,""peakAmplitude"":null,""rmsDbfs"":null}",
            @"{""stream"":""system"",""path"":""audio/system.wav"",""device"":""Speakers"",""format"":" + Format + "," +
            @"""dataBytes"":384000,""buffersObserved"":200,""buffersDropped"":0}"),
        ("collector.lifecycle", "collector-lifecycle",
            @"{""action"":""stop"",""state"":""stopped"",""utc"":""2026-09-25T12:00:01.5000000+00:00""}",
            @"{""action"":""stop"",""state"":""stopped"",""utc"":""2026-09-25T12:00:01.5+00:00""}"),
        ("browser.lifecycle", "collector-omission",
            @"{""reason"":""browser-queue-full"",""count"":1,""context"":null}",
            @"{""reason"":""browser-queue-full"",""count"":1}"),
        ("browser.scheduler", "wake-up-deferred",
            J("{'context':") + WorkerContext + "," + Scheduler +
            J("'deferralMilliseconds':544.0,'hasReadyTask':true,'blockType':'new-tasks-only','decisionBoundary':'task-queue-throttler'}"),
            J("{'context':") + WorkerContext + "," + Scheduler +
            J("'deferralMilliseconds':544,'hasReadyTask':true,'blockType':'new-tasks-only','decisionBoundary':'task-queue-throttler'}")),
        ("browser.dispatch", "dispatch-completed",
            J("{'context':") + Context + J(",'dispatchId':'dispatch-5','eventName':'click','trusted':true,'originalTarget':") + ButtonTarget + "," +
            OneStepPath + J(",'phase':'none','listenerId':null,'defaultPrevented':false,'propagationStopped':false,") +
            J("'immediatePropagationStopped':false,'defaultAction':null,'outcome':null,'currentTarget':null}"),
            J("{'context':") + Context + J(",'dispatchId':'dispatch-5','eventName':'click','trusted':true,'originalTarget':") + ButtonTarget + "," +
            OneStepPath + J(",'phase':'none','listenerId':null,'defaultPrevented':false,'propagationStopped':false,") +
            J("'immediatePropagationStopped':false,'defaultAction':null,'outcome':null}"))
    ];

    /// <summary>The first sample of the event type.</summary>
    public static string Sample(string channel, string eventType) =>
        All.First(item => item.Channel == channel && item.EventType == eventType).Payload;

    /// <summary>A UI Automation focus change to the named element.</summary>
    public static string Focus(string name, string controlType = "Button") =>
        Element
            .Replace(@"""name"":""Save""", @"""name"":" + JsonSerializer.Serialize(name), StringComparison.Ordinal)
            .Replace(@"""controlType"":""Button""", @"""controlType"":" + JsonSerializer.Serialize(controlType), StringComparison.Ordinal)
            is var element
            ? @"{""eventId"":""UIA_AutomationFocusChangedEventId"",""changeType"":null,""runtimeId"":[42,7],""newValue"":null,""element"":" +
                element + "}"
            : throw new InvalidOperationException();

    /// <summary>A desktop frame image at the path.</summary>
    public static string DesktopFrame(string path) =>
        @"{""path"":" + JsonSerializer.Serialize(path) + @",""x"":0,""y"":0,""width"":1920,""height"":1080,""stride"":7680," +
        @"""pixelFormat"":""bgra8"",""encodedFormat"":""png"",""byteLength"":3,""captureDurationNanoseconds"":1," +
        @"""framesPerSecond"":5,""backend"":""windows-graphics-capture"",""monitorCount"":1,""fallbackReason"":null," +
        @"""gdiFallbackFrameCount"":0}";

    /// <summary>The start of an audio stream written to the path.</summary>
    public static string AudioStarted(string stream, string path) =>
        @"{""stream"":" + JsonSerializer.Serialize(stream) + @",""path"":" + JsonSerializer.Serialize(path) +
        @",""device"":""Test device"",""format"":" + Format + @",""dataBytes"":0,""buffersObserved"":0,""buffersDropped"":0}";
}
