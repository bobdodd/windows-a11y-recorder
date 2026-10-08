using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Recorder.Contracts;
using Windows.Foundation.Metadata;
using Windows.UI.ViewManagement;

namespace Recorder.Collectors.Windowing;

/// <summary>
/// Records the Windows accessibility-related settings at the start and stop
/// of a recording and each change during it, on <c>system.preferences</c>.
/// Every reading is made on one thread, which owns a hidden top-level window:
/// <c>WM_SETTINGCHANGE</c> and <c>WM_DISPLAYCHANGE</c> are sent to top-level
/// windows, and a message-only window "does not receive broadcast
/// messages". The thread is per-monitor DPI aware, so GetDpiForMonitor
/// gives each monitor's actual DPI. Registry watches and UISettings events
/// post to the window, so their readings are made on the same thread. A
/// notice is not itself a change: only a reading that differs from the last
/// recorded one is written. See docs/architecture/accessibility-preferences.md.
/// </summary>
public sealed class WindowsPreferencesCollector : ICaptureCollector
{
    private const uint WmQuit = 0x0012;
    private const uint WmSettingChange = 0x001A;
    private const uint WmDisplayChange = 0x007E;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmApp = 0x8000;
    private const uint WmRegistryChanged = WmApp + 1;
    private const uint WmUiSettingsChanged = WmApp + 2;
    private const uint WmFinalReading = WmApp + 3;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ColorFilteringKey = @"Software\Microsoft\ColorFiltering";
    private static readonly string[] WatchedKeys = [PersonalizeKey, ColorFilteringKey];

    private readonly object _gate = new();
    private Thread? _windowThread;
    private Thread? _registryThread;
    private TaskCompletionSource<bool>? _ready;
    private TaskCompletionSource<bool>? _finalRead;
    private CollectorInitializationContext? _context;
    private WndProc? _windowProc;
    private nint _window;
    private UISettings? _uiSettings;
    private readonly Dictionary<string, bool> _uiSettingsEvents = new(StringComparer.Ordinal);
    private Dictionary<string, WindowsPreferenceReading> _last = new(StringComparer.Ordinal);
    private WindowsPreferenceReading? _caretBlinkTime;
    private readonly ManualResetEvent _stopRegistry = new(false);
    private readonly AutoResetEvent _rearmRegistry = new(false);
    private long _eventSequence = -1;
    private long _lifecycleSequence = -1;
    private bool _disposed;

    public WindowsPreferencesCollector()
    {
        Descriptor = CollectorDescriptor.Create(
            "windows.preferences",
            nameof(WindowsPreferencesCollector),
            typeof(WindowsPreferencesCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [WindowsPreferenceSettings.Channel],
            "win32.setting-change+ui-settings+registry-notify");
    }

    public CollectorDescriptor Descriptor { get; }
    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;
    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public ValueTask<CapabilityResult> InitializeAsync(
        CollectorInitializationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Created)
            {
                return ValueTask.FromResult(new CapabilityResult(
                    CapabilityStatus.Incompatible,
                    Descriptor.Channels,
                    ["collector-already-initialized"],
                    false,
                    false));
            }

            _context = context;
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                LifecycleState = CollectorLifecycleState.Failed;
                HealthState = CollectorHealthState.Failed;
                return ValueTask.FromResult(new CapabilityResult(
                    CapabilityStatus.Incompatible,
                    Descriptor.Channels,
                    ["requires-windows-10-2004-or-later"],
                    false,
                    false));
            }

