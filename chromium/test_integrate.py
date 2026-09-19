import importlib.util
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("integrate.py")
SPEC = importlib.util.spec_from_file_location("chromium_integrate", MODULE_PATH)
assert SPEC and SPEC.loader
INTEGRATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(INTEGRATE)


class IntegrateTests(unittest.TestCase):
    def test_upgrades_legacy_scheduling_hooks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cases = (
                (
                    root / "dom_timer.cc",
                    INTEGRATE.patch_blink_dom_timer,
                    (
                        (
                            INTEGRATE.BLINK_TIMER_SCHEDULED_HOOK,
                            INTEGRATE.LEGACY_BLINK_TIMER_SCHEDULED_HOOK,
                        ),
                        (
                            INTEGRATE.BLINK_TIMER_CANCELLED_HOOK,
                            INTEGRATE.LEGACY_BLINK_TIMER_CANCELLED_HOOK,
                        ),
                        (
                            INTEGRATE.BLINK_TIMER_FIRED_HOOK,
                            INTEGRATE.LEGACY_BLINK_TIMER_FIRED_HOOK,
                        ),
                    ),
                ),
                (
                    root / "frame_request_callback_collection.cc",
                    INTEGRATE.patch_blink_animation_frame_callbacks,
                    (
                        (
                            INTEGRATE.BLINK_ANIMATION_FRAME_SCHEDULED_HOOK,
                            INTEGRATE.LEGACY_BLINK_ANIMATION_FRAME_SCHEDULED_HOOK,
                        ),
                        (
                            INTEGRATE.BLINK_ANIMATION_FRAME_CANCELLED_INDEX_HOOK,
                            INTEGRATE.LEGACY_BLINK_ANIMATION_FRAME_CANCELLED_INDEX_HOOK,
                        ),
                        (
                            INTEGRATE.BLINK_ANIMATION_FRAME_CANCELLED_CALLBACK_HOOK,
                            INTEGRATE.LEGACY_BLINK_ANIMATION_FRAME_CANCELLED_CALLBACK_HOOK,
                        ),
                        (
                            INTEGRATE.BLINK_ANIMATION_FRAME_FIRED_HOOK,
                            INTEGRATE.LEGACY_BLINK_ANIMATION_FRAME_FIRED_HOOK,
                        ),
                    ),
                ),
                (
                    root / "scripted_idle_task_controller.cc",
                    INTEGRATE.patch_blink_idle_callbacks,
                    (
                        (
                            INTEGRATE.BLINK_IDLE_CALLBACK_SCHEDULED_HOOK,
                            INTEGRATE.LEGACY_BLINK_IDLE_CALLBACK_SCHEDULED_HOOK,
                        ),
                        (
                            INTEGRATE.BLINK_IDLE_CALLBACK_CANCELLED_HOOK,
                            INTEGRATE.LEGACY_BLINK_IDLE_CALLBACK_CANCELLED_HOOK,
                        ),
                        (
                            INTEGRATE.BLINK_IDLE_CALLBACK_FIRED_HOOK,
                            INTEGRATE.LEGACY_BLINK_IDLE_CALLBACK_FIRED_HOOK,
                        ),
                    ),
                ),
            )

            fixtures = self._current_scheduling_fixtures(root)
            for path, patcher, replacements in cases:
                current = fixtures[path]
                legacy = current.replace(f"{INTEGRATE.BLINK_PAGE_INCLUDE}\n", "")
                for current_hook, legacy_hook in replacements:
                    legacy = legacy.replace(current_hook, legacy_hook, 1)
                path.write_text(legacy, encoding="utf-8")

                patcher(path)
                upgraded = path.read_text(encoding="utf-8")
                self.assertEqual(current, upgraded)
                patcher(path)
                self.assertEqual(upgraded, path.read_text(encoding="utf-8"))

    @staticmethod
    def _current_scheduling_fixtures(root):
        fixtures = {}
        for name, hooks in (
            (
                "dom_timer.cc",
                (
                    INTEGRATE.BLINK_TIMER_SCHEDULED_HOOK,
                    INTEGRATE.BLINK_TIMER_CANCELLED_HOOK,
                    INTEGRATE.BLINK_TIMER_FIRED_HOOK,
                ),
            ),
            (
                "frame_request_callback_collection.cc",
                (
                    INTEGRATE.BLINK_ANIMATION_FRAME_SCHEDULED_HOOK,
                    INTEGRATE.BLINK_ANIMATION_FRAME_CANCELLED_INDEX_HOOK,
                    INTEGRATE.BLINK_ANIMATION_FRAME_CANCELLED_CALLBACK_HOOK,
                    INTEGRATE.BLINK_ANIMATION_FRAME_FIRED_HOOK,
                ),
            ),
            (
                "scripted_idle_task_controller.cc",
                (
                    INTEGRATE.BLINK_IDLE_CALLBACK_SCHEDULED_HOOK,
                    INTEGRATE.BLINK_IDLE_CALLBACK_CANCELLED_HOOK,
                    INTEGRATE.BLINK_IDLE_CALLBACK_FIRED_HOOK,
                ),
            ),
        ):
            path = root / name
            fixtures[path] = (
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                f"{INTEGRATE.BLINK_DOCUMENT_INCLUDE}\n"
                '#include "third_party/blink/renderer/core/page/page.h"\n'
                + (
                    f"{INTEGRATE.BLINK_TIMER_REQUESTED_DELAY_HOOK}\n"
                    if name == "dom_timer.cc"
                    else ""
                )
                + "\n".join(hooks)
            )
        return fixtures

    def test_patches_current_idle_callback_shape_idempotently(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "scripted_idle_task_controller.cc"
            path.write_text(
                '#include "third_party/blink/renderer/core/scheduler/'
                'scripted_idle_task_controller.h"\n'
                "\n"
                "ScriptedIdleTaskController::CallbackId\n"
                "ScriptedIdleTaskController::RegisterCallback(\n"
                "    IdleTask* idle_task,\n"
                "    const IdleRequestOptions* options) {\n"
                "  CallbackId id = NextCallbackId();\n"
                "  uint32_t timeout_millis = options->timeout();\n"
                "  PostSchedulerIdleAndTimeoutTasks(id, timeout_millis);\n"
                "  return id;\n"
                "}\n"
                "\n"
                "void ScriptedIdleTaskController::CancelCallback(CallbackId id) {\n"
                "  CHECK(IsValidCallbackId(id));\n"
                "\n"
                "  RemoveIdleTask(id);\n"
                "}\n"
                "\n"
                "void ScriptedIdleTaskController::RunIdleTask(\n"
                "    CallbackId id,\n"
                "    base::TimeTicks deadline,\n"
                "    IdleDeadline::CallbackType callback_type) {\n"
                "  auto idle_task_iter = idle_tasks_.find(id);\n"
                "  IdleTask* idle_task = idle_task_iter->value;\n"
                "  bool cross_origin_isolated_capability = true;\n"
                "  idle_task->invoke(MakeGarbageCollected<IdleDeadline>(\n"
                "      deadline, cross_origin_isolated_capability, "
                "callback_type));\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_blink_idle_callbacks(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_idle_callbacks(path)

            self.assertEqual(first, path.read_text(encoding="utf-8"))
            self.assertEqual(
                1, first.count("RecordBlinkIdleCallbackScheduled")
            )
            self.assertEqual(
                1, first.count("RecordBlinkIdleCallbackCancelled")
            )
            self.assertEqual(1, first.count("RecordBlinkIdleCallbackFired"))
            self.assertLess(
                first.index("RecordBlinkIdleCallbackFired"),
                first.index("idle_task->invoke"),
            )

    def test_patches_legacy_animation_frame_invocation_shape(self):
        with tempfile.TemporaryDirectory() as directory:
            path = (
                Path(directory)
                / "frame_request_callback_collection.cc"
            )
            path.write_text(
                '#include "third_party/blink/renderer/core/dom/'
                'frame_request_callback_collection.h"\n'
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                f"{INTEGRATE.BLINK_DOCUMENT_INCLUDE}\n"
                f"{INTEGRATE.BLINK_PAGE_INCLUDE}\n"
                "\n"
                "void RecordBlinkAnimationFrameScheduled() {}\n"
                "void RecordBlinkAnimationFrameCancelled() {}\n"
                "\n"
                "void FrameRequestCallbackCollection::"
                "ExecuteFrameCallbacksImpl(\n"
                "    CallbackList& callbacks_to_invoke,\n"
                "    double high_res_now_ms,\n"
                "    double high_res_now_ms_legacy) {\n"
                "  for (const auto& callback : callbacks_to_invoke) {\n"
                "    if (callback->IsCancelled()) {\n"
                "      continue;\n"
                "    }\n"
                "    callback->Invoke(high_res_now_ms);\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_blink_animation_frame_callbacks(path)
            result = path.read_text(encoding="utf-8")

            self.assertEqual(
                1, result.count("RecordBlinkAnimationFrameFired")
            )
            self.assertLess(
                result.index("RecordBlinkAnimationFrameFired"),
                result.index("callback->Invoke(high_res_now_ms);"),
            )

    def test_patches_current_chromium_shapes_idempotently(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            delegate = root / "chrome_main_delegate.cc"
            chrome_build = root / "chrome_BUILD.gn"
            child_launcher = root / "child_process_launcher_helper.cc"
            windows_child_launcher = (
                root / "child_process_launcher_helper_win.cc"
            )
            content_build = root / "content_browser_BUILD.gn"
            event_target = root / "event_target.cc"
            event_dispatcher = root / "event_dispatcher.cc"
            dom_timer = root / "dom_timer.cc"
            animation_frames = root / "frame_request_callback_collection.cc"
            blink_build = root / "blink_core_BUILD.gn"
            delegate.write_text(
                '#include "chrome/app/chrome_main_delegate.h"\n'
                "\n"
                "std::optional<int> "
                "ChromeMainDelegate::BasicStartupComplete() {\n"
                "  const base::CommandLine& command_line =\n"
                "      *base::CommandLine::ForCurrentProcess();\n"
                "}\n",
                encoding="utf-8",
            )
            chrome_build.write_text(
                'static_library("test_support") {\n'
                "  sources = [\n"
                '    "chrome_main_delegate.cc",\n'
                "  ]\n"
                "  deps = [\n"
                '    "//base",\n'
                "  ]\n"
                "}\n"
                "\n"
                "if (is_win) {\n"
                '  shared_library("chrome_dll") {\n'
                "    sources = [\n"
                '      "app/chrome_main_delegate.cc",\n'
                "    ]\n"
                "    deps = [\n"
                '      ":dependencies",\n'
                '      "//sandbox/win:sandbox",\n'
                "    ]\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )
            child_launcher.write_text(
                '#include "content/browser/child_process_launcher_helper.h"\n'
                "\n"
                "void ChildProcessLauncherHelper::"
                "LaunchOnLauncherThread() {\n"
                "  std::unique_ptr<FileMappedForLaunch> "
                "files_to_register = GetFilesToMap();\n"
                "  std::optional<base::LaunchOptions> options;\n"
                "  base::LaunchOptions* options_ptr = nullptr;\n"
                "  if (IsUsingLaunchOptions()) {\n"
                "    options.emplace();\n"
                "    options_ptr = &*options;\n"
                "  }\n"
                "  if (BeforeLaunchOnLauncherThread("
                "*files_to_register, options_ptr)) {\n"
                "    LaunchProcessOnLauncherThread();\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )
            windows_child_launcher.write_text(
                '#include "content/browser/child_process_launcher_helper.h"\n'
                f"{INTEGRATE.CHILD_LAUNCHER_INCLUDE}\n"
                "\n"
                "bool ChildProcessLauncherHelper::"
                "BeforeLaunchOnLauncherThread(\n"
                "    FileMappedForLaunch& files_to_register,\n"
                "    base::LaunchOptions* options) {\n"
                f"{INTEGRATE.ORIGINAL_CHILD_LAUNCHER_HOOK}"
                "  return true;\n"
                "}\n",
                encoding="utf-8",
            )
            content_build.write_text(
                'source_set("browser") {\n'
                "  if (is_win) {\n"
                "    sources += [\n"
                '      "child_process_launcher_helper_win.cc",\n'
                "    ]\n"
                "    deps += [\n"
                '      "//components/app_launch_prefetch",\n'
                "    ]\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )
            event_target.write_text(
                '#include "third_party/blink/renderer/core/dom/events/'
                'event_target.h"\n'
                '#include "base/time/time.h"\n'
                "\n"
                "bool EventTarget::AddEventListenerInternal() {\n"
                "  bool added = true;\n"
                "  if (added) {\n"
                "    CHECK(registered_listener);\n"
                "    AddedEventListener(event_type, *registered_listener);\n"
                "  }\n"
                "  return added;\n"
                "}\n"
                "\n"
                "bool EventTarget::RemoveEventListenerInternal() {\n"
                "  CHECK(registered_listener);\n"
                "  RemovedEventListener(event_type, *registered_listener);\n"
                "  return true;\n"
                "}\n"
                "\n"
                "bool EventTarget::FireEventListeners() {\n"
                "    listener->Invoke(context, &event);\n"
                "    EventListener* listener = registered_listener->Callback();\n"
                "    // The listener will be retained by Member<EventListener> in the\n"
                "    // registeredListener, i and size are updated with the firing "
                "event iterator\n"
                "    // in case the listener is removed from the listener vector "
                "below.\n"
                "    if (registered_listener->Once()) {\n"
                "      removeEventListener(event.type(), listener,\n"
                "                          registered_listener->Capture());\n"
                "    }\n"
                "    event.SetHandlingPassive(EventPassiveMode(*registered_listener));\n"
                "\n"
                "    probe::UserCallback probe(context, nullptr, event.type(), false, "
                "this);\n"
                "\n"
                "    // To match Mozilla, the AT_TARGET phase fires both capturing and "
                "bubbling\n"
                "    // event listeners, even though that violates some versions of "
                "the DOM spec.\n"
                "    listener->Invoke(context, &event);\n"
                "    fired_listener = true;\n"
                "}\n",
                encoding="utf-8",
            )
            event_dispatcher.write_text(
                '#include "third_party/blink/renderer/core/dom/events/'
                'event_dispatcher.h"\n'
                '#include "build/build_config.h"\n'
                "\n"
                "DispatchEventResult EventDispatcher::Dispatch() {\n"
                "  event_->SetTarget("
                "&EventPath::EventTargetRespectingTargetRules(*node_));\n"
                "#if DCHECK_IS_ON()\n"
                "  DCHECK(event_->RawTarget());\n"
                "#endif\n"
                "  auto result = "
                "EventTarget::GetDispatchEventResult(*event_);\n"
                "\n"
                "  return result;\n"
                "}\n",
                encoding="utf-8",
            )
            event_dispatcher.write_text(
                event_dispatcher.read_text(encoding="utf-8")
                + "\n"
                + "inline void EventDispatcher::DispatchEventPostProcess() {\n"
                + "  bool is_trusted_or_click = true;\n"
                + "  if (!event_->defaultPrevented() && "
                + "!event_->DefaultHandled() &&\n"
                + "      is_trusted_or_click) {\n"
                + "    node_->DefaultEventHandler(*event_);\n"
                + "    if (!event_->DefaultHandled() && "
                + "!event_->defaultPrevented() &&\n"
                + "        event_->bubbles()) {\n"
                + "      wtf_size_t size = event_->GetEventPath().size();\n"
                + "      for (wtf_size_t i = 1; i < size; ++i) {\n"
                + "        event_->GetEventPath()[i].GetNode()."
                + "DefaultEventHandler(*event_);\n"
                + "        if (event_->DefaultHandled() || "
                + "event_->defaultPrevented()) {\n"
                + "          break;\n"
                + "        }\n"
                + "      }\n"
                + "    }\n"
                + "  } else {\n"
                + "#if BUILDFLAG(IS_MAC)\n"
                + "#endif\n"
                + "  }\n"
                + "}\n",
                encoding="utf-8",
            )
            dom_timer.write_text(
                '#include "third_party/blink/renderer/core/scheduler/'
                'dom_timer.h"\n'
                '#include "base/check_deref.h"\n'
                "\n"
                "void DOMTimer::RemoveByID(ExecutionContext& context, "
                "int timeout_id) {\n"
                "  if (DOMTimer* timer =\n"
                "          DOMTimerCoordinator::From(context)."
                "RemoveTimeoutByID(timeout_id)) {\n"
                "    timer->SetExecutionContext(nullptr);\n"
                "  }\n"
                "}\n"
                "\n"
                "DOMTimer::DOMTimer(ExecutionContext& context,\n"
                "                   ScheduledAction* action,\n"
                "                   base::TimeDelta timeout,\n"
                "                   bool single_shot,\n"
                "                   StackOptions stack_options)\n"
                "    : timeout_id_(1), nesting_level_(0) {\n"
                "  DCHECK_GT(timeout_id_, 0);\n"
                "\n"
                "  if (timeout.is_negative()) {\n"
                "    timeout = base::TimeDelta();\n"
                "  }\n"
                "  if (single_shot) {\n"
                "    StartOneShot(timeout, FROM_HERE, true);\n"
                "  } else {\n"
                "    StartRepeating(timeout, FROM_HERE, true);\n"
                "  }\n"
                "  DEVTOOLS_TIMELINE_TRACE_EVENT_INSTANT(\n"
                '      "TimerInstall", inspector_timer_install_event::Data, '
                "&context,\n"
                "      timeout_id_, timeout, single_shot);\n"
                "}\n"
                "\n"
                "void DOMTimer::Stop() {\n"
                "  if (action_) {\n"
                "    const bool is_interval = RepeatInterval().has_value();\n"
                "\n"
                "    action_->Dispose();\n"
                "  }\n"
                "}\n"
                "\n"
                "void DOMTimer::Fired() {\n"
                "  ExecutionContext* context = GetExecutionContext();\n"
                "  TRACE_EVENT_INSTANT(\n"
                '      "devtools.timeline", "TimerFire", context, timeout_id_);\n'
                "  const bool is_interval = RepeatInterval().has_value();\n"
                "\n"
                "  action_->Execute(context);\n"
                "}\n",
                encoding="utf-8",
            )
            animation_frames.write_text(
                '#include "third_party/blink/renderer/core/dom/'
                'frame_request_callback_collection.h"\n'
                "\n"
                "FrameRequestCallbackCollection::CallbackId\n"
                "FrameRequestCallbackCollection::RegisterFrameCallback(\n"
                "    FrameCallback* callback,\n"
                "    FrameCallbackType type) {\n"
                "  CallbackId id = ++next_callback_id_;\n"
                "  callback->SetId(id);\n"
                '  DEVTOOLS_TIMELINE_TRACE_EVENT_INSTANT("RequestAnimationFrame",\n'
                "                                         "
                "inspector_animation_frame_event::Data,\n"
                "                                         context_, id);\n"
                "  return id;\n"
                "}\n"
                "\n"
                "void FrameRequestCallbackCollection::CancelFrameCallback(\n"
                "    CallbackId id,\n"
                "    FrameCallbackType type) {\n"
                "  auto& callbacks = frame_callbacks_;\n"
                "  auto& callbacks_to_invoke = callbacks_to_invoke_;\n"
                "  for (wtf_size_t i = 0; i < callbacks.size(); ++i) {\n"
                "    if (callbacks[i]->Id() == id) {\n"
                "      callbacks[i]->async_task_context()->Cancel();\n"
                "      callbacks.EraseAt(i);\n"
                "      return;\n"
                "    }\n"
                "  }\n"
                "  for (const auto& callback : callbacks_to_invoke) {\n"
                "    if (callback->Id() == id) {\n"
                "      callback->async_task_context()->Cancel();\n"
                "      callback->SetIsCancelled(true);\n"
                "      return;\n"
                "    }\n"
                "  }\n"
                "}\n"
                "\n"
                "void FrameRequestCallbackCollection::"
                "ExecuteFrameCallbacksImpl(\n"
                "    CallbackList& callbacks_to_invoke,\n"
                "    double high_res_now_ms,\n"
                "    double high_res_now_ms_legacy) {\n"
                "  for (const auto& callback : callbacks_to_invoke) {\n"
                "    if (callback->IsCancelled()) {\n"
                "      continue;\n"
                "    }\n"
                "    DEVTOOLS_TIMELINE_TRACE_EVENT_INSTANT(\n"
                '        "FireAnimationFrame", '
                "inspector_animation_frame_event::Data,\n"
                "        context_, callback->Id());\n"
                "    if (callback->GetUseLegacyTimeBase()) {\n"
                "      callback->Invoke(high_res_now_ms_legacy);\n"
                "    } else {\n"
                "      callback->Invoke(high_res_now_ms);\n"
                "    }\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )
            blink_build.write_text(
                'component("core") {\n'
                '  output_name = "blink_core"\n'
                "  deps = [\n"
                '    "//base",\n'
                "  ]\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_main_delegate(delegate)
            INTEGRATE.patch_chrome_build(chrome_build)
            INTEGRATE.remove_legacy_child_launcher_hook(
                windows_child_launcher
            )
            INTEGRATE.patch_child_launcher(child_launcher)
            INTEGRATE.patch_content_browser_build(content_build)
            INTEGRATE.patch_blink_event_target(event_target)
            INTEGRATE.patch_blink_event_dispatcher(event_dispatcher)
            INTEGRATE.patch_blink_dom_timer(dom_timer)
            INTEGRATE.patch_blink_animation_frame_callbacks(
                animation_frames
            )
            INTEGRATE.patch_blink_core_build(blink_build)
            first_delegate = delegate.read_text(encoding="utf-8")
            first_chrome_build = chrome_build.read_text(encoding="utf-8")
            first_child_launcher = child_launcher.read_text(encoding="utf-8")
            first_windows_child_launcher = (
                windows_child_launcher.read_text(encoding="utf-8")
            )
            first_content_build = content_build.read_text(encoding="utf-8")
            first_event_target = event_target.read_text(encoding="utf-8")
            first_event_dispatcher = event_dispatcher.read_text(
                encoding="utf-8"
            )
            first_dom_timer = dom_timer.read_text(encoding="utf-8")
            first_animation_frames = animation_frames.read_text(
                encoding="utf-8"
            )
            first_blink_build = blink_build.read_text(encoding="utf-8")

            INTEGRATE.patch_main_delegate(delegate)
            INTEGRATE.patch_chrome_build(chrome_build)
            INTEGRATE.remove_legacy_child_launcher_hook(
                windows_child_launcher
            )
            INTEGRATE.patch_child_launcher(child_launcher)
            INTEGRATE.patch_content_browser_build(content_build)
            INTEGRATE.patch_blink_event_target(event_target)
            INTEGRATE.patch_blink_event_dispatcher(event_dispatcher)
            INTEGRATE.patch_blink_dom_timer(dom_timer)
            INTEGRATE.patch_blink_animation_frame_callbacks(
                animation_frames
            )
            INTEGRATE.patch_blink_core_build(blink_build)

            self.assertEqual(
                first_delegate, delegate.read_text(encoding="utf-8")
            )
            self.assertEqual(
                first_chrome_build, chrome_build.read_text(encoding="utf-8")
            )
            self.assertEqual(
                first_child_launcher,
                child_launcher.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                first_windows_child_launcher,
                windows_child_launcher.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                first_content_build, content_build.read_text(encoding="utf-8")
            )
            self.assertEqual(
                first_event_target,
                event_target.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                first_event_dispatcher,
                event_dispatcher.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                first_dom_timer,
                dom_timer.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                first_animation_frames,
                animation_frames.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                first_blink_build,
                blink_build.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                1, first_delegate.count("InitializeProcessBridge")
            )
            self.assertIn(
                "Recorder process bridge initialization failed:",
                first_delegate,
            )
            self.assertIn(
                '#if BUILDFLAG(IS_WIN)\n'
                '#include "chromium/recorder_bridge/browser_bridge.h"\n'
                "#endif\n",
                first_delegate,
            )
            self.assertIn(
                '      "//chromium/recorder_bridge",\n', first_chrome_build
            )
            self.assertNotIn(
                '    "//chromium/recorder_bridge",\n',
                first_chrome_build.split('if (is_win) {', 1)[0],
            )
            self.assertIn(
                "AppendRecorderBootstrapToChildProcess",
                first_child_launcher,
            )
            self.assertIn(
                "Recorder child bootstrap attachment failed:",
                first_child_launcher,
            )
            self.assertIn(
                "child_process_id().value()",
                first_child_launcher,
            )
            self.assertIn(
                "command_line(), options_ptr, child_process_id().value(),",
                first_child_launcher,
            )
            self.assertIn(
                "    return;\n  }\n#endif\n"
                "  if (BeforeLaunchOnLauncherThread(",
                first_child_launcher,
            )
            self.assertNotIn(
                "AppendRecorderBootstrapToChildProcess",
                first_windows_child_launcher,
            )
            self.assertNotIn(
                INTEGRATE.CHILD_LAUNCHER_INCLUDE,
                first_windows_child_launcher,
            )
            self.assertIn(
                '      "//chromium/recorder_bridge",\n',
                first_content_build,
            )
            self.assertIn(
                "RecordBlinkListenerRegistered",
                first_event_target,
            )
            self.assertIn(
                "RecordBlinkListenerRemoved",
                first_event_target,
            )
            self.assertIn(
                "RecordBlinkListenerInvoked",
                first_event_target,
            )
            self.assertIn(
                "BeginBlinkListenerInvocation",
                first_event_target,
            )
            self.assertLess(
                first_event_target.index("BeginBlinkListenerInvocation"),
                first_event_target.index(
                    "if (registered_listener->Once())"
                ),
            )
            self.assertEqual(
                1,
                first_event_target.count("RecordBlinkListenerInvoked"),
            )
            self.assertEqual(
                2,
                first_event_target.count(
                    "listener->Invoke(context, &event);"
                ),
            )
            self.assertIn(
                "RecordBlinkDispatchStarted",
                first_event_dispatcher,
            )
            self.assertIn(
                "RecordBlinkDispatchCompleted",
                first_event_dispatcher,
            )
            self.assertIn(
                "RecordBlinkDispatchPathNode",
                first_event_dispatcher,
            )
            self.assertIn(
                "CompleteBlinkDispatchStart",
                first_event_dispatcher,
            )
            self.assertIn(
                "RecordBlinkDefaultAction",
                first_event_dispatcher,
            )
            self.assertEqual(
                3,
                first_event_dispatcher.count("RecordBlinkDefaultAction"),
            )
            self.assertLess(
                first_event_dispatcher.index(
                    "recorder_default_target_element"
                ),
                first_event_dispatcher.index(
                    "    node_->DefaultEventHandler(*event_);"
                ),
            )
            self.assertLess(
                first_event_dispatcher.index(
                    "recorder_default_ancestor_element"
                ),
                first_event_dispatcher.index(
                    "        event_->GetEventPath()[i].GetNode()."
                    "DefaultEventHandler(*event_);"
                ),
            )
            self.assertIn(
                "event_->defaultPrevented() ? 1 : "
                "event_->DefaultHandled() ? 2 : 3",
                first_event_dispatcher,
            )
            self.assertNotIn("event_.Get()", first_event_dispatcher)
            self.assertIn("RecordBlinkTimerScheduled", first_dom_timer)
            self.assertIn("RecordBlinkTimerFired", first_dom_timer)
            self.assertIn("RecordBlinkTimerCancelled", first_dom_timer)
            self.assertEqual(
                2,
                first_dom_timer.count(
                    "const bool is_interval = RepeatInterval().has_value();"
                ),
            )
            self.assertLess(
                first_dom_timer.index("RecordBlinkTimerScheduled"),
                first_dom_timer.index('"TimerInstall"'),
            )
            self.assertLess(
                first_dom_timer.index("RecordBlinkTimerFired"),
                first_dom_timer.index("action_->Execute(context);"),
            )
            self.assertIn(
                "RecordBlinkAnimationFrameScheduled",
                first_animation_frames,
            )
            self.assertEqual(
                2,
                first_animation_frames.count(
                    "RecordBlinkAnimationFrameCancelled"
                ),
            )
            self.assertIn(
                "RecordBlinkAnimationFrameFired",
                first_animation_frames,
            )
            self.assertLess(
                first_animation_frames.index(
                    "RecordBlinkAnimationFrameScheduled"
                ),
                first_animation_frames.index('"RequestAnimationFrame"'),
            )
            self.assertLess(
                first_animation_frames.index(
                    "RecordBlinkAnimationFrameFired"
                ),
                first_animation_frames.index(
                    "if (callback->GetUseLegacyTimeBase())"
                ),
            )
            self.assertEqual(
                1,
                first_event_dispatcher.count(
                    "RecordBlinkDispatchStarted"
                ),
            )
            self.assertIn(
                'payload.Set("composedPath", std::move(composed_path));',
                (Path(__file__).parent / "recorder_bridge" / "browser_bridge.cc")
                .read_text(encoding="utf-8"),
            )
            self.assertIn(
                "observed_propagation_stopped",
                (Path(__file__).parent / "recorder_bridge" / "browser_bridge.cc")
                .read_text(encoding="utf-8"),
            )
            self.assertIn(
                "ObserveDispatchState(event_identity, default_prevented,",
                (Path(__file__).parent / "recorder_bridge" / "browser_bridge.cc")
                .read_text(encoding="utf-8"),
            )
            self.assertIn(
                '    "//chromium/recorder_bridge",\n',
                first_blink_build,
            )

            event_target.write_text(
                first_event_target
                .replace(
                    INTEGRATE.CURRENT_BLINK_LISTENER_CALL,
                    INTEGRATE.LEGACY_BLINK_LISTENER_CALL,
                    1,
                )
                .replace(
                    INTEGRATE.BLINK_LISTENER_INVOCATION_STARTED_HOOK,
                    INTEGRATE.LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK,
                    1,
                ),
                encoding="utf-8",
            )
            event_dispatcher.write_text(
                first_event_dispatcher.replace(
                    INTEGRATE.BLINK_DISPATCH_HOOK,
                    INTEGRATE.LEGACY_BLINK_DISPATCH_HOOK_WITH_IDENTITY,
                    1,
                ),
                encoding="utf-8",
            )

            INTEGRATE.patch_blink_event_target(event_target)
            INTEGRATE.patch_blink_event_dispatcher(event_dispatcher)

            self.assertEqual(
                first_event_target,
                event_target.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                first_event_dispatcher,
                event_dispatcher.read_text(encoding="utf-8"),
            )

    def test_removes_all_historical_windows_hook_variants(self):
        for hook in (
            INTEGRATE.ORIGINAL_CHILD_LAUNCHER_HOOK,
            INTEGRATE.TRACED_CHILD_LAUNCHER_HOOK,
        ):
            with self.subTest(hook=hook):
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / "child_launcher_win.cc"
                    path.write_text(
                        '#include "content/browser/'
                        'child_process_launcher_helper.h"\n'
                        f"{INTEGRATE.CHILD_LAUNCHER_INCLUDE}\n"
                        "\n"
                        "bool BeforeLaunch() {\n"
                        f"{hook}"
                        "  return true;\n"
                        "}\n",
                        encoding="utf-8",
                    )

                    INTEGRATE.remove_legacy_child_launcher_hook(path)
                    result = path.read_text(encoding="utf-8")

                    self.assertNotIn(
                        "AppendRecorderBootstrapToChildProcess",
                        result,
                    )
                    self.assertNotIn(
                        INTEGRATE.CHILD_LAUNCHER_INCLUDE,
                        result,
                    )

    def test_upgrades_incorrect_shared_child_launcher_hook(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "child_process_launcher_helper.cc"
            path.write_text(
                '#include "content/browser/child_process_launcher_helper.h"\n'
                f"{INTEGRATE.CHILD_LAUNCHER_INCLUDE_BLOCK}"
                "\n"
                "void ChildProcessLauncherHelper::"
                "LaunchOnLauncherThread() {\n"
                "  std::optional<base::LaunchOptions> options;\n"
                "  base::LaunchOptions* options_ptr = nullptr;\n"
                f"{INTEGRATE.LEGACY_SHARED_CHILD_LAUNCHER_HOOK}"
                "  if (BeforeLaunchOnLauncherThread("
                "*files_to_register, options_ptr)) {\n"
                "    LaunchProcessOnLauncherThread();\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_child_launcher(path)
            patched = path.read_text(encoding="utf-8")

            self.assertNotIn(
                INTEGRATE.LEGACY_SHARED_CHILD_LAUNCHER_HOOK,
                patched,
            )
            self.assertIn(
                INTEGRATE.CHILD_LAUNCHER_HOOK,
                patched,
            )
            self.assertEqual(
                1,
                patched.count("AppendRecorderBootstrapToChildProcess"),
            )

    def test_native_bridge_distributes_bootstrap_only_to_renderers(self):
        bridge = (
            Path(__file__).parent / "recorder_bridge" / "browser_bridge.cc"
        ).read_text(encoding="utf-8")
        supported_processes = bridge.split(
            "bool IsSupportedChildProcess", 1
        )[1].split("}", 1)[0]

        self.assertIn("kChromiumRendererProcess", supported_processes)
        self.assertNotIn("kChromiumGpuProcess", supported_processes)
        self.assertNotIn("kChromiumUtilityProcess", supported_processes)

    def test_patches_scheduler_decision_boundary_idempotently(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            throttler_h = root / "task_queue_throttler.h"
            throttler_cc = root / "task_queue_throttler.cc"
            queue_cc = root / "main_thread_task_queue.cc"
            frame_h = root / "frame_scheduler_impl.h"
            build = root / "BUILD.gn"

            throttler_h.write_text(
                '#include "base/memory/raw_ptr.h"\n'
                "namespace scheduler {\n\n"
                "class BudgetPool;\n"
                "class TaskQueueThrottler {\n"
                " public:\n"
                "  TaskQueueThrottler("
                "base::sequence_manager::TaskQueue* task_queue,\n"
                "                     const base::TickClock* tick_clock);\n"
                " private:\n"
                "  friend class BudgetPool;\n"
                "  const raw_ptr<base::sequence_manager::TaskQueue> task_queue_;\n"
                "};\n"
                "}  // namespace scheduler\n",
                encoding="utf-8",
            )
            throttler_cc.write_text(
                '#include "base/check_op.h"\n'
                "TaskQueueThrottler::TaskQueueThrottler(\n"
                "    base::sequence_manager::TaskQueue* task_queue,\n"
                "    const base::TickClock* tick_clock)\n"
                "    : task_queue_(task_queue), tick_clock_(tick_clock) {}\n"
                "std::optional<base::sequence_manager::WakeUp>\n"
                "TaskQueueThrottler::GetNextAllowedWakeUp(\n"
                "    LazyNow* lazy_now,\n"
                "    std::optional<base::sequence_manager::WakeUp> "
                "next_desired_wake_up,\n"
                "    bool has_ready_task) {\n"
                "  return GetNextAllowedWakeUpImpl(lazy_now, "
                "next_desired_wake_up,\n"
                "                                  has_ready_task);\n"
                "}\n",
                encoding="utf-8",
            )
            queue_cc.write_text(
                "if (params.queue_traits.can_be_throttled) {\n"
                "      throttler_.emplace(task_queue_.get(),\n"
                "                         "
                "main_thread_scheduler_->GetTickClock());\n"
                "}\n",
                encoding="utf-8",
            )
            frame_h.write_text(
                "class FrameSchedulerImpl {\n"
                "  void UpdatePolicy();\n"
                "};\n",
                encoding="utf-8",
            )
            build.write_text(
                'blink_platform_sources("scheduler") {\n'
                "  deps = [\n"
                '    "//base",\n'
                "  ]\n"
                "}\n",
                encoding="utf-8",
            )

            patchers = (
                (INTEGRATE.patch_blink_task_queue_throttler_header, throttler_h),
                (INTEGRATE.patch_blink_task_queue_throttler, throttler_cc),
                (INTEGRATE.patch_blink_main_thread_task_queue, queue_cc),
                (INTEGRATE.patch_blink_frame_scheduler_header, frame_h),
                (INTEGRATE.patch_blink_scheduler_build, build),
            )
            for patcher, path in patchers:
                patcher(path)
            first = {path: path.read_text(encoding="utf-8") for _, path in patchers}
            for patcher, path in patchers:
                patcher(path)
                self.assertEqual(first[path], path.read_text(encoding="utf-8"))

            self.assertEqual(
                1,
                first[throttler_cc].count(
                    "RecordBlinkSchedulerWakeUpDeferred"
                ),
            )
            self.assertIn("throttler_.emplace(this,", first[queue_cc])
            self.assertIn(
                INTEGRATE.BLINK_SCHEDULER_DEP,
                first[build],
            )
            self.assertIn(
                '#include "base/memory/weak_ptr.h"',
                first[throttler_h],
            )
            self.assertIn(
                "base::WeakPtr<MainThreadTaskQueue> owner_;",
                first[throttler_h],
            )
            self.assertIn("owner_(owner->AsWeakPtr())", first[throttler_cc])

            throttler_h.write_text(
                first[throttler_h]
                .replace('#include "base/memory/weak_ptr.h"\n', "")
                .replace(
                    INTEGRATE.BLINK_THROTTLER_OWNER_MEMBER,
                    INTEGRATE.LEGACY_BLINK_THROTTLER_OWNER_MEMBER,
                ),
                encoding="utf-8",
            )
            throttler_cc.write_text(
                first[throttler_cc].replace(
                    INTEGRATE.BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION,
                    INTEGRATE.LEGACY_BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION,
                ),
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_task_queue_throttler_header(throttler_h)
            INTEGRATE.patch_blink_task_queue_throttler(throttler_cc)
            self.assertEqual(first[throttler_h], throttler_h.read_text())
            self.assertEqual(first[throttler_cc], throttler_cc.read_text())


if __name__ == "__main__":
    unittest.main()