            LifecycleState = CollectorLifecycleState.Ready;
            HealthState = CollectorHealthState.Healthy;
            return ValueTask.FromResult(CapabilityResult.Supported(Descriptor.Channels.ToArray()));
        }
    }

    public async ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready)
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Starting;
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _windowThread = new Thread(WindowLoop)
            {
                IsBackground = true,
                Name = "Windows preferences"
            };
            _windowThread.SetApartmentState(ApartmentState.MTA);
            _windowThread.Start();
        }

        try
        {
            await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
            return CollectorTransitionResult.Reject(
                LifecycleState,
                "windows-preferences-start-failed",
                ex.Message);
        }

        _registryThread = new Thread(RegistryLoop)
        {
            IsBackground = true,
            Name = "Windows preferences registry"
        };
        _registryThread.Start();
        LifecycleState = CollectorLifecycleState.Running;
        EmitLifecycle("started", boundary);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? finalRead = null;
        lock (_gate)
        {
            if (LifecycleState is CollectorLifecycleState.Stopped or CollectorLifecycleState.Disposed)
            {
                return CollectorTransitionResult.Success(LifecycleState);
            }

            if (LifecycleState is not (CollectorLifecycleState.Running or CollectorLifecycleState.Failed))
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot stop from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Stopping;
            if (_window != nint.Zero)
            {
                finalRead = _finalRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                PostMessageW(_window, WmFinalReading, nuint.Zero, nint.Zero);
            }
        }

        _stopRegistry.Set();
        if (finalRead is not null)
        {
            await finalRead.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }

        foreach (var thread in new[] { _windowThread, _registryThread })
        {
            if (thread is not null)
            {
                await Task.Run(() => thread.Join(TimeSpan.FromSeconds(5)), cancellationToken).ConfigureAwait(false);
            }
        }

        LifecycleState = CollectorLifecycleState.Stopped;
        EmitLifecycle("stopped", boundary);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (LifecycleState is CollectorLifecycleState.Running or CollectorLifecycleState.Failed)
        {
            var now = _context?.Clock.GetElapsedNanoseconds() ?? 0;
            await StopAsync(new SessionBoundary(now, DateTimeOffset.UtcNow), CancellationToken.None)
                .ConfigureAwait(false);
        }

        _disposed = true;
        _stopRegistry.Dispose();
        _rearmRegistry.Dispose();
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    // --- The window thread ---------------------------------------------------

    private void WindowLoop()
    {
        var className = "WindowsA11yRecorderPreferences" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var instance = GetModuleHandleW(null);
        var registered = false;
        try
        {
            // Per-monitor aware, so GetDpiForMonitor gives each monitor's
            // actual DPI.
            SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
            _windowProc = OnWindowMessage;
            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WindowProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
                Instance = instance,
                ClassName = className
            };
            if (RegisterClassExW(ref windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed.");
            }

            registered = true;
            // A hidden top-level window, not a message-only one, so that
            // broadcast messages reach it. WS_EX_TOOLWINDOW keeps it off
            // the taskbar; it is never shown.
            _window = CreateWindowExW(
                WsExToolWindow, className, "Windows A11y Recorder preferences", WsPopup,
                0, 0, 0, 0, nint.Zero, nint.Zero, instance, nint.Zero);
            if (_window == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed.");
            }

            SubscribeUiSettings();
            _caretBlinkTime = ReadCaretBlinkTime();
            _last = ReadAll();
            Emit(
                WindowsPreferenceSettings.SnapshotEventType,
                WindowsPreferencePayloads.Snapshot("start", WithCaret(_last), _uiSettingsEvents),
                Now());
            _ready!.TrySetResult(true);

            while (GetMessageW(out var message, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception ex)
        {
            _ready?.TrySetException(ex);
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
        }
        finally
        {
            UnsubscribeUiSettings();
            if (_window != nint.Zero)
            {
                DestroyWindow(_window);
                _window = nint.Zero;
            }

            if (registered)
            {
                UnregisterClassW(className, instance);
            }

            _finalRead?.TrySetResult(true);
        }
    }

    private nint OnWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case WmSettingChange:
                    _rearmRegistry.Set();
                    Reread(new WindowsPreferenceNotice(
                        "setting-change",
                        (long)wParam,
                        lParam == nint.Zero ? null : Marshal.PtrToStringUni(lParam)));
                    break;
                case WmDisplayChange:
                    Reread(new WindowsPreferenceNotice("display-change"));
                    break;
                case WmDpiChanged:
                    Reread(new WindowsPreferenceNotice("dpi-changed"));
                    return 0;
                case WmRegistryChanged:
                    Reread(new WindowsPreferenceNotice("registry", Source: @"HKCU\" + WatchedKeys[(int)wParam]));
                    return 0;
                case WmUiSettingsChanged:
                    Reread(new WindowsPreferenceNotice("ui-settings", Source: WindowsPreferenceSettings.UiSettingsEvents[(int)wParam]));
                    return 0;
                case WmFinalReading:
                    var final = ReadAll();
                    EmitChanges(final, new WindowsPreferenceNotice("stop"));
                    Emit(
                        WindowsPreferenceSettings.SnapshotEventType,
                        WindowsPreferencePayloads.Snapshot("stop", WithCaret(final), _uiSettingsEvents),
                        Now());
                    _finalRead?.TrySetResult(true);
                    PostQuitMessage(0);
                    return 0;
            }
        }
        catch (Exception)
        {
            HealthState = CollectorHealthState.Degraded;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void Reread(WindowsPreferenceNotice notice) => EmitChanges(ReadAll(), notice);

    private void EmitChanges(Dictionary<string, WindowsPreferenceReading> current, WindowsPreferenceNotice notice)
    {
        var time = Now();
        foreach (var name in WindowsPreferencePayloads.Changed(_last, current))
        {
            Emit(
                WindowsPreferenceSettings.ChangeEventType,
                WindowsPreferencePayloads.Change(name, _last[name], current[name], notice),
                time);
        }

        _last = current;
    }

    private Dictionary<string, WindowsPreferenceReading> WithCaret(Dictionary<string, WindowsPreferenceReading> readings) =>
        new(readings, StringComparer.Ordinal)
        {
            [WindowsPreferenceSettings.CaretBlinkTime] = _caretBlinkTime ?? WindowsPreferenceReading.Failed("not read")
        };

    // --- UISettings ------------------------------------------------------------

    private void SubscribeUiSettings()
    {
        foreach (var name in WindowsPreferenceSettings.UiSettingsEvents)
        {
            _uiSettingsEvents[name] = false;
        }

        try
        {
            _uiSettings = new UISettings();
        }
        catch (Exception)
        {
            return;
        }

        const string type = "Windows.UI.ViewManagement.UISettings";
        if (ApiInformation.IsEventPresent(type, "AdvancedEffectsEnabledChanged"))
        {
            _uiSettings.AdvancedEffectsEnabledChanged += OnAdvancedEffectsEnabledChanged;
            _uiSettingsEvents["advancedEffectsEnabledChanged"] = true;
        }

        if (ApiInformation.IsEventPresent(type, "AnimationsEnabledChanged"))
        {
            _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
            _uiSettingsEvents["animationsEnabledChanged"] = true;
        }

        if (ApiInformation.IsEventPresent(type, "AutoHideScrollBarsChanged"))
        {
            _uiSettings.AutoHideScrollBarsChanged += OnAutoHideScrollBarsChanged;
            _uiSettingsEvents["autoHideScrollBarsChanged"] = true;
        }

        if (ApiInformation.IsEventPresent(type, "TextScaleFactorChanged"))
        {
            _uiSettings.TextScaleFactorChanged += OnTextScaleFactorChanged;
            _uiSettingsEvents["textScaleFactorChanged"] = true;
        }

        if (ApiInformation.IsEventPresent(type, "ColorValuesChanged"))
        {
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
            _uiSettingsEvents["colorValuesChanged"] = true;
        }
    }

    private void UnsubscribeUiSettings()
    {
        if (_uiSettings is not { } settings)
        {
            return;
        }

        try
        {
            if (_uiSettingsEvents["advancedEffectsEnabledChanged"]) settings.AdvancedEffectsEnabledChanged -= OnAdvancedEffectsEnabledChanged;
            if (_uiSettingsEvents["animationsEnabledChanged"]) settings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
            if (_uiSettingsEvents["autoHideScrollBarsChanged"]) settings.AutoHideScrollBarsChanged -= OnAutoHideScrollBarsChanged;
            if (_uiSettingsEvents["textScaleFactorChanged"]) settings.TextScaleFactorChanged -= OnTextScaleFactorChanged;
            if (_uiSettingsEvents["colorValuesChanged"]) settings.ColorValuesChanged -= OnColorValuesChanged;
        }
        catch (Exception)
        {
            // The settings object is released with the collector.
        }

        _uiSettings = null;
    }

    // UISettings raises its events on other threads; each is posted to the
    // window so the reading is made on its thread.
    private void OnAdvancedEffectsEnabledChanged(UISettings sender, object args) => PostUiSettings(0);
    private void OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) => PostUiSettings(1);
    private void OnAutoHideScrollBarsChanged(UISettings sender, UISettingsAutoHideScrollBarsChangedEventArgs args) => PostUiSettings(2);
    private void OnTextScaleFactorChanged(UISettings sender, object args) => PostUiSettings(3);
    private void OnColorValuesChanged(UISettings sender, object args) => PostUiSettings(4);

    private void PostUiSettings(int index)
    {
        var window = _window;
        if (window != nint.Zero)
        {
            PostMessageW(window, WmUiSettingsChanged, (nuint)index, nint.Zero);
        }
    }

    // --- The registry watches -------------------------------------------------

    // Each watched key is notified once per RegNotifyChangeKeyValue call and
    // then watched again, as Chromium does. While a key does not exist, as
    // ColorFiltering before a filter is first set, its parent is watched
    // for a subkey added (REG_NOTIFY_CHANGE_NAME), and when the key appears
    // it is read as a registry change and watched; the run of 2026-10-08
    // found that turning a color filter on creates the key with no notice
    // the collector receives. The key is also opened again after each
    // WM_SETTINGCHANGE.
    private void RegistryLoop()
    {
        var keys = new RegistryKey?[WatchedKeys.Length];
        var signals = new AutoResetEvent[WatchedKeys.Length];
        var parents = new RegistryKey?[WatchedKeys.Length];
        var parentSignals = new AutoResetEvent[WatchedKeys.Length];
        try
        {
            for (var i = 0; i < signals.Length; i++)
            {
                signals[i] = new AutoResetEvent(false);
                parentSignals[i] = new AutoResetEvent(false);
            }

            while (true)
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    if (keys[i] is null && OpenAndWatch(i, keys, signals))
                    {
                        parents[i]?.Dispose();
                        parents[i] = null;
                    }

                    if (keys[i] is null && parents[i] is null)
                    {
                        parents[i] = Registry.CurrentUser.OpenSubKey(ParentOf(WatchedKeys[i]));
                        if (parents[i] is not null)
                        {
                            Watch(parents[i]!, parentSignals[i], RegNotifyChangeName);
                        }
                    }
                }

                WaitHandle[] handles = [_stopRegistry, _rearmRegistry, .. signals, .. parentSignals];
                var signalled = WaitHandle.WaitAny(handles);
                if (signalled == 0)
                {
                    return;
                }

                if (signalled >= 2 && signalled < 2 + keys.Length)
                {
                    // A key deleted while watched cannot be watched again;
                    // it is closed, and its parent is watched until it is
                    // created again.
                    var index = signalled - 2;
                    if (!TryWatch(keys[index]!, signals[index], RegNotifyChangeLastSet))
                    {
                        keys[index]!.Dispose();
                        keys[index] = null;
                    }

                    PostRegistryChanged(index);
                }
                else if (signalled >= 2 + keys.Length)
                {
                    // A subkey of the parent was added or removed: the key
                    // is read if it is now there; otherwise the parent is
                    // watched again at the top of the loop.
                    var index = signalled - 2 - keys.Length;
                    parents[index]?.Dispose();
                    parents[index] = null;
                    if (keys[index] is null && OpenAndWatch(index, keys, signals))
                    {
                        PostRegistryChanged(index);
                    }
                }
            }
        }
        catch (Exception)
        {
            HealthState = CollectorHealthState.Degraded;
        }
        finally
        {
            foreach (var key in keys.Concat(parents))
            {
                key?.Dispose();
            }

            foreach (var signal in signals.Concat(parentSignals))
            {
                signal?.Dispose();
            }
        }
    }

    private static bool OpenAndWatch(int index, RegistryKey?[] keys, AutoResetEvent[] signals)
    {
        keys[index] = Registry.CurrentUser.OpenSubKey(WatchedKeys[index]);
        if (keys[index] is null)
        {
            return false;
        }

        if (TryWatch(keys[index]!, signals[index], RegNotifyChangeLastSet))
        {
            return true;
        }

        keys[index]!.Dispose();
        keys[index] = null;
        return false;
    }

    private static bool TryWatch(RegistryKey key, AutoResetEvent signal, int filter) =>
        RegNotifyChangeKeyValue(key.Handle, false, filter, signal.SafeWaitHandle, true) == 0;

    private void PostRegistryChanged(int index)
    {
        var window = _window;
        if (window != nint.Zero)
        {
            PostMessageW(window, WmRegistryChanged, (nuint)index, nint.Zero);
        }
    }

    /// <summary>The key a watched key is a subkey of, under HKCU.</summary>
    internal static string ParentOf(string key) => key[..key.LastIndexOf('\\')];

    private static void Watch(RegistryKey key, AutoResetEvent signal, int filter)
    {
        var result = RegNotifyChangeKeyValue(
            key.Handle,
            false,
            filter,
            signal.SafeWaitHandle,
            true);
        if (result != 0)
        {
            throw new Win32Exception(result, "RegNotifyChangeKeyValue failed.");
        }
    }

    // --- Readings ---------------------------------------------------------------

    private Dictionary<string, WindowsPreferenceReading> ReadAll()
    {
        var readings = new Dictionary<string, WindowsPreferenceReading>(StringComparer.Ordinal)
        {
            ["monitors"] = ReadMonitors(),
            ["textScaleFactor"] = FromUiSettings(settings => settings.TextScaleFactor),
            ["appsUseLightTheme"] = ReadRegistryFlag(PersonalizeKey, "AppsUseLightTheme"),
            ["transparencyEffects"] = FromUiSettings(settings => settings.AdvancedEffectsEnabled),
            ["colorFilterActive"] = ReadRegistryFlag(ColorFilteringKey, "Active"),
            ["colorFilterType"] = ReadRegistryInteger(ColorFilteringKey, "FilterType"),
            ["accentColor"] = FromUiSettings(settings =>
            {
                var color = settings.GetColorValue(UIColorType.Accent);
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }),
            ["animationsEnabled"] = FromUiSettings(settings => settings.AnimationsEnabled),
            ["uiEffects"] = ReadSystemFlag(SpiGetUiEffects, "SPI_GETUIEFFECTS"),
            ["menuAnimation"] = ReadSystemFlag(SpiGetMenuAnimation, "SPI_GETMENUANIMATION"),
            ["menuFade"] = ReadSystemFlag(SpiGetMenuFade, "SPI_GETMENUFADE"),
            ["comboBoxAnimation"] = ReadSystemFlag(SpiGetComboBoxAnimation, "SPI_GETCOMBOBOXANIMATION"),
            ["cursorWidth"] = FromUiSettings(settings => settings.CursorSize.Width),
            ["cursorHeight"] = FromUiSettings(settings => settings.CursorSize.Height),
            ["caretWidth"] = ReadSystemInteger(SpiGetCaretWidth, "SPI_GETCARETWIDTH"),
            ["focusBorderWidth"] = ReadSystemInteger(SpiGetFocusBorderWidth, "SPI_GETFOCUSBORDERWIDTH"),
            ["focusBorderHeight"] = ReadSystemInteger(SpiGetFocusBorderHeight, "SPI_GETFOCUSBORDERHEIGHT"),
            ["keyboardCues"] = ReadSystemFlag(SpiGetKeyboardCues, "SPI_GETKEYBOARDCUES"),
            ["autoHideScrollBars"] = FromUiSettings(settings => settings.AutoHideScrollBars),
            ["messageDuration"] = FromUiSettings(settings => (long)settings.MessageDuration),
            ["stickyKeys"] = ReadAccessFlag(SpiGetStickyKeys, 8, "SPI_GETSTICKYKEYS"),
            ["filterKeys"] = ReadAccessFlag(SpiGetFilterKeys, 24, "SPI_GETFILTERKEYS"),
            ["toggleKeys"] = ReadAccessFlag(SpiGetToggleKeys, 8, "SPI_GETTOGGLEKEYS"),
            ["mouseKeys"] = ReadAccessFlag(SpiGetMouseKeys, 28, "SPI_GETMOUSEKEYS")
        };
        var (highContrast, scheme) = ReadHighContrast();
        readings["highContrast"] = highContrast;
        readings["highContrastScheme"] = scheme;
        return readings;
    }

    private WindowsPreferenceReading FromUiSettings<T>(Func<UISettings, T> read)
    {
        if (_uiSettings is not { } settings)
        {
            return WindowsPreferenceReading.Failed("UISettings could not be created.");
        }

        try
        {
            return WindowsPreferenceReading.Read(JsonValue.Create(read(settings)));
        }
        catch (Exception ex)
        {
            return WindowsPreferenceReading.Failed($"UISettings: {ex.Message}");
        }
    }

    private static WindowsPreferenceReading ReadRegistryValue(string key, string name, Func<int, JsonNode> convert)
    {
        try
        {
            using var opened = Registry.CurrentUser.OpenSubKey(key);
            if (opened is null)
            {
                return WindowsPreferenceReading.Failed($@"The key HKCU\{key} does not exist.");
            }

            return opened.GetValue(name) switch
            {
                int value => WindowsPreferenceReading.Read(convert(value)),
                null => WindowsPreferenceReading.Failed($"The value {name} does not exist."),
                var other => WindowsPreferenceReading.Failed($"The value {name} is a {other.GetType().Name}, not a DWORD.")
            };
        }
        catch (Exception ex)
        {
            return WindowsPreferenceReading.Failed(ex.Message);
        }
    }

    private static WindowsPreferenceReading ReadRegistryFlag(string key, string name) =>
        ReadRegistryValue(key, name, value => JsonValue.Create(value != 0));

    private static WindowsPreferenceReading ReadRegistryInteger(string key, string name) =>
        ReadRegistryValue(key, name, value => JsonValue.Create((long)value));

    private static WindowsPreferenceReading ReadSystemFlag(uint action, string name)
    {
        var value = 0;
        return SystemParametersInfoW(action, 0, ref value, 0)
            ? WindowsPreferenceReading.Read(JsonValue.Create(value != 0))
            : Failed(name);
    }

    private static WindowsPreferenceReading ReadSystemInteger(uint action, string name)
    {
        var value = 0;
        return SystemParametersInfoW(action, 0, ref value, 0)
            ? WindowsPreferenceReading.Read(JsonValue.Create((long)(uint)value))
            : Failed(name);
    }

    // STICKYKEYS, FILTERKEYS, TOGGLEKEYS, and MOUSEKEYS start with cbSize
    // and dwFlags; bit 0 of dwFlags is the feature's on flag in each
    // (SKF_STICKYKEYSON, FKF_FILTERKEYSON, TKF_TOGGLEKEYSON, MKF_MOUSEKEYSON).
    private static WindowsPreferenceReading ReadAccessFlag(uint action, int size, string name)
    {
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            for (var offset = 0; offset < size; offset += 4)
            {
                Marshal.WriteInt32(buffer, offset, 0);
            }

            Marshal.WriteInt32(buffer, 0, size);
            return SystemParametersInfoW(action, (uint)size, buffer, 0)
                ? WindowsPreferenceReading.Read(JsonValue.Create((Marshal.ReadInt32(buffer, 4) & 1) != 0))
                : Failed(name);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (WindowsPreferenceReading On, WindowsPreferenceReading Scheme) ReadHighContrast()
    {
        var contrast = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        if (!SystemParametersInfoW(SpiGetHighContrast, contrast.Size, ref contrast, 0))
        {
            var failed = Failed("SPI_GETHIGHCONTRAST");
            return (failed, failed);
        }

        var scheme = contrast.DefaultScheme == nint.Zero ? null : Marshal.PtrToStringUni(contrast.DefaultScheme);
        return (
            WindowsPreferenceReading.Read(JsonValue.Create((contrast.Flags & HcfHighContrastOn) != 0)),
            WindowsPreferenceReading.Read(scheme is null ? null : JsonValue.Create(scheme)));
    }

    // The caret blink time is read at the start only (Decisions, 3).
    // INFINITE, 4294967295, means the caret does not blink; zero means the
    // call failed.
    private static WindowsPreferenceReading ReadCaretBlinkTime()
    {
        var time = GetCaretBlinkTime();
        return time == 0
            ? Failed("GetCaretBlinkTime")
            : WindowsPreferenceReading.Read(JsonValue.Create((long)time));
    }

    private static WindowsPreferenceReading ReadMonitors()
    {
        var monitors = new JsonArray();
        string? problem = null;
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfoW(monitor, ref info))
            {
                problem = "GetMonitorInfo failed.";
                return true;
            }

            var result = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out var dpiY);
            monitors.Add(new JsonObject
            {
                ["deviceName"] = info.DeviceName,
                ["bounds"] = new JsonObject
                {
                    ["x"] = info.Monitor.Left,
                    ["y"] = info.Monitor.Top,
                    ["width"] = info.Monitor.Right - info.Monitor.Left,
                    ["height"] = info.Monitor.Bottom - info.Monitor.Top
                },
                ["isPrimary"] = (info.Flags & MonitorInfoPrimary) != 0,
                ["dpiX"] = result == 0 ? (long)dpiX : null,
                ["dpiY"] = result == 0 ? (long)dpiY : null
            });
            if (result != 0)
            {
                problem = $"GetDpiForMonitor failed with 0x{result:X8}.";
            }

            return true;
        };
        if (!EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero))
        {
            return Failed("EnumDisplayMonitors");
        }

        GC.KeepAlive(callback);
        return problem is null
            ? WindowsPreferenceReading.Read(monitors)
            : WindowsPreferenceReading.Failed(problem);
    }

    private static WindowsPreferenceReading Failed(string call) =>
        WindowsPreferenceReading.Failed($"{call} failed with error {Marshal.GetLastWin32Error()}.");

    // --- Events ---------------------------------------------------------------

    private long Now() => _context?.Clock.GetElapsedNanoseconds() ?? 0;

    private void Emit(string eventType, JsonObject payload, long monotonicNanoseconds)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var sequence = unchecked((ulong)Interlocked.Increment(ref _eventSequence));
        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            WindowsPreferenceSettings.Channel,
            sequence,
            monotonicNanoseconds,
            eventType,
            payload));
    }

    private void EmitLifecycle(string action, SessionBoundary boundary)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var sequence = unchecked((ulong)Interlocked.Increment(ref _lifecycleSequence));
        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "collector.lifecycle",
            sequence,
            boundary.MonotonicNanoseconds,
            "collector-lifecycle",
            new { action, state = LifecycleState.ToString(), boundary.Utc }));
    }

    // --- Native ---------------------------------------------------------------

    private const uint SpiGetFilterKeys = 0x0032;
    private const uint SpiGetToggleKeys = 0x0034;
    private const uint SpiGetMouseKeys = 0x0036;
    private const uint SpiGetStickyKeys = 0x003A;
    private const uint SpiGetHighContrast = 0x0042;
    private const uint SpiGetMenuAnimation = 0x1002;
    private const uint SpiGetComboBoxAnimation = 0x1004;
    private const uint SpiGetKeyboardCues = 0x100A;
    private const uint SpiGetMenuFade = 0x1012;
    private const uint SpiGetUiEffects = 0x103E;
    private const uint SpiGetCaretWidth = 0x2006;
    private const uint SpiGetFocusBorderWidth = 0x200E;
    private const uint SpiGetFocusBorderHeight = 0x2010;
    private const uint HcfHighContrastOn = 0x00000001;
    private const uint MonitorInfoPrimary = 0x00000001;
    private const int MdtEffectiveDpi = 0;
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolWindow = 0x00000080;
    private const int RegNotifyChangeName = 0x00000001;
    private const int RegNotifyChangeLastSet = 0x00000004;
    private static readonly nint DpiAwarenessContextPerMonitorAwareV2 = -4;

    private delegate nint WndProc(nint window, uint message, nuint wParam, nint lParam);

    private delegate bool MonitorEnumProc(nint monitor, nint deviceContext, nint rect, nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Value;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Message message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref Message message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, ref int value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, nint value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, ref HighContrast value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetCaretBlinkTime();

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint deviceContext, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(
        Microsoft.Win32.SafeHandles.SafeRegistryHandle key,
        bool watchSubtree,
        int notifyFilter,
        Microsoft.Win32.SafeHandles.SafeWaitHandle eventHandle,
        bool asynchronous);
}
