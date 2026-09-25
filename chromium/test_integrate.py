import importlib.util
import re
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("integrate.py")
SPEC = importlib.util.spec_from_file_location("chromium_integrate", MODULE_PATH)
assert SPEC and SPEC.loader
INTEGRATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(INTEGRATE)


# Hook bodies written by earlier revisions of the integration script. They are
# test data rather than integration templates: the script migrates a listener
# hook by replacing the region the hook introduces, so it does not carry a copy
# of every body it has ever written, and these bodies exist here to prove that a
# checkout holding one converges on the current body.
HISTORICAL_LISTENER_HOOK_NODE_ONLY = """\
    if (Node* recorder_target = ToNode()) {
      Element* recorder_element = DynamicTo<Element>(recorder_target);
      a11y_recorder::RecordBlinkListenerRegistered(
          reinterpret_cast<uintptr_t>(registered_listener),
          recorder_target->GetDocument().GetDomNodeId(),
          recorder_target->GetDomNodeId(),
          event_type.Utf8().c_str(),
          recorder_target->nodeName().Utf8().c_str(),
          recorder_element
              ? recorder_element->GetIdAttribute().Utf8().c_str()
              : "",
          registered_listener->Capture(),
          registered_listener->Passive(),
          registered_listener->Once());
    }
"""
HISTORICAL_LISTENER_REMOVED_HOOK_NODE_ONLY = """\
  if (Node* recorder_target = ToNode()) {
    Element* recorder_element = DynamicTo<Element>(recorder_target);
    a11y_recorder::RecordBlinkListenerRemoved(
        reinterpret_cast<uintptr_t>(registered_listener),
        recorder_target->GetDocument().GetDomNodeId(),
        recorder_target->GetDomNodeId(),
        event_type.Utf8().c_str(),
        recorder_target->nodeName().Utf8().c_str(),
        recorder_element
            ? recorder_element->GetIdAttribute().Utf8().c_str()
            : "",
        registered_listener->Capture(),
        registered_listener->Passive(),
        registered_listener->Once());
  }
"""
# The body written before a registration kind was reported, which recorded every
# EventTarget kind but always described the registration as an addEventListener
# call.
# The registration hook as protocol 0.19 wrote it, reporting the registration
# form but no location.
HISTORICAL_LISTENER_HOOK_WITHOUT_LOCATION = """\
    {
      const EventListener* recorder_callback = registered_listener->Callback();
      const char* recorder_registration_kind =
          RecorderListenerRegistrationKind(recorder_callback);
      Node* recorder_target = ToNode();
      LocalDOMWindow* recorder_window = ToLocalDOMWindow();
      Element* recorder_element = DynamicTo<Element>(recorder_target);
      LocalDOMWindow* recorder_document_window =
          recorder_window ? recorder_window
                          : DynamicTo<LocalDOMWindow>(GetExecutionContext());
      Document* recorder_document =
          recorder_target ? &recorder_target->GetDocument()
                          : (recorder_document_window
                                 ? recorder_document_window->document()
                                 : nullptr);
      a11y_recorder::RecordBlinkListenerRegistered(
          reinterpret_cast<uintptr_t>(registered_listener),
          recorder_registration_kind,
          recorder_target ? a11y_recorder::kEventTargetKindNode
                          : (recorder_window
                                 ? a11y_recorder::kEventTargetKindWindow
                                 : a11y_recorder::kEventTargetKindOther),
          InterfaceName().Utf8().c_str(),
          reinterpret_cast<uintptr_t>(this),
          recorder_document ? recorder_document->GetDomNodeId() : 0,
          recorder_target ? recorder_target->GetDomNodeId() : 0,
          event_type.Utf8().c_str(),
          recorder_target ? recorder_target->nodeName().Utf8().c_str() : "",
          recorder_element
              ? recorder_element->GetIdAttribute().Utf8().c_str()
              : "",
          registered_listener->Capture(),
          registered_listener->Passive(),
          registered_listener->Once());
    }
"""

# The registration hook as protocol 0.20 wrote it, reporting the call that
# made the registration but not the world its callback belongs to.
HISTORICAL_LISTENER_HOOK_WITHOUT_WORLD = """\
    {
      const EventListener* recorder_callback = registered_listener->Callback();
      const char* recorder_registration_kind =
          RecorderListenerRegistrationKind(recorder_callback);
      Node* recorder_target = ToNode();
      LocalDOMWindow* recorder_window = ToLocalDOMWindow();
      Element* recorder_element = DynamicTo<Element>(recorder_target);
      ExecutionContext* recorder_context = GetExecutionContext();
      SourceLocation* recorder_location =
          recorder_context ? CaptureSourceLocation(recorder_context) : nullptr;
      LocalDOMWindow* recorder_document_window =
          recorder_window ? recorder_window
                          : DynamicTo<LocalDOMWindow>(recorder_context);
      Document* recorder_document =
          recorder_target ? &recorder_target->GetDocument()
                          : (recorder_document_window
                                 ? recorder_document_window->document()
                                 : nullptr);
      a11y_recorder::RecordBlinkListenerRegistered(
          reinterpret_cast<uintptr_t>(registered_listener),
          recorder_registration_kind,
          recorder_target ? a11y_recorder::kEventTargetKindNode
                          : (recorder_window
                                 ? a11y_recorder::kEventTargetKindWindow
                                 : a11y_recorder::kEventTargetKindOther),
          InterfaceName().Utf8().c_str(),
          reinterpret_cast<uintptr_t>(this),
          recorder_document ? recorder_document->GetDomNodeId() : 0,
          recorder_target ? recorder_target->GetDomNodeId() : 0,
          event_type.Utf8().c_str(),
          recorder_target ? recorder_target->nodeName().Utf8().c_str() : "",
          recorder_element
              ? recorder_element->GetIdAttribute().Utf8().c_str()
              : "",
          registered_listener->Capture(),
          registered_listener->Passive(),
          registered_listener->Once(),
          recorder_location ? recorder_location->Url().Utf8().c_str() : "",
          recorder_location ? recorder_location->Function().Utf8().c_str() : "",
          recorder_location ? recorder_location->ScriptId() : 0,
          recorder_location ? static_cast<int>(recorder_location->LineNumber())
                            : 0,
          recorder_location
              ? static_cast<int>(recorder_location->ColumnNumber())
              : 0);
    }
"""

HISTORICAL_LISTENER_HOOK_WITHOUT_REGISTRATION_KIND = """\
    {
      Node* recorder_target = ToNode();
      LocalDOMWindow* recorder_window = ToLocalDOMWindow();
      Element* recorder_element = DynamicTo<Element>(recorder_target);
      a11y_recorder::RecordBlinkListenerRegistered(
          reinterpret_cast<uintptr_t>(registered_listener),
          recorder_target ? a11y_recorder::kEventTargetKindNode
                          : (recorder_window
                                 ? a11y_recorder::kEventTargetKindWindow
                                 : a11y_recorder::kEventTargetKindOther),
          InterfaceName().Utf8().c_str(),
          reinterpret_cast<uintptr_t>(this),
          recorder_target ? recorder_target->GetDocument().GetDomNodeId() : 0,
          recorder_target ? recorder_target->GetDomNodeId() : 0,
          event_type.Utf8().c_str(),
          recorder_target ? recorder_target->nodeName().Utf8().c_str() : "",
          recorder_element
              ? recorder_element->GetIdAttribute().Utf8().c_str()
              : "",
          registered_listener->Capture(),
          registered_listener->Passive(),
          registered_listener->Once());
    }
"""


class IntegrateTests(unittest.TestCase):
    def test_bridge_accepts_generated_negative_ax_node_ids(self):
        bridge_source = (
            MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.cc"
        ).read_text(encoding="utf-8")

        self.assertIn("accessibility_node_id == 0", bridge_source)
        self.assertNotIn("accessibility_node_id <= 0", bridge_source)
        self.assertIn("parent_accessibility_node_id != 0", bridge_source)
        self.assertNotIn("parent_accessibility_node_id > 0", bridge_source)

    def test_patches_renderer_accessibility_serialization_idempotently(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "render_accessibility_impl.cc"
            build = root / "BUILD.gn"
            source.write_text(
                '#include "content/renderer/accessibility/'
                'render_accessibility_impl.h"\n\n'
                "bool RenderAccessibilityImpl::"
                "SendAccessibilitySerialization() {\n"
                "  ax_annotators_manager_->AddDebuggingAttributes("
                "updates_and_events.updates);\n"
                "  return true;\n"
                "}\n",
                encoding="utf-8",
            )
            build.write_text(
                'target(link_target_type, "renderer") {\n'
                "  deps = [\n"
                '    "//base",\n'
                "  ]\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_content_renderer_accessibility(source)
            INTEGRATE.patch_content_renderer_build(build)
            first_source = source.read_text(encoding="utf-8")
            first_build = build.read_text(encoding="utf-8")
            INTEGRATE.patch_content_renderer_accessibility(source)
            INTEGRATE.patch_content_renderer_build(build)

            self.assertEqual(first_source, source.read_text(encoding="utf-8"))
            self.assertEqual(first_build, build.read_text(encoding="utf-8"))
            self.assertIn(
                "BeginRendererAccessibilityCheckpoint", first_source
            )
            self.assertIn(
                "RecordRendererAccessibilityCheckpointNode", first_source
            )
            self.assertIn(
                "CompleteRendererAccessibilityCheckpoint", first_source
            )
            self.assertIn(
                "recorder_update.has_tree_data", first_source
            )
            self.assertNotIn(
                "ax::mojom::State::kFocused", first_source
            )
            self.assertIn(
                "ui::ToString(recorder_node.role)", first_source
            )
            self.assertIn(
                INTEGRATE.CONTENT_RENDERER_AX_ENUM_INCLUDE, first_source
            )
            self.assertIn(
                '    "//chromium/recorder_bridge",', first_build
            )

            legacy_source = first_source.replace(
                INTEGRATE.CONTENT_RENDERER_FOCUSED_EXPRESSION,
                INTEGRATE.LEGACY_CONTENT_RENDERER_FOCUSED_EXPRESSION,
            )
            source.write_text(legacy_source, encoding="utf-8")
            INTEGRATE.patch_content_renderer_accessibility(source)
            upgraded_source = source.read_text(encoding="utf-8")
            self.assertIn(
                INTEGRATE.CONTENT_RENDERER_FOCUSED_EXPRESSION,
                upgraded_source,
            )
            self.assertNotIn(
                INTEGRATE.LEGACY_CONTENT_RENDERER_FOCUSED_EXPRESSION,
                upgraded_source,
            )

            # A checkout patched before the role name was recorded keeps its
            # hook body, so the migration has to supply both the new argument
            # and the include it needs.
            legacy_role_source = first_source.replace(
                INTEGRATE.CONTENT_RENDERER_ROLE_EXPRESSION,
                INTEGRATE.LEGACY_CONTENT_RENDERER_ROLE_EXPRESSION,
            ).replace(
                INTEGRATE.CONTENT_RENDERER_AX_ENUM_INCLUDE + "\n", ""
            )
            self.assertNotIn(
                "ui::ToString(recorder_node.role)", legacy_role_source
            )
            source.write_text(legacy_role_source, encoding="utf-8")
            INTEGRATE.patch_content_renderer_accessibility(source)
            upgraded_role_source = source.read_text(encoding="utf-8")
            self.assertIn(
                INTEGRATE.CONTENT_RENDERER_ROLE_EXPRESSION,
                upgraded_role_source,
            )
            self.assertIn(
                INTEGRATE.CONTENT_RENDERER_AX_ENUM_INCLUDE,
                upgraded_role_source,
            )
            self.assertEqual(
                1,
                upgraded_role_source.count(
                    INTEGRATE.CONTENT_RENDERER_AX_ENUM_INCLUDE
                ),
            )
            INTEGRATE.patch_content_renderer_accessibility(source)
            self.assertEqual(
                upgraded_role_source,
                source.read_text(encoding="utf-8"),
            )

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
                "bool EventTarget::SetAttributeEventListener("
                "const AtomicString& event_type,\n"
                "                                            "
                "EventListener* listener) {\n"
                "  RegisteredEventListener* registered_listener =\n"
                "      GetAttributeRegisteredEventListener(event_type);\n"
                "  if (!listener) {\n"
                "    if (registered_listener)\n"
                "      removeEventListener(event_type, "
                "registered_listener->Callback(), false);\n"
                "    return false;\n"
                "  }\n"
                "  if (registered_listener) {\n"
                "    registered_listener->SetCallback(listener);\n"
                "    return true;\n"
                "  }\n"
                "  return addEventListener(event_type, listener, false);\n"
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
                "a11y_recorder::kEventTargetKindWindow",
                first_event_target,
            )
            self.assertIn(
                "ToLocalDOMWindow()",
                first_event_target,
            )
            self.assertNotIn(
                "if (Node* recorder_target = ToNode()) {",
                first_event_target,
            )
            self.assertNotIn(
                "if (Node* recorder_current_target = ToNode()) {",
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
                "RecordBlinkDispatchPathWindow",
                first_event_dispatcher,
            )
            self.assertLess(
                first_event_dispatcher.index("RecordBlinkDispatchPathNode"),
                first_event_dispatcher.index("RecordBlinkDispatchPathWindow"),
            )
            self.assertLess(
                first_event_dispatcher.index("RecordBlinkDispatchPathWindow"),
                first_event_dispatcher.index("CompleteBlinkDispatchStart"),
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
                    INTEGRATE.BLINK_LISTENER_HOOK,
                    HISTORICAL_LISTENER_HOOK_WITHOUT_REGISTRATION_KIND,
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

    def _write_main_delegate(self, path: Path, hook: str = "") -> None:
        path.write_text(
            '#include "chrome/app/chrome_main_delegate.h"\n'
            "\n"
            "std::optional<int> ChromeMainDelegate::BasicStartupComplete() {\n"
            f"{hook}"
            "  return std::nullopt;\n"
            "}\n",
            encoding="utf-8",
        )

    def test_bridge_failure_exits_with_the_failure_code(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "chrome_main_delegate.cc"
            self._write_main_delegate(path)

            INTEGRATE.patch_main_delegate(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_main_delegate(path)

            self.assertEqual(first, path.read_text(encoding="utf-8"))
            self.assertIn(
                "return a11y_recorder::kBridgeInitializationFailureExitCode;",
                first,
            )
            self.assertNotIn("RESULT_CODE_NORMAL_EXIT", first)
            self.assertIn(
                "kBridgeInitializationFailureExitCode",
                (
                    Path(__file__).parent
                    / "recorder_bridge"
                    / "recorder_switches.h"
                ).read_text(encoding="utf-8"),
            )

    # Every bridge hook body this script has written, oldest first. A checkout
    # patched by any of these revisions must converge on the current body, so
    # each one is migrated here rather than trusted to match a template.
    HISTORICAL_BRIDGE_HOOKS = (
        # The original body: no diagnostic, and a normal exit code.
        "#if BUILDFLAG(IS_WIN)\n"
        "  std::string recorder_bridge_error;\n"
        "  if (!a11y_recorder::InitializeProcessBridge(\n"
        "          &recorder_bridge_error)) {\n"
        '    LOG(ERROR) << "Windows A11y Recorder bridge failed: "\n'
        "               << recorder_bridge_error;\n"
        "    return content::RESULT_CODE_NORMAL_EXIT;\n"
        "  }\n"
        "#endif\n",
        # A diagnostic was added, still with a normal exit code.
        "#if BUILDFLAG(IS_WIN)\n"
        "  std::string recorder_bridge_error;\n"
        "  if (!a11y_recorder::InitializeProcessBridge(\n"
        "          &recorder_bridge_error)) {\n"
        "    a11y_recorder::WriteRecorderBridgeDiagnostic(\n"
        '        "Recorder process bridge initialization failed: " +\n'
        "        recorder_bridge_error);\n"
        '    LOG(ERROR) << "Windows A11y Recorder bridge failed: "\n'
        "               << recorder_bridge_error;\n"
        "    return content::RESULT_CODE_NORMAL_EXIT;\n"
        "  }\n"
        "#endif\n",
        # The failure exit code was adopted, before the version query existed.
        "#if BUILDFLAG(IS_WIN)\n"
        "  std::string recorder_bridge_error;\n"
        "  if (!a11y_recorder::InitializeProcessBridge(\n"
        "          &recorder_bridge_error)) {\n"
        "    a11y_recorder::WriteRecorderBridgeDiagnostic(\n"
        '        "Recorder process bridge initialization failed: " +\n'
        "        recorder_bridge_error);\n"
        '    LOG(ERROR) << "Windows A11y Recorder bridge failed: "\n'
        "               << recorder_bridge_error;\n"
        "    return a11y_recorder::kBridgeInitializationFailureExitCode;\n"
        "  }\n"
        "#endif\n",
    )

    def test_migrates_every_historical_bridge_hook_body(self):
        for hook in self.HISTORICAL_BRIDGE_HOOKS:
            with self.subTest(hook=hook.splitlines()[4].strip()):
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / "chrome_main_delegate.cc"
                    self._write_main_delegate(path, hook)

                    INTEGRATE.patch_main_delegate(path)
                    patched = path.read_text(encoding="utf-8")

                    self.assertNotIn("RESULT_CODE_NORMAL_EXIT", patched)
                    self.assertEqual(
                        1,
                        patched.count(
                            "a11y_recorder::InitializeProcessBridge("
                        ),
                    )
                    self.assertIn(
                        "a11y_recorder::WriteProtocolVersionIfRequested(",
                        patched,
                    )
                    self.assertIn(INTEGRATE.HOOK, patched)

    def test_reports_a_duplicated_bridge_hook(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "chrome_main_delegate.cc"
            self._write_main_delegate(
                path, INTEGRATE.HOOK + self.HISTORICAL_BRIDGE_HOOKS[0]
            )

            with self.assertRaises(RuntimeError) as failure:
                INTEGRATE.patch_main_delegate(path)

            self.assertIn(
                "more than one recorder bridge hook", str(failure.exception)
            )

    def test_a_normal_exit_bridge_hook_never_reaches_a_build(self):
        """The migration is the fix, and this guard is its backstop."""
        with self.assertRaises(RuntimeError) as failure:
            INTEGRATE.verify_bridge_failure_is_fatal(
                self.HISTORICAL_BRIDGE_HOOKS[0], Path("chrome_main_delegate.cc")
            )

        self.assertIn(
            "kBridgeInitializationFailureExitCode", str(failure.exception)
        )

    def test_a_hook_without_the_version_query_never_reaches_a_build(self):
        """A hook that cannot answer the query would degrade the check quietly."""
        with self.assertRaises(RuntimeError) as failure:
            INTEGRATE.verify_protocol_query_is_answered(
                self.HISTORICAL_BRIDGE_HOOKS[-1],
                Path("chrome_main_delegate.cc"),
            )

        self.assertIn("protocol version query", str(failure.exception))

    def test_the_patched_hook_answers_the_version_query(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "chrome_main_delegate.cc"
            self._write_main_delegate(path)

            INTEGRATE.patch_main_delegate(path)
            patched = path.read_text(encoding="utf-8")

            self.assertIn(
                "return a11y_recorder::kProtocolVersionQueryExitCode;", patched
            )
            switches = (
                Path(__file__).parent / "recorder_bridge" / "recorder_switches.h"
            ).read_text(encoding="utf-8")
            for declaration in (
                "kProtocolVersionQueryExitCode",
                "kProtocolVersionOutputPrefix",
                "kPrintProtocolVersionSwitch",
            ):
                self.assertIn(declaration, switches)
            self.assertIn(
                "bool WriteProtocolVersionIfRequested();",
                (
                    Path(__file__).parent
                    / "recorder_bridge"
                    / "browser_bridge.h"
                ).read_text(encoding="utf-8"),
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

    def test_native_bridge_uses_root_frame_identity_for_page_id(self):
        bridge = (
            Path(__file__).parent / "recorder_bridge" / "browser_bridge.cc"
        ).read_text(encoding="utf-8")
        navigation_context = bridge.split(
            "base::DictValue CreateNavigationContext", 1
        )[1].split("base::DictValue CreateNavigationPayload", 1)[0]

        self.assertIn(
            '"frame-" + base::NumberToString(page_frame_tree_node_id)',
            navigation_context,
        )
        self.assertNotIn(
            '"page-" + base::NumberToString(page_frame_tree_node_id)',
            navigation_context,
        )

    def test_validation_waits_for_fixture_lifecycle_readiness(self):
        root = Path(__file__).parent.parent
        fixture = (
            root / "tests" / "fixtures" / "blink-listener-dispatch.html"
        ).read_text(encoding="utf-8")
        runner = (
            root / "scripts" / "Run-BlinkValidation.ps1"
        ).read_text(encoding="utf-8")
        ready_title = "Blink listener and dispatch fixture ready"

        self.assertIn(f'document.title = "{ready_title}"', fixture)
        self.assertIn(f'"{ready_title}"', runner)
        self.assertIn('(Get-CdpProperty $_ "title") -eq $ReadyTitle', runner)
        self.assertLess(
            fixture.index('document.addEventListener("visibilitychange"'),
            fixture.index(f'document.title = "{ready_title}"'),
        )

    def test_validation_schedules_lifecycle_evidence_while_visible(self):
        root = Path(__file__).parent.parent
        fixture = (
            root / "tests" / "fixtures" / "blink-listener-dispatch.html"
        ).read_text(encoding="utf-8")
        runner = (
            root / "scripts" / "Run-BlinkValidation.ps1"
        ).read_text(encoding="utf-8")

        # A page's visibility during load depends on when its window is shown,
        # so the fixture must not schedule its page-lifecycle timers at parse
        # time. They belong to a function the harness calls once the page
        # reports visible.
        self.assertIn(
            "window.recorderScheduleLifecycleEvidence = () => {", fixture
        )
        self.assertGreater(
            fixture.index("}, 3000);"),
            fixture.index("window.recorderScheduleLifecycleEvidence = () => {"),
        )
        self.assertGreater(
            fixture.index("}, 3500);"),
            fixture.index("window.recorderScheduleLifecycleEvidence = () => {"),
        )

        # The harness raises the page, requires its reported visibility, and
        # schedules the timers before the page is hidden, so the schedule is
        # recorded while visible and the callbacks enter while hidden.
        self.assertIn('"Page.bringToFront"', runner)
        self.assertIn("/json/activate/$TargetId", runner)
        self.assertIn("String(document.visibilityState)", runner)
        self.assertIn(
            "String(window.recorderScheduleLifecycleEvidence())", runner
        )
        self.assertIn('if ($outcome -ne "visible") {', runner)
        self.assertLess(
            runner.index("Start-FixtureLifecycleEvidence `"),
            runner.index("$backgroundTargetId = New-CdpBackgroundTarget"),
        )

    def test_bridge_waits_for_a_busy_recorder_pipe(self):
        root = Path(__file__).parent.parent
        protocol = (
            root / "chromium" / "recorder_bridge" / "recorder_protocol.cc"
        ).read_text(encoding="utf-8")
        header = (
            root / "chromium" / "recorder_bridge" / "recorder_protocol.h"
        ).read_text(encoding="utf-8")
        connect = protocol.split(
            "bool RecorderPipeClient::ConnectAndSynchronize", 1
        )[1].split("base::DictValue hello;", 1)[0]

        # A busy pipe is contention between processes starting at once, and a
        # missing pipe can be the gap between server instances, so both are
        # waited on. Anything else, an access denial above all, must be
        # reported rather than retried until the deadline expires.
        self.assertIn("ERROR_PIPE_BUSY", connect)
        self.assertIn("ERROR_FILE_NOT_FOUND", connect)
        self.assertIn("::WaitNamedPipeW(", connect)
        self.assertIn("kPipeConnectTimeoutMilliseconds", connect)
        self.assertIn(
            "inline constexpr uint32_t kPipeConnectTimeoutMilliseconds",
            header,
        )
        self.assertNotIn("ERROR_ACCESS_DENIED", connect)

        # The failure a process reports must name the Windows error and the
        # time waited, because a bare message cannot distinguish contention
        # that outlasted the deadline from a pipe this process may not open.
        self.assertIn(
            "Could not connect to the recorder named pipe after ",
            connect,
        )
        self.assertIn("Windows error ", connect)

    def test_validation_fails_when_a_process_bridge_failed(self):
        root = Path(__file__).parent.parent
        runner = (
            root / "scripts" / "Run-BlinkValidation.ps1"
        ).read_text(encoding="utf-8")
        bridge = (
            root / "chromium" / "recorder_bridge" / "browser_bridge.cc"
        ).read_text(encoding="utf-8")

        # The phrases the run treats as fatal must be phrases the bridge
        # actually writes, or the check would pass a run that lost a process.
        self.assertIn(
            '$_ -match "bridge pipe connection failed"',
            runner,
        )
        self.assertIn(
            '"Recorder process bridge pipe connection failed: "',
            bridge,
        )
        self.assertIn("reported a recorder bridge ", runner)
        self.assertIn("BRIDGE_INITIALIZATION_FAILURES=0", runner)

        # Contention that was waited out is reported without failing the run,
        # so a session that had to wait is still distinguishable from one that
        # did not.
        self.assertIn(
            '"Recorder process bridge waited for a free pipe instance "',
            bridge,
        )
        self.assertIn('$_ -match "waited for a free pipe instance"', runner)
        self.assertIn("BRIDGE_CONNECT_WAITS=$bridgeConnectWaits", runner)

    def test_verifier_accounts_for_every_recorded_transition(self):
        root = Path(__file__).parent.parent
        verifier = (
            root / "scripts" / "Verify-BlinkEvidence.ps1"
        ).read_text(encoding="utf-8")

        # An uncovered transition is reported in one of exactly two classes, and
        # the classes are required to sum to the reported total, so a transition
        # can no longer be dropped from the accounting without failing.
        self.assertIn("$uncoveredWithoutPass = 0", verifier)
        self.assertIn("$uncoveredAfterLastPass = 0", verifier)
        self.assertIn(
            "if (($uncoveredWithoutPass + $uncoveredAfterLastPass) -ne",
            verifier,
        )
        self.assertIn(
            "The uncovered transitions were not fully accounted for.",
            verifier,
        )

        # The counts reach the run summary, which is what turns the bound from a
        # caveat repeated in prose into a value a run reports.
        for key in (
            "CoveredTransitions =",
            "UncoveredTransitionsWithoutPass = $uncoveredWithoutPass",
            "UncoveredTransitionsAfterLastPass = $uncoveredAfterLastPass",
            "UncoveredTransitionDocuments = $uncoveredScopes.Count",
        ):
            self.assertIn(key, verifier)

    def test_verifier_rejects_a_hole_in_transition_coverage(self):
        root = Path(__file__).parent.parent
        verifier = (
            root / "scripts" / "Verify-BlinkEvidence.ps1"
        ).read_text(encoding="utf-8")

        # A transition inside the span its own document already claimed is a
        # defect in the coverage rather than a fact about the page, and two
        # passes may not claim one transition twice.
        self.assertIn(
            "so the coverage its passes report has a hole.",
            verifier,
        )
        self.assertIn(
            "claimed overlapping ",
            verifier,
        )
        self.assertIn("if ($sequence -gt $coverageLast[$scope]) {", verifier)

    def test_validation_reads_the_background_target_without_rest_method(self):
        root = Path(__file__).parent.parent
        runner = (
            root / "scripts" / "Run-BlinkValidation.ps1"
        ).read_text(encoding="utf-8")

        # Invoke-RestMethod does not enumerate a JSON array under Windows
        # PowerShell 5.1, so every DevTools HTTP read parses the content itself
        # and requires the identifier it uses to be a scalar string.
        invocations = [
            line
            for line in runner.splitlines()
            if "Invoke-RestMethod" in line and not line.strip().startswith("#")
        ]
        self.assertEqual([], invocations)
        self.assertIn("function New-CdpBackgroundTarget {", runner)
        self.assertIn(
            "Opening a background DevTools target returned no identifier.",
            runner,
        )
        self.assertIn(
            "if ($activated.StatusCode -ne 200) {",
            runner,
        )

    def test_validation_does_not_print_a_messageless_error_record(self):
        root = Path(__file__).parent.parent
        runner = (
            root / "scripts" / "Run-BlinkValidation.ps1"
        ).read_text(encoding="utf-8")

        # A record with no message renders as a bare category line and reads
        # like a failure in a run that passed, so it is counted rather than
        # printed and the count is still reported.
        self.assertIn("$emptyCaptureErrors = 0", runner)
        self.assertIn("++$emptyCaptureErrors", runner)
        self.assertIn(
            "record(s) that report nothing, which are not failures.",
            runner,
        )

        # A blank line the browser wrote to standard error cannot carry an
        # empty message through the remoting wrapper, so the record arrives
        # carrying its own category line as its message. Comparing the two is
        # what recognizes it.
        self.assertIn("$categoryText = [string] $_.CategoryInfo", runner)
        self.assertIn("$errorText -eq $categoryText", runner)
        self.assertNotIn(
            "$captureErrors |\n        ForEach-Object {\n            "
            "Write-Host $_\n        }",
            runner,
        )

    def test_bridge_reports_evidence_it_failed_to_write(self):
        root = Path(__file__).parent.parent
        bridge = (
            root / "chromium" / "recorder_bridge" / "browser_bridge.cc"
        ).read_text(encoding="utf-8")

        # A failed write used to leave nothing in the archive, so the loss was
        # visible only in a local log file. The count is held per channel and
        # reported on that channel as soon as the pipe accepts a write again.
        self.assertIn("void HoldOmittedEvidence(", bridge)
        self.assertIn("int TakeOmittedEvidence(", bridge)
        self.assertIn("ReportOmittedEvidence(client, channel);", bridge)
        self.assertIn('HoldOmittedEvidence(channel, 1);', bridge)
        self.assertIn(
            'kEvidenceWriteFailedOmissionReason[] =\n'
            '    "browser-evidence-write-failed";',
            bridge,
        )

        # The omission record is not captured evidence, so a failure to report
        # the loss must return the held count unchanged rather than count the
        # omission record itself as another lost record.
        report = bridge[bridge.index("void ReportOmittedEvidence("):]
        report = report[:report.index("void SendBlinkEvidence(")]
        self.assertIn("HoldOmittedEvidence(channel, count);", report)
        self.assertNotIn("count + 1", report)

    def test_omission_records_have_a_managed_contract(self):
        root = Path(__file__).parent.parent
        contracts = (
            root / "src" / "Recorder.Contracts" / "BrowserEvidenceContracts.cs"
        ).read_text(encoding="utf-8")
        protocol = (
            root
            / "src"
            / "Recorder.Collectors.Browser"
            / "BrowserProtocol.cs"
        ).read_text(encoding="utf-8")
        bridge_protocol = (
            root / "chromium" / "recorder_bridge" / "recorder_protocol.h"
        ).read_text(encoding="utf-8")

        # A bridge record with no matching managed contract is rejected on
        # ingest, which costs the rest of that renderer's evidence for the
        # session, so the new record type is mapped before the bridge sends it.
        self.assertIn(
            "public sealed record BrowserOmissionPayload(", contracts
        )
        self.assertIn(
            "BrowserEvidenceEventTypes.Omission) =>", protocol
        )
        self.assertIn(
            "payload.Deserialize<BrowserOmissionPayload>(JsonOptions)",
            protocol,
        )

        # The bridge and the recorder must agree on the protocol version, or
        # every connection is refused.
        self.assertIn('kProtocolVersion[] = "0.29"', bridge_protocol)
        self.assertIn('CurrentVersion = "0.29"', contracts)

    def test_validation_fails_when_the_run_lost_evidence(self):
        root = Path(__file__).parent.parent
        runner = (
            root / "scripts" / "Run-BlinkValidation.ps1"
        ).read_text(encoding="utf-8")
        verifier = (
            root / "scripts" / "Verify-BlinkEvidence.ps1"
        ).read_text(encoding="utf-8")

        # A reference run must be lossless, so a stated omission or a record the
        # event sink refused fails the run instead of passing quietly.
        self.assertIn('"browser-evidence-write-failed",', runner)
        self.assertIn('"browser-evidence-sink-refused"', runner)
        self.assertIn('$manifest.droppedEventCount', runner)
        self.assertIn(
            "This run lost evidence: $omittedRecords record(s) reported as ",
            runner,
        )
        self.assertIn('Write-Host "OMITTED_EVIDENCE_RECORDS=', runner)
        self.assertIn('Write-Host "SINK_REFUSED_EVENTS=', runner)

        # An omission that does not report a lost record, such as a rejected
        # connection, is reported without failing the run.
        self.assertIn("$otherOmissionRecords++", runner)
        self.assertIn('Write-Host "OTHER_OMISSION_RECORDS=', runner)

        # The verifier reports the loss and leaves the decision to the runner,
        # because the omission record is a true account of what happened.
        self.assertIn("EvidenceOmissionRecords = $browserOmissions.Count", verifier)
        self.assertIn("OmittedEvidenceRecords = $omittedRecordCount", verifier)
        self.assertIn("EvidenceOmissionReasons = ", verifier)

    def test_receiver_reports_records_the_sink_refused(self):
        root = Path(__file__).parent.parent
        receiver = (
            root
            / "src"
            / "Recorder.Collectors.Browser"
            / "BrowserEvidenceReceiver.cs"
        ).read_text(encoding="utf-8")

        # A record the sink refused was counted only as collector health, which
        # the archive does not carry, so the loss is now stated on the channel
        # that lost it as soon as the sink accepts records again.
        self.assertIn("RecordRefusedRecord(message.Channel);", receiver)
        self.assertIn("ReportRefusedRecords(message.Channel);", receiver)
        self.assertIn(
            "BrowserEvidenceOmissionReasons.SinkRefusedRecord", receiver
        )
        self.assertIn(
            "ReturnRefusedRecords(channel, count);", receiver
        )

    def test_validation_registers_an_isolated_world_listener(self):
        root = Path(__file__).parent.parent
        fixture = (
            root / "tests" / "fixtures" / "blink-listener-dispatch.html"
        ).read_text(encoding="utf-8")
        runner = (
            root / "scripts" / "Run-BlinkValidation.ps1"
        ).read_text(encoding="utf-8")
        verifier = (
            root / "scripts" / "Verify-BlinkEvidence.ps1"
        ).read_text(encoding="utf-8")
        world_name = "A11yRecorderValidationWorld"

        # The registration target must exist in the fixture but must never be
        # touched by the fixture's own script, or the recorded registration
        # would not be the isolated world's alone.
        self.assertIn('id="isolated-world-target"', fixture)
        self.assertGreater(
            fixture.index('id="isolated-world-target"'),
            fixture.rindex("</script>"),
        )

        self.assertIn('"Page.createIsolatedWorld"', runner)
        self.assertIn(f'"{world_name}"', runner)
        self.assertIn("isolated-world-target", runner)
        self.assertIn("__a11yRecorderIsolatedMarker", runner)

        self.assertIn(f'$isolatedWorld.name -ne "{world_name}"', verifier)
        self.assertIn('-ne "inspector-isolated"', verifier)

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
                "static_cast<int>(throttling_type_.get())",
                first[frame_h],
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
            self.assertIn(
                "TaskQueueThrottler(\n"
                "    base::sequence_manager::TaskQueue* task_queue,",
                first[throttler_cc],
            )

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
                    INTEGRATE.LEGACY_BLINK_THROTTLER_WEAK_CONSTRUCTOR_IMPLEMENTATION,
                ),
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_task_queue_throttler_header(throttler_h)
            INTEGRATE.patch_blink_task_queue_throttler(throttler_cc)
            self.assertEqual(first[throttler_h], throttler_h.read_text())
            self.assertEqual(first[throttler_cc], throttler_cc.read_text())

            frame_h.write_text(
                first[frame_h].replace(
                    INTEGRATE.BLINK_FRAME_THROTTLING_ACCESSOR,
                    INTEGRATE.LEGACY_BLINK_FRAME_THROTTLING_ACCESSOR,
                ),
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_frame_scheduler_header(frame_h)
            self.assertEqual(first[frame_h], frame_h.read_text())

            frame_h.write_text(
                first[frame_h].replace(
                    INTEGRATE.BLINK_FRAME_THROTTLING_ACCESSOR,
                    INTEGRATE.INTERMEDIATE_BLINK_FRAME_THROTTLING_ACCESSOR,
                ),
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_frame_scheduler_header(frame_h)
            self.assertEqual(first[frame_h], frame_h.read_text())

    def test_patches_navigation_boundaries_idempotently(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "web_contents_impl.cc"
            path.write_text(
                '#include "content/browser/web_contents/web_contents_impl.h"\n'
                "\n"
                "void WebContentsImpl::DidStartNavigation(\n"
                "    NavigationHandle* navigation_handle) {\n"
                '  TRACE_EVENT1("navigation", '
                '"WebContentsImpl::DidStartNavigation",\n'
                '               "navigation_handle", navigation_handle);\n'
                "  const bool is_in_main_frame = "
                "navigation_handle->IsInMainFrame();\n"
                "  const GURL url = navigation_handle->GetURL();\n"
                "\n"
                "  base::ElapsedTimer duration;\n"
                "}\n"
                "\n"
                "void WebContentsImpl::DidFinishNavigation(\n"
                "    NavigationHandle* navigation_handle) {\n"
                '  TRACE_EVENT1("navigation", '
                '"WebContentsImpl::DidFinishNavigation",\n'
                '               "navigation_handle", navigation_handle);\n'
                "\n"
                "  observers_.NotifyObservers(\n"
                "      &WebContentsObserver::DidFinishNavigation,\n"
                "      navigation_handle);\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_web_contents_navigation(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_web_contents_navigation(path)

            self.assertEqual(first, path.read_text(encoding="utf-8"))
            self.assertEqual(
                1,
                first.count("RecordBrowserNavigationStarted"),
            )
            self.assertEqual(
                1,
                first.count("RecordBrowserNavigationCompleted"),
            )
            self.assertIn(
                "GetFrameTreeNodeId().GetUnsafeValue()",
                first,
            )
            self.assertIn("GetParentFrame()", first)
            self.assertIn("GetParentFrameOrOuterDocument()", first)
            self.assertIn("GetMainFrame()", first)
            self.assertIn("GetDocumentToken()", first)
            self.assertIn("GetProcess()->GetProcess().Pid()", first)
            self.assertIn("FrameType::kPrerenderMainFrame", first)
            self.assertIn("FrameType::kFencedFrameRoot", first)
            self.assertIn("FrameType::kGuestMainFrame", first)
            self.assertNotIn(
                "if (navigation_handle->IsInPrimaryMainFrame())",
                first,
            )
            self.assertNotIn("reinterpret_cast<uintptr_t>(this)", first)
            self.assertIn(
                "navigation_handle->HasCommitted() &&\n"
                "          navigation_handle->IsErrorPage()",
                first,
            )
            self.assertIn(INTEGRATE.CONTENT_NAVIGATION_INCLUDE, first)

    def test_patches_document_finished_parsing_idempotently(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "document.cc"
            path.write_text(
                '#include "third_party/blink/renderer/core/dom/document.h"\n'
                '#include "third_party/blink/renderer/core/dom/element.h"\n'
                '#include "third_party/blink/renderer/core/dom/node_traversal.h"\n'
                "\n"
                "void Document::FinishedParsing() {\n"
                "  SetParsingState(kInDOMContentLoaded);\n"
                "  DocumentParserTiming::From(*this).MarkParserStop();\n"
                "\n"
                "  DispatchEvent();\n"
                "}\n"
                "\n"
                "void Document::NotifyChangeChildren(\n"
                "    const ContainerNode& container,\n"
                "    const ContainerNode::ChildrenChange& change) {\n"
                "  NotifySelection();\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_blink_document(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_document(path)

            self.assertEqual(first, path.read_text(encoding="utf-8"))
            self.assertEqual(1, first.count("BeginBlinkDomCheckpoint"))
            self.assertEqual(1, first.count("RecordBlinkDomCheckpointNode("))
            self.assertEqual(1, first.count("CompleteBlinkDomCheckpoint"))
            self.assertIn("kRecorderMaximumDomCheckpointNodes = 512", first)
            self.assertEqual(1, first.count(INTEGRATE.BLINK_DOM_CHECKPOINT_HOOK))
            self.assertEqual(
                1, first.count(INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER)
            )
            self.assertLess(
                first.index(INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER),
                first.index("void Document::FinishedParsing() {"),
            )
            # The traversal is composed: a hosted shadow root of any mode is
            # recorded with its host as its parent, and slot assignments are
            # read without a recalculation.
            self.assertIn("recorder_element->GetShadowRoot()", first)
            self.assertIn("recorder_node.ParentOrShadowHostNode()", first)
            self.assertIn("RecordBlinkDomCheckpointShadowRoot(", first)
            self.assertIn("AssignedNodesNoRecalc()", first)
            self.assertNotIn("->AssignedNodes()", first)
            self.assertNotIn("RecalcAssignment()", first)
            self.assertIn("Token().ToString()", first)
            for include in INTEGRATE.BLINK_DOM_CHECKPOINT_INCLUDES:
                self.assertIn(include, first)
            self.assertIn(
                "if (HasFinishedParsing())\n"
                "    MutationObserver::EnqueueRecorderDomCheckpoint(*this)",
                first,
            )
            self.assertIn(INTEGRATE.BLINK_BRIDGE_INCLUDE, first)

    def test_patches_mutation_delivery_idempotently(self):
        with tempfile.TemporaryDirectory() as directory:
            header_path = Path(directory) / "mutation_observer.h"
            header_path.write_text(
                "class MutationObserver {\n"
                " public:\n"
                "  static void EnqueueSlotChange(HTMLSlotElement&);\n"
                "};\n",
                encoding="utf-8",
            )
            path = Path(directory) / "mutation_observer.cc"
            path.write_text(
                '#include "third_party/blink/renderer/core/dom/mutation_observer.h"\n'
                '#include "third_party/blink/renderer/core/dom/node.h"\n'
                "\n"
                "class MutationObserverAgentData {\n"
                " public:\n"
                "  void Trace(Visitor* visitor) const override {\n"
                "    Supplement<Agent>::Trace(visitor);\n"
                "    visitor->Trace(active_mutation_observers_);\n"
                "    visitor->Trace(active_slot_change_list_);\n"
                "  }\n"
                "\n"
                "  void ActivateObserver(MutationObserver* observer) {\n"
                "    EnsureEnqueueMicrotask();\n"
                "    active_mutation_observers_.insert(observer);\n"
                "  }\n"
                "\n"
                "  void EnsureEnqueueMicrotask() {\n"
                "    if (active_mutation_observers_.empty() &&\n"
                "        active_slot_change_list_.empty()) {\n"
                "      Enqueue();\n"
                "    }\n"
                "  }\n"
                "\n"
                "  void DeliverMutations() {\n"
                "    MutationObserverVector observers(active_mutation_observers_);\n"
                "    active_mutation_observers_.clear();\n"
                "    SlotChangeList slots;\n"
                "    slots.swap(active_slot_change_list_);\n"
                "    for (const auto& observer : observers)\n"
                "      observer->Deliver();\n"
                "    for (const auto& slot : slots)\n"
                "      slot->DispatchSlotChangeEvent();\n"
                "  }\n"
                "\n"
                " private:\n"
                "  MutationObserverSet active_mutation_observers_;\n"
                "  SlotChangeList active_slot_change_list_;\n"
                "};\n"
                "\n"
                "// static\n"
                "void MutationObserver::EnqueueSlotChange(HTMLSlotElement& slot) {\n"
                "  Enqueue(slot);\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_blink_mutation_observer_header(header_path)
            header_first = header_path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_mutation_observer(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_mutation_observer_header(header_path)
            INTEGRATE.patch_blink_mutation_observer(path)

            self.assertEqual(
                header_first,
                header_path.read_text(encoding="utf-8"),
            )
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            self.assertEqual(
                1,
                header_first.count("EnqueueRecorderDomCheckpoint"),
            )
            self.assertGreaterEqual(
                first.count("recorder_mutated_documents"),
                4,
            )
            self.assertEqual(1, first.count('"post-mutation"'))
            self.assertEqual(0, first.count("BeginBlinkDomCheckpoint"))
            self.assertEqual(
                1, first.count(INTEGRATE.BLINK_DOM_CHECKPOINT_DECLARATION)
            )
            self.assertLess(
                first.index(INTEGRATE.BLINK_DOM_CHECKPOINT_DECLARATION),
                first.index("class MutationObserverAgentData"),
            )
            self.assertIn(
                "MutationObserver::EnqueueRecorderDomCheckpoint",
                first,
            )
            self.assertIn("recorder_mutated_documents_.insert", first)
            self.assertIn(
                "recorder_mutated_documents_.empty()",
                first,
            )
            self.assertIn("recorder_document->HasFinishedParsing()", first)
            self.assertIn("recorder_document->IsActive()", first)
            self.assertIn(INTEGRATE.BLINK_BRIDGE_INCLUDE, first)

    def test_migrates_protocol_010_navigation_hooks_idempotently(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "web_contents_impl.cc"
            path.write_text(
                '#include "content/browser/web_contents/web_contents_impl.h"\n'
                f"{INTEGRATE.CONTENT_NAVIGATION_INCLUDE}\n"
                "\n"
                "void WebContentsImpl::DidStartNavigation(\n"
                "    NavigationHandle* navigation_handle) {\n"
                '  TRACE_EVENT1("navigation", '
                '"WebContentsImpl::DidStartNavigation",\n'
                '               "navigation_handle", navigation_handle);\n'
                "  const bool is_in_main_frame = "
                "navigation_handle->IsInMainFrame();\n"
                "  const GURL url = navigation_handle->GetURL();\n"
                "\n"
                f"{INTEGRATE.LEGACY_CONTENT_NAVIGATION_STARTED_HOOK}\n"
                "  base::ElapsedTimer duration;\n"
                "}\n"
                "\n"
                "void WebContentsImpl::DidFinishNavigation(\n"
                "    NavigationHandle* navigation_handle) {\n"
                '  TRACE_EVENT1("navigation", '
                '"WebContentsImpl::DidFinishNavigation",\n'
                '               "navigation_handle", navigation_handle);\n'
                "\n"
                f"{INTEGRATE.LEGACY_CONTENT_NAVIGATION_COMPLETED_HOOK}\n"
                "  observers_.NotifyObservers(\n"
                "      &WebContentsObserver::DidFinishNavigation,\n"
                "      navigation_handle);\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_web_contents_navigation(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_web_contents_navigation(path)

            self.assertEqual(first, path.read_text(encoding="utf-8"))
            self.assertNotIn(
                INTEGRATE.LEGACY_CONTENT_NAVIGATION_STARTED_HOOK,
                first,
            )
            self.assertNotIn(
                INTEGRATE.LEGACY_CONTENT_NAVIGATION_COMPLETED_HOOK,
                first,
            )
            self.assertEqual(
                1,
                first.count("RecordBrowserNavigationStarted"),
            )
            self.assertEqual(
                1,
                first.count("RecordBrowserNavigationCompleted"),
            )
            self.assertIn("recorder_page_frame_tree_node_id", first)
            self.assertIn("recorder_parent_frame_tree_node_id", first)
            self.assertIn(
                "recorder_parent_or_outer_document_frame_tree_node_id",
                first,
            )
            self.assertIn("recorder_frame_type", first)
            self.assertIn("recorder_primary_page", first)


    def test_validation_treats_native_stderr_as_progress(self):
        runner = (
            Path(__file__).parent.parent / "scripts" / "Run-BlinkValidation.ps1"
        ).read_text(encoding="utf-8")

        self.assertIn('$previousPreference = $ErrorActionPreference', runner)
        self.assertIn('$ErrorActionPreference = "Continue"', runner)
        self.assertIn(
            "$ErrorActionPreference = $previousPreference",
            runner,
        )
        relaxed = runner.index('$ErrorActionPreference = "Continue"')
        invoked = runner.index("& $Command", relaxed)
        restored = runner.index(
            "$ErrorActionPreference = $previousPreference",
            invoked,
        )
        checked = runner.index("if ($LASTEXITCODE -ne 0) {", restored)

        self.assertLess(relaxed, invoked)
        self.assertLess(invoked, restored)
        self.assertLess(restored, checked)

    def test_validation_renders_native_stderr_as_plain_text(self):
        """Progress output must not read as a failure in a passing run."""
        runner = (
            Path(__file__).parent.parent / "scripts" / "Run-BlinkValidation.ps1"
        ).read_text(encoding="utf-8")

        self.assertIn("& $Command 2>&1 | ForEach-Object {", runner)
        self.assertIn(
            "if ($_ -is [System.Management.Automation.ErrorRecord]) {",
            runner,
        )
        self.assertIn("Write-Output $_.ToString()", runner)
        merged = runner.index("& $Command 2>&1 | ForEach-Object {")
        rendered = runner.index("Write-Output $_.ToString()", merged)
        checked = runner.index("if ($LASTEXITCODE -ne 0) {", rendered)
        self.assertLess(merged, rendered)
        self.assertLess(rendered, checked)


    def test_migrates_protocol_013_document_identity_hooks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            navigation_path = root / "web_contents_impl.cc"
            navigation_path.write_text(
                '#include "content/browser/web_contents/web_contents_impl.h"\n'
                f"{INTEGRATE.CONTENT_NAVIGATION_INCLUDE}\n"
                "\n"
                "void WebContentsImpl::DidStartNavigation(\n"
                "    NavigationHandle* navigation_handle) {\n"
                "  const GURL url = navigation_handle->GetURL();\n"
                "\n"
                f"{INTEGRATE.CONTENT_NAVIGATION_STARTED_HOOK}\n"
                "  base::ElapsedTimer duration;\n"
                "}\n"
                "\n"
                "void WebContentsImpl::DidFinishNavigation(\n"
                "    NavigationHandle* navigation_handle) {\n"
                "\n"
                f"{INTEGRATE.INTERMEDIATE_CONTENT_NAVIGATION_COMPLETED_HOOK}\n"
                "  observers_.NotifyObservers(\n"
                "      &WebContentsObserver::DidFinishNavigation,\n"
                "      navigation_handle);\n"
                "}\n",
                encoding="utf-8",
            )

            document_path = root / "document.cc"
            document_path.write_text(
                '#include "third_party/blink/renderer/core/dom/document.h"\n'
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                '#include "third_party/blink/renderer/core/dom/node_traversal.h"\n'
                "\n"
                "void Document::FinishedParsing() {\n"
                "  DocumentParserTiming::From(*this).MarkParserStop();\n"
                "\n"
                f"{INTEGRATE.ORIGINAL_BLINK_DOM_CHECKPOINT_HOOK}"
                "\n"
                "  DispatchEvent();\n"
                "}\n"
                "\n"
                "void Document::NotifyChangeChildren(\n"
                "    const ContainerNode& container,\n"
                "    const ContainerNode::ChildrenChange& change) {\n"
                f"{INTEGRATE.BLINK_DOCUMENT_MUTATION_HOOK}"
                "  NotifySelection();\n"
                "}\n",
                encoding="utf-8",
            )

            mutation_path = root / "mutation_observer.cc"
            mutation_path.write_text(
                '#include "third_party/blink/renderer/core/dom/mutation_observer.h"\n'
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                '#include "third_party/blink/renderer/core/dom/node.h"\n'
                '#include "third_party/blink/renderer/core/dom/node_traversal.h"\n'
                "\n"
                "class MutationObserverAgentData {\n"
                " public:\n"
                "  void Trace(Visitor* visitor) const override {\n"
                "    visitor->Trace(active_mutation_observers_);\n"
                f"{INTEGRATE.BLINK_MUTATION_AGENT_TRACE_HOOK}"
                "    visitor->Trace(active_slot_change_list_);\n"
                "  }\n"
                "\n"
                f"{INTEGRATE.BLINK_MUTATION_AGENT_METHOD}"
                "  void ActivateObserver(MutationObserver* observer) {\n"
                "    active_mutation_observers_.insert(observer);\n"
                "  }\n"
                "\n"
                "  void EnsureEnqueueMicrotask() {\n"
                "    if (active_mutation_observers_.empty() &&\n"
                "        active_slot_change_list_.empty() &&\n"
                "        recorder_mutated_documents_.empty()) {\n"
                "      Enqueue();\n"
                "    }\n"
                "  }\n"
                "\n"
                "  void DeliverMutations() {\n"
                "    MutationObserverVector observers(active_mutation_observers_);\n"
                f"{INTEGRATE.BLINK_POST_MUTATION_DOM_CHECKPOINT_HOOK}"
                "    active_mutation_observers_.clear();\n"
                "    SlotChangeList slots;\n"
                "    slots.swap(active_slot_change_list_);\n"
                "    for (const auto& observer : observers)\n"
                "      observer->Deliver();\n"
                "    for (const auto& slot : slots)\n"
                "      slot->DispatchSlotChangeEvent();\n"
                f"{INTEGRATE.ORIGINAL_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK}"
                "  }\n"
                "\n"
                " private:\n"
                f"{INTEGRATE.BLINK_MUTATION_AGENT_MEMBER}"
                "  MutationObserverSet active_mutation_observers_;\n"
                "  SlotChangeList active_slot_change_list_;\n"
                "};\n"
                "\n"
                f"{INTEGRATE.BLINK_MUTATION_OBSERVER_METHOD}"
                "// static\n"
                "void MutationObserver::EnqueueSlotChange(HTMLSlotElement& slot) {\n"
                "  Enqueue(slot);\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_web_contents_navigation(navigation_path)
            INTEGRATE.patch_blink_document(document_path)
            INTEGRATE.patch_blink_mutation_observer(mutation_path)
            navigation_first = navigation_path.read_text(encoding="utf-8")
            document_first = document_path.read_text(encoding="utf-8")
            mutation_first = mutation_path.read_text(encoding="utf-8")
            INTEGRATE.patch_web_contents_navigation(navigation_path)
            INTEGRATE.patch_blink_document(document_path)
            INTEGRATE.patch_blink_mutation_observer(mutation_path)

            self.assertEqual(
                navigation_first,
                navigation_path.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                document_first,
                document_path.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                mutation_first,
                mutation_path.read_text(encoding="utf-8"),
            )

            self.assertNotIn(
                INTEGRATE.INTERMEDIATE_CONTENT_NAVIGATION_COMPLETED_HOOK,
                navigation_first,
            )
            self.assertIn(
                INTEGRATE.CONTENT_NAVIGATION_COMPLETED_HOOK,
                navigation_first,
            )
            self.assertEqual(
                1,
                navigation_first.count("RecordBrowserNavigationCompleted"),
            )
            self.assertIn("recorder_document_token", navigation_first)
            self.assertIn("recorder_renderer_process_id", navigation_first)

            self.assertNotIn(
                INTEGRATE.ORIGINAL_BLINK_DOM_CHECKPOINT_HOOK,
                document_first,
            )
            self.assertIn(INTEGRATE.BLINK_DOM_CHECKPOINT_HOOK, document_first)
            self.assertEqual(1, document_first.count("BeginBlinkDomCheckpoint"))
            self.assertEqual(
                1,
                document_first.count("RecordBlinkDomCheckpointNode("),
            )
            self.assertEqual(
                1,
                document_first.count("CompleteBlinkDomCheckpoint"),
            )
            self.assertIn("Token().ToString()", document_first)

            self.assertNotIn(
                INTEGRATE.ORIGINAL_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
                mutation_first,
            )
            self.assertIn(
                INTEGRATE.BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
                mutation_first,
            )
            # The post-mutation checkpoint is recorded by the helper that
            # document.cc defines, so the delivery hook only calls it.
            self.assertEqual(0, mutation_first.count("BeginBlinkDomCheckpoint"))
            self.assertEqual(
                1,
                mutation_first.count(
                    'RecorderRecordDomCheckpoint(*recorder_document, '
                    '"post-mutation")'
                ),
            )
            self.assertIn(
                INTEGRATE.BLINK_DOM_CHECKPOINT_DECLARATION, mutation_first
            )


    def test_migrates_protocol_014_checkpoint_attribute_hooks(self):
        """A 0.14 checkpoint hook must be replaced, not left beside 0.15."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            document_path = root / "document.cc"
            document_path.write_text(
                '#include "third_party/blink/renderer/core/dom/document.h"\n'
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                '#include "third_party/blink/renderer/core/dom/node_traversal.h"\n'
                "\n"
                "void Document::FinishedParsing() {\n"
                "  DocumentParserTiming::From(*this).MarkParserStop();\n"
                "\n"
                f"{INTEGRATE.LEGACY_BLINK_DOM_CHECKPOINT_HOOK}"
                "\n"
                "  DispatchEvent();\n"
                "}\n"
                "\n"
                "void Document::NotifyChangeChildren(\n"
                "    const ContainerNode& container,\n"
                "    const ContainerNode::ChildrenChange& change) {\n"
                f"{INTEGRATE.BLINK_DOCUMENT_MUTATION_HOOK}"
                "  NotifySelection();\n"
                "}\n",
                encoding="utf-8",
            )

            mutation_path = root / "mutation_observer.cc"
            mutation_path.write_text(
                '#include "third_party/blink/renderer/core/dom/mutation_observer.h"\n'
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                '#include "third_party/blink/renderer/core/dom/node.h"\n'
                '#include "third_party/blink/renderer/core/dom/node_traversal.h"\n'
                "\n"
                "class MutationObserverAgentData {\n"
                " public:\n"
                "  void Trace(Visitor* visitor) const override {\n"
                "    visitor->Trace(active_mutation_observers_);\n"
                f"{INTEGRATE.BLINK_MUTATION_AGENT_TRACE_HOOK}"
                "    visitor->Trace(active_slot_change_list_);\n"
                "  }\n"
                "\n"
                f"{INTEGRATE.BLINK_MUTATION_AGENT_METHOD}"
                "  void ActivateObserver(MutationObserver* observer) {\n"
                "    active_mutation_observers_.insert(observer);\n"
                "  }\n"
                "\n"
                "  void EnsureEnqueueMicrotask() {\n"
                "    if (active_mutation_observers_.empty() &&\n"
                "        active_slot_change_list_.empty() &&\n"
                "        recorder_mutated_documents_.empty()) {\n"
                "      Enqueue();\n"
                "    }\n"
                "  }\n"
                "\n"
                "  void DeliverMutations() {\n"
                "    MutationObserverVector observers(active_mutation_observers_);\n"
                f"{INTEGRATE.BLINK_POST_MUTATION_DOM_CHECKPOINT_HOOK}"
                "    active_mutation_observers_.clear();\n"
                "    SlotChangeList slots;\n"
                "    slots.swap(active_slot_change_list_);\n"
                "    for (const auto& observer : observers)\n"
                "      observer->Deliver();\n"
                "    for (const auto& slot : slots)\n"
                "      slot->DispatchSlotChangeEvent();\n"
                f"{INTEGRATE.LEGACY_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK}"
                "  }\n"
                "\n"
                " private:\n"
                f"{INTEGRATE.BLINK_MUTATION_AGENT_MEMBER}"
                "  MutationObserverSet active_mutation_observers_;\n"
                "  SlotChangeList active_slot_change_list_;\n"
                "};\n"
                "\n"
                f"{INTEGRATE.BLINK_MUTATION_OBSERVER_METHOD}"
                "// static\n"
                "void MutationObserver::EnqueueSlotChange(HTMLSlotElement& slot) {\n"
                "  Enqueue(slot);\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_blink_document(document_path)
            INTEGRATE.patch_blink_mutation_observer(mutation_path)
            document_first = document_path.read_text(encoding="utf-8")
            mutation_first = mutation_path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_document(document_path)
            INTEGRATE.patch_blink_mutation_observer(mutation_path)

            self.assertEqual(
                document_first,
                document_path.read_text(encoding="utf-8"),
            )
            self.assertEqual(
                mutation_first,
                mutation_path.read_text(encoding="utf-8"),
            )

            self.assertNotIn(
                INTEGRATE.LEGACY_BLINK_DOM_CHECKPOINT_HOOK,
                document_first,
            )
            self.assertIn(INTEGRATE.BLINK_DOM_CHECKPOINT_HOOK, document_first)
            self.assertEqual(1, document_first.count("BeginBlinkDomCheckpoint"))
            self.assertEqual(
                1,
                document_first.count("RecordBlinkDomCheckpointNode("),
            )
            self.assertEqual(
                1,
                document_first.count("CompleteBlinkDomCheckpoint"),
            )
            self.assertIn("Token().ToString()", document_first)

            self.assertNotIn(
                INTEGRATE.LEGACY_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
                mutation_first,
            )
            self.assertIn(
                INTEGRATE.BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
                mutation_first,
            )
            # The post-mutation checkpoint is recorded by the helper that
            # document.cc defines, so the delivery hook only calls it.
            self.assertEqual(0, mutation_first.count("BeginBlinkDomCheckpoint"))
            self.assertEqual(
                1,
                mutation_first.count(
                    'RecorderRecordDomCheckpoint(*recorder_document, '
                    '"post-mutation")'
                ),
            )
            self.assertIn(
                INTEGRATE.BLINK_DOM_CHECKPOINT_DECLARATION, mutation_first
            )

            self.assertEqual(
                1,
                document_first.count("RecordBlinkDomCheckpointNodeAttribute("),
            )
            self.assertIn(
                "kRecorderMaximumDomAttributesPerNode = 64", document_first
            )
            self.assertIn(
                "kRecorderMaximumDomValueLength = 4096", document_first
            )
            self.assertIn("recorder_attributes_truncated", document_first)
            for patched in (document_first, mutation_first):
                self.assertIn(
                    '#include "third_party/blink/renderer/core/dom/attribute.h"',
                    patched,
                )
                self.assertIn(
                    '#include "third_party/blink/renderer/core/dom/element.h"',
                    patched,
                )


    def document_source(self, helper=""):
        return (
            '#include "third_party/blink/renderer/core/dom/document.h"\n'
            f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
            "\n"
            f"{helper}"
            "void Document::FinishedParsing() {\n"
            "  DocumentParserTiming::From(*this).MarkParserStop();\n"
            "\n"
            "}\n"
            "\n"
            "void Document::NotifyChangeChildren(\n"
            "    const ContainerNode& container,\n"
            "    const ContainerNode::ChildrenChange& change) {\n"
            "}\n"
        )

    def test_dom_checkpoints_are_followed_by_an_interaction_checkpoint(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "document.cc"
            path.write_text(self.document_source(), encoding="utf-8")
            INTEGRATE.patch_blink_document(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_document(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        self.assertEqual(
            1, first.count(INTEGRATE.BLINK_INTERACTION_CHECKPOINT_HELPER)
        )
        self.assertEqual(1, first.count(INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER))
        for include in INTEGRATE.BLINK_INTERACTION_CHECKPOINT_INCLUDES:
            with self.subTest(include=include):
                self.assertEqual(1, first.count(include))
        complete = first.index("a11y_recorder::CompleteBlinkDomCheckpoint(")
        call = first.index(
            '"browser.dom", recorder_reason);', complete
        )
        self.assertLess(complete, call)
        # The helper is defined at blink scope, after the DOM helper that
        # declares it and before the function it is inserted ahead of.
        self.assertLess(
            first.index(INTEGRATE.BLINK_INTERACTION_CHECKPOINT_HELPER),
            first.index("void Document::FinishedParsing() {"),
        )

    def test_upgrades_a_dom_helper_without_the_interaction_checkpoint(self):
        legacy = INTEGRATE.LEGACY_SHADOW_TREE_BLINK_DOM_CHECKPOINT_HELPER
        self.assertNotIn("RecorderRecordInteractionCheckpoint", legacy)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "document.cc"
            path.write_text(self.document_source(legacy), encoding="utf-8")
            INTEGRATE.patch_blink_document(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_document(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        self.assertNotIn(legacy, first)
        self.assertEqual(1, first.count(INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER))
        self.assertEqual(
            1, first.count(INTEGRATE.BLINK_INTERACTION_CHECKPOINT_HELPER)
        )

    def test_the_interaction_checkpoint_never_forces_work(self):
        helper = INTEGRATE.BLINK_INTERACTION_CHECKPOINT_HELPER
        for forcing_call in (
            "UpdateStyleAndLayout",
            "EnsureComputedStyle",
            "UpdateLifecycle",
            "UpdateLayout",
            "ComputeVisibleSelection",
            "UpdateIfNeeded",
            "RecalcAssignment",
            "AssignedNodes()",
            "GetBoundingClientRect",
            "FlatTreeTraversal",
        ):
            with self.subTest(call=forcing_call):
                self.assertNotIn(forcing_call, helper)
        # Only the helper-owned constants bound the snapshot.
        self.assertIn("kRecorderMaximumInteractionTextControls = 512", helper)
        self.assertIn("kRecorderMaximumInteractionValueLength = 4096", helper)

    def test_migrates_light_tree_dom_checkpoint_hooks(self):
        """A checkpoint that recorded the light tree only must be replaced."""
        with tempfile.TemporaryDirectory() as directory:
            document_path = Path(directory) / "document.cc"
            document_path.write_text(
                '#include "third_party/blink/renderer/core/dom/document.h"\n'
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                '#include "third_party/blink/renderer/core/dom/node_traversal.h"\n'
                "\n"
                "void Document::FinishedParsing() {\n"
                "  DocumentParserTiming::From(*this).MarkParserStop();\n"
                "\n"
                f"{INTEGRATE.LEGACY_LIGHT_TREE_BLINK_DOM_CHECKPOINT_HOOK}"
                "\n"
                "}\n"
                "\n"
                "void Document::NotifyChangeChildren(\n"
                "    const ContainerNode& container,\n"
                "    const ContainerNode::ChildrenChange& change) {\n"
                "}\n",
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_document(document_path)
            document_first = document_path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_document(document_path)
            self.assertEqual(
                document_first, document_path.read_text(encoding="utf-8")
            )
            self.assertNotIn(
                INTEGRATE.LEGACY_LIGHT_TREE_BLINK_DOM_CHECKPOINT_HOOK,
                document_first,
            )
            self.assertEqual(
                1, document_first.count(INTEGRATE.BLINK_DOM_CHECKPOINT_HOOK)
            )
            self.assertEqual(
                1, document_first.count("a11y_recorder::BeginBlinkDomCheckpoint(")
            )

            mutation_path = Path(directory) / "mutation_observer.cc"
            mutation_path.write_text(
                '#include "third_party/blink/renderer/core/dom/mutation_observer.h"\n'
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                '#include "third_party/blink/renderer/core/dom/node.h"\n'
                '#include "third_party/blink/renderer/core/dom/node_traversal.h"\n'
                "\n"
                "class MutationObserverAgentData {\n"
                "  void DeliverMutations() {\n"
                "    HeapHashSet<Member<Document>> recorder_mutated_documents;\n"
                "    for (const auto& slot : slots)\n"
                "      slot->DispatchSlotChangeEvent();\n"
                f"{INTEGRATE.LEGACY_LIGHT_TREE_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK}"
                "  }\n"
                "};\n",
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_mutation_observer(mutation_path)
            mutation_first = mutation_path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_mutation_observer(mutation_path)
            self.assertEqual(
                mutation_first, mutation_path.read_text(encoding="utf-8")
            )
            self.assertNotIn(
                INTEGRATE.LEGACY_LIGHT_TREE_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
                mutation_first,
            )
            self.assertIn(
                INTEGRATE.BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
                mutation_first,
            )
            self.assertNotIn("BeginBlinkDomCheckpoint", mutation_first)

    def test_patches_element_attribute_mutations_idempotently(self):
        """Attribute transitions must be recorded once per mutation path."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "element.cc"
            path.write_text(
                '#include "third_party/blink/renderer/core/dom/element.h"\n'
                "\n"
                "void Element::DidAddAttribute(const QualifiedName& name,\n"
                "                              const AtomicString& value) {\n"
                "  AttributeChangedWithInvalidations(\n"
                "      AttributeModificationParams(name, g_null_atom, value));\n"
                "  probe::DidModifyDOMAttr(this, name, value);\n"
                "}\n"
                "\n"
                "void Element::DidModifyAttribute(const QualifiedName& name,\n"
                "                                 const AtomicString& old_value,\n"
                "                                 const AtomicString& new_value,\n"
                "                                 AttributeModificationReason reason) {\n"
                "  probe::DidModifyDOMAttr(this, name, new_value);\n"
                "}\n"
                "\n"
                "void Element::DidRemoveAttribute(const QualifiedName& name,\n"
                "                                 const AtomicString& old_value) {\n"
                "  probe::DidRemoveDOMAttr(this, name);\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_blink_element(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_element(path)

            self.assertEqual(first, path.read_text(encoding="utf-8"))
            self.assertEqual(
                1, first.count("RecordBlinkDomAttributeChanged(")
            )
            self.assertEqual(
                4, first.count("RecordRecorderElementAttributeMutation")
            )
            self.assertIn(
                "RecordRecorderElementAttributeMutation("
                "*this, name, g_null_atom, value);",
                first,
            )
            self.assertIn(
                "RecordRecorderElementAttributeMutation("
                "*this, name, old_value, new_value);",
                first,
            )
            self.assertIn(
                "RecordRecorderElementAttributeMutation("
                "*this, name, old_value, g_null_atom);",
                first,
            )
            self.assertIn(
                "MutationObserver::EnqueueRecorderDomCheckpoint("
                "recorder_document);",
                first,
            )
            self.assertIn(
                '#include "third_party/blink/renderer/core/dom/'
                'mutation_observer.h"',
                first,
            )
            # The helper must be defined before the first call site, because it
            # is a file-local function rather than a declared symbol.
            self.assertLess(
                first.index("static void RecordRecorderElementAttributeMutation"),
                first.index("void Element::DidAddAttribute"),
            )

    def test_patches_character_data_mutations_idempotently(self):
        """Text transitions must exclude parse-time updates."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "character_data.cc"
            path.write_text(
                '#include "third_party/blink/renderer/core/dom/'
                'character_data.h"\n'
                "\n"
                "void CharacterData::SetDataAndUpdate(const String& new_data,\n"
                "                                     const TextDiffRange& diff,\n"
                "                                     UpdateSource source) {\n"
                "  String old_data = this->data();\n"
                "  diff.CheckValid(old_data, new_data);\n"
                "  SetDataWithoutUpdate(new_data);\n"
                "}\n",
                encoding="utf-8",
            )

            INTEGRATE.patch_blink_character_data(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_character_data(path)

            self.assertEqual(first, path.read_text(encoding="utf-8"))
            self.assertEqual(
                1, first.count("RecordBlinkDomCharacterDataChanged(")
            )
            self.assertIn("if (source != kUpdateFromParser) {", first)
            self.assertIn("kRecorderMaximumDomValueLength = 4096", first)
            self.assertIn(
                "MutationObserver::EnqueueRecorderDomCheckpoint("
                "recorder_document);",
                first,
            )
            for include in (
                '#include "third_party/blink/renderer/core/dom/document.h"',
                '#include "third_party/blink/renderer/core/dom/'
                'mutation_observer.h"',
            ):
                self.assertEqual(1, first.count(include))
            # The recorded text must be bounded before it reaches the bridge.
            self.assertIn(
                "new_data.substr(0, kRecorderMaximumDomValueLength)", first
            )
            self.assertIn(
                "old_data.substr(0, kRecorderMaximumDomValueLength)", first
            )

    def test_migrates_node_only_event_target_hooks(self):
        """A checkout patched before non-Node targets were recorded upgrades.

        The presence guards in the integration script key on symbol names, so a
        hook body that only ever reported a Node reads as already integrated.
        The historical bodies are replaced explicitly, and the result is the
        body the script writes into a fresh checkout.
        """
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_target.cc"
            self._write_event_target(path)
            INTEGRATE.patch_blink_event_target(path)
            current = path.read_text(encoding="utf-8")

            node_only = (
                current.replace(
                    INTEGRATE.BLINK_LISTENER_HOOK,
                    HISTORICAL_LISTENER_HOOK_NODE_ONLY,
                    1,
                )
                .replace(
                    INTEGRATE.BLINK_LISTENER_REMOVED_HOOK,
                    HISTORICAL_LISTENER_REMOVED_HOOK_NODE_ONLY,
                    1,
                )
                .replace(
                    INTEGRATE.BLINK_LISTENER_INVOCATION_STARTED_HOOK,
                    INTEGRATE
                    .LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK_NODE_ONLY,
                    1,
                )
            )
            self.assertNotEqual(current, node_only)
            path.write_text(node_only, encoding="utf-8")

            INTEGRATE.patch_blink_event_target(path)

            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_reports_how_each_listener_registration_was_created(self):
        """Every registration carries the form Blink created it from.

        Blink funnels an addEventListener call, an on-event attribute
        assignment, and an inline content attribute through one registration
        path, so the form is read from the listener object rather than from the
        call site.
        """
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_target.cc"
            self._write_event_target(path)
            INTEGRATE.patch_blink_event_target(path)
            patched = path.read_text(encoding="utf-8")

            self.assertEqual(
                1,
                patched.count(
                    "const char* RecorderListenerRegistrationKind("
                ),
            )
            self.assertIn(
                '#include "third_party/blink/renderer/core/dom/events/'
                'event_listener.h"',
                patched,
            )
            self.assertIn(
                "listener->IsEventHandlerForContentAttribute()", patched
            )
            self.assertIn(
                "a11y_recorder::kListenerRegistrationKindInlineAttribute",
                patched,
            )
            self.assertIn(
                "a11y_recorder::kListenerRegistrationKindEventHandlerProperty",
                patched,
            )
            self.assertIn(
                "a11y_recorder::kListenerRegistrationKindAddEventListener",
                patched,
            )
            self.assertEqual(
                3, patched.count("recorder_registration_kind")
                - patched.count("const char* recorder_registration_kind")
            )

    def test_reports_where_each_listener_record_came_from(self):
        """Every listener record carries the location Blink reports for it.

        The location is captured from Blink's own capture helper at the hook, so
        it describes the call that registered, removed, or replaced the
        listener. It does not describe where the callback function was defined,
        and it is absent when no script was running.
        """
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_target.cc"
            self._write_event_target(path)
            INTEGRATE.patch_blink_event_target(path)
            patched = path.read_text(encoding="utf-8")

            for include in (
                INTEGRATE.BLINK_CAPTURE_SOURCE_LOCATION_INCLUDE,
                INTEGRATE.BLINK_SOURCE_LOCATION_INCLUDE,
            ):
                self.assertEqual(1, patched.count(include), include)

            # One capture per listener record, and none of them assumes an
            # execution context is present.
            self.assertEqual(
                3, patched.count("CaptureSourceLocation(recorder_context)")
            )
            self.assertEqual(
                3,
                patched.count(
                    "recorder_context ? CaptureSourceLocation("
                    "recorder_context) : nullptr"
                ),
            )
            for accessor in ("Url()", "Function()", "ScriptId()",
                             "LineNumber()", "ColumnNumber()"):
                self.assertEqual(
                    3,
                    patched.count(f"recorder_location->{accessor}"),
                    accessor,
                )

            INTEGRATE.patch_blink_event_target(path)
            self.assertEqual(patched, path.read_text(encoding="utf-8"))

    def test_migrates_a_listener_hook_that_reported_no_location(self):
        """A checkout patched before locations were recorded upgrades.

        The registration hook already existed with its registration kind, so
        the presence guard reads it as integrated. The region the hook
        introduces is replaced, so the earlier body converges on the current
        one and starts reporting a location.
        """
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_target.cc"
            self._write_event_target(path)
            INTEGRATE.patch_blink_event_target(path)
            current = path.read_text(encoding="utf-8")

            without_location = current.replace(
                INTEGRATE.BLINK_LISTENER_HOOK,
                HISTORICAL_LISTENER_HOOK_WITHOUT_LOCATION,
                1,
            )
            self.assertNotEqual(current, without_location)
            self.assertNotIn(
                "recorder_location", HISTORICAL_LISTENER_HOOK_WITHOUT_LOCATION
            )
            path.write_text(without_location, encoding="utf-8")

            INTEGRATE.patch_blink_event_target(path)

            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_reports_the_world_each_listener_callback_belongs_to(self):
        """Every listener record names the world its callback came from.

        The world is read from the callback object, so it is the world the
        registration was made from rather than whichever world was current when
        the record was written. A listener Blink installed itself is not script
        based and reports no world. Blink checks that the name accessors are
        never called for the main world, so each call is guarded.
        """
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_target.cc"
            self._write_event_target(path)
            INTEGRATE.patch_blink_event_target(path)
            patched = path.read_text(encoding="utf-8")

            for include in (
                INTEGRATE.BLINK_JS_BASED_EVENT_LISTENER_INCLUDE,
                INTEGRATE.BLINK_DOM_WRAPPER_WORLD_INCLUDE,
            ):
                self.assertEqual(1, patched.count(include), include)

            # One helper, and one world read per listener record.
            self.assertEqual(
                1, patched.count("const DOMWrapperWorld* RecorderListenerWorld(")
            )
            self.assertEqual(
                1, patched.count("const char* RecorderExecutionWorldKind(")
            )
            self.assertEqual(
                3, patched.count("RecorderListenerWorld(recorder_callback)")
            )
            self.assertEqual(
                3,
                patched.count(
                    "recorder_world ? RecorderExecutionWorldKind(*recorder_world)"
                ),
            )
            self.assertEqual(
                3,
                patched.count(
                    "a11y_recorder::kExecutionWorldIdUnobserved"
                ),
            )

            # Blink DCHECKs that neither name accessor is called for the main
            # world, so no call may be reached with only a null check.
            for accessor in (
                "NonMainWorldHumanReadableName()",
                "NonMainWorldStableId()",
            ):
                self.assertEqual(
                    3, patched.count(f"recorder_world->{accessor}"), accessor
                )
            self.assertEqual(
                6,
                patched.count(
                    "recorder_world && !recorder_world->IsMainWorld()"
                ),
            )

            # The inspector's worlds are classified before isolated worlds
            # generally, because Blink reports both as isolated.
            self.assertLess(
                patched.index("WorldType::kInspectorIsolated"),
                patched.index("world.IsIsolatedWorld()"),
            )

            INTEGRATE.patch_blink_event_target(path)
            self.assertEqual(patched, path.read_text(encoding="utf-8"))

    def test_migrates_a_listener_hook_that_reported_no_world(self):
        """A checkout patched before worlds were recorded upgrades.

        The registration hook and the registration-kind helper already existed,
        so both presence guards read the checkout as integrated. The hook region
        is replaced and the world helper has its own guard, so an earlier
        checkout converges on the current body instead of calling a helper it
        does not define.
        """
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_target.cc"
            self._write_event_target(path)
            INTEGRATE.patch_blink_event_target(path)
            current = path.read_text(encoding="utf-8")

            without_world = current.replace(
                INTEGRATE.BLINK_LISTENER_HOOK,
                HISTORICAL_LISTENER_HOOK_WITHOUT_WORLD,
                1,
            ).replace(INTEGRATE.BLINK_LISTENER_WORLD_HELPER, "", 1)
            self.assertNotEqual(current, without_world)
            self.assertNotIn("recorder_world", HISTORICAL_LISTENER_HOOK_WITHOUT_WORLD)
            self.assertIn("RecorderListenerRegistrationKind(", without_world)
            self.assertNotIn(
                "const DOMWrapperWorld* RecorderListenerWorld(", without_world
            )
            path.write_text(without_world, encoding="utf-8")

            INTEGRATE.patch_blink_event_target(path)

            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_records_a_replaced_attribute_listener_callback(self):
        """Reassigning an on-event attribute replaces the callback in place.

        Blink returns from SetAttributeEventListener after swapping the
        callback of an existing registration, so neither the add hook nor the
        remove hook runs and the recorded form would keep describing a callback
        Blink no longer holds.
        """
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_target.cc"
            self._write_event_target(path)
            INTEGRATE.patch_blink_event_target(path)
            patched = path.read_text(encoding="utf-8")

            self.assertEqual(
                1,
                patched.count(
                    "a11y_recorder::RecordBlinkListenerCallbackReplaced("
                ),
            )
            self.assertIn(
                "    registered_listener->SetCallback(listener);\n    {\n",
                patched,
            )
            self.assertIn(
                "const EventListener* recorder_callback = listener;", patched
            )

            INTEGRATE.patch_blink_event_target(path)
            self.assertEqual(patched, path.read_text(encoding="utf-8"))

    def test_migrates_a_listener_hook_body_it_never_wrote(self):
        """An unrecognized hook body still converges on the current one.

        A hook is inserted only when its symbol is absent, so a checkout
        patched by an earlier revision keeps that revision's body. The region
        the hook introduces is replaced rather than a remembered copy of its
        text, so a body this script has no record of is migrated too.
        """
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_target.cc"
            self._write_event_target(path)
            INTEGRATE.patch_blink_event_target(path)
            current = path.read_text(encoding="utf-8")

            invented = current.replace(
                INTEGRATE.BLINK_LISTENER_HOOK,
                "    {\n"
                "      a11y_recorder::RecordBlinkListenerRegistered(\n"
                "          reinterpret_cast<uintptr_t>(registered_listener));\n"
                "    }\n",
                1,
            )
            self.assertNotEqual(current, invented)
            path.write_text(invented, encoding="utf-8")

            INTEGRATE.patch_blink_event_target(path)

            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_refuses_to_migrate_a_region_that_is_not_a_hook(self):
        """A bridge call in a Blink function body is not treated as a hook.

        The migration replaces the whole block that contains the call, so it
        must refuse a block it did not introduce rather than delete Blink code.
        """
        source = (
            "bool EventTarget::AddEventListenerInternal() {\n"
            "  a11y_recorder::RecordBlinkListenerRegistered(0);\n"
            "  return true;\n"
            "}\n"
        )

        with self.assertRaises(RuntimeError) as raised:
            INTEGRATE.migrate_hook_region(
                source,
                Path("event_target.cc"),
                "RecordBlinkListenerRegistered",
                INTEGRATE.BLINK_LISTENER_HOOK,
            )

        self.assertIn("does not open a hook region", str(raised.exception))

    def test_migrates_a_dispatch_path_that_omitted_the_window(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_dispatcher.cc"
            self._write_event_dispatcher(path)
            INTEGRATE.patch_blink_event_dispatcher(path)
            current = path.read_text(encoding="utf-8")

            without_window = current.replace(
                INTEGRATE.BLINK_DISPATCH_HOOK,
                INTEGRATE.LEGACY_BLINK_DISPATCH_HOOK_WITHOUT_WINDOW,
                1,
            )
            self.assertNotIn("RecordBlinkDispatchPathWindow", without_window)
            path.write_text(without_window, encoding="utf-8")

            INTEGRATE.patch_blink_event_dispatcher(path)

            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_migrates_a_dispatch_path_that_omitted_the_tree_scopes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_dispatcher.cc"
            self._write_event_dispatcher(path)
            INTEGRATE.patch_blink_event_dispatcher(path)
            current = path.read_text(encoding="utf-8")

            without_scopes = current.replace(
                INTEGRATE.BLINK_DISPATCH_HOOK,
                INTEGRATE.LEGACY_BLINK_DISPATCH_HOOK_WITHOUT_SCOPES,
                1,
            )
            self.assertNotIn("EnsureEventPath", without_scopes)
            path.write_text(without_scopes, encoding="utf-8")

            INTEGRATE.patch_blink_event_dispatcher(path)

            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_dispatch_path_records_each_entry_tree_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_dispatcher.cc"
            self._write_event_dispatcher(path)
            INTEGRATE.patch_blink_event_dispatcher(path)
            patched = path.read_text(encoding="utf-8")
        for include in INTEGRATE.BLINK_DISPATCH_SCOPE_INCLUDES:
            self.assertEqual(1, patched.count(include + "\n"))
        hook = INTEGRATE.BLINK_DISPATCH_HOOK
        # The retargeted targets and the visible path are read from Blink's
        # per-scope context, the same data composedPath() returns.
        self.assertIn("recorder_context.Target()", hook)
        self.assertIn("recorder_context.RelatedTarget()", hook)
        self.assertIn("EnsureEventPath(recorder_event_path)", hook)
        self.assertIn("recorder_window_context.RelatedTarget()", hook)
        self.assertIn("TopNodeEventContext()", hook)
        self.assertIn("recorder_event_path.IsEmpty()", hook)

    def test_exported_data_declarations_do_not_hide_a_signature(self):
        header = (
            "namespace a11y_recorder {\n"
            "\n"
            "COMPONENT_EXPORT(RECORDER_BRIDGE)\n"
            "extern const char kEventTargetKindNode[];\n"
            "\n"
            "COMPONENT_EXPORT(RECORDER_BRIDGE)\n"
            "void RecordBlinkListenerRegistered(uintptr_t listener_identity,\n"
            "                                   std::string target_kind);\n"
            "\n"
            "}  // namespace a11y_recorder\n"
        )
        self.assertEqual(
            {"RecordBlinkListenerRegistered": 2},
            INTEGRATE.parse_bridge_signatures(header),
        )

    def _write_event_target(self, path: Path) -> None:
        path.write_text(
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
            "bool EventTarget::SetAttributeEventListener("
            "const AtomicString& event_type,\n"
            "                                            "
            "EventListener* listener) {\n"
            "  RegisteredEventListener* registered_listener =\n"
            "      GetAttributeRegisteredEventListener(event_type);\n"
            "  if (!listener) {\n"
            "    if (registered_listener)\n"
            "      removeEventListener(event_type, "
            "registered_listener->Callback(), false);\n"
            "    return false;\n"
            "  }\n"
            "  if (registered_listener) {\n"
            "    registered_listener->SetCallback(listener);\n"
            "    return true;\n"
            "  }\n"
            "  return addEventListener(event_type, listener, false);\n"
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
            "    // The listener will be retained by Member<EventListener> in "
            "the\n"
            "    // registeredListener, i and size are updated with the firing "
            "event iterator\n"
            "    // in case the listener is removed from the listener vector "
            "below.\n"
            "    if (registered_listener->Once()) {\n"
            "      removeEventListener(event.type(), listener,\n"
            "                          registered_listener->Capture());\n"
            "    }\n"
            "    event.SetHandlingPassive("
            "EventPassiveMode(*registered_listener));\n"
            "\n"
            "    probe::UserCallback probe(context, nullptr, event.type(), "
            "false, this);\n"
            "\n"
            "    // To match Mozilla, the AT_TARGET phase fires both capturing "
            "and bubbling\n"
            "    // event listeners, even though that violates some versions "
            "of the DOM spec.\n"
            "    listener->Invoke(context, &event);\n"
            "    fired_listener = true;\n"
            "}\n",
            encoding="utf-8",
        )

    def _write_event_dispatcher(self, path: Path) -> None:
        path.write_text(
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
            "}\n"
            "\n"
            "inline void EventDispatcher::DispatchEventPostProcess() {\n"
            "  bool is_trusted_or_click = true;\n"
            "  if (!event_->defaultPrevented() && !event_->DefaultHandled() "
            "&&\n"
            "      is_trusted_or_click) {\n"
            "    node_->DefaultEventHandler(*event_);\n"
            "    if (!event_->DefaultHandled() && "
            "!event_->defaultPrevented() &&\n"
            "        event_->bubbles()) {\n"
            "      wtf_size_t size = event_->GetEventPath().size();\n"
            "      for (wtf_size_t i = 1; i < size; ++i) {\n"
            "        event_->GetEventPath()[i].GetNode()."
            "DefaultEventHandler(*event_);\n"
            "        if (event_->DefaultHandled() || "
            "event_->defaultPrevented()) {\n"
            "          break;\n"
            "        }\n"
            "      }\n"
            "    }\n"
            "  } else {\n"
            "#if BUILDFLAG(IS_MAC)\n"
            "#endif\n"
            "  }\n"
            "}\n",
            encoding="utf-8",
        )

    def test_parses_declared_bridge_signatures(self):
        header = (
            "namespace a11y_recorder {\n"
            "\n"
            "// Records a checkpoint, counting three parameters.\n"
            "COMPONENT_EXPORT(RECORDER_BRIDGE)\n"
            "void BeginBlinkDomCheckpoint(int document_node_id,\n"
            "                             std::string document_token,\n"
            "                             int checkpoint_id);\n"
            "\n"
            "COMPONENT_EXPORT(RECORDER_BRIDGE)\n"
            "RecorderPipeClient* GetProcessRecorderClient();\n"
            "\n"
            "}  // namespace a11y_recorder\n"
        )
        self.assertEqual(
            {"BeginBlinkDomCheckpoint": 3, "GetProcessRecorderClient": 0},
            INTEGRATE.parse_bridge_signatures(header),
        )

    def test_counts_call_arguments_without_splitting_literals(self):
        text = (
            "  a11y_recorder::RecordBlinkDomCheckpointNode(\n"
            "      document->GetDomNodeId(), MakeName(a, b), \"text, more\",\n"
            "      static_cast<int>(node.getNodeType()));\n"
        )
        self.assertEqual(
            (("RecordBlinkDomCheckpointNode", 4, 1),),
            INTEGRATE.bridge_call_arities(text),
        )

    def test_partial_call_templates_are_not_counted(self):
        text = "  a11y_recorder::RecordBlinkDispatchStarted(\n      first,\n"
        self.assertEqual((), INTEGRATE.bridge_call_arities(text))

    def test_current_hook_templates_match_the_bridge_header(self):
        header = (
            INTEGRATE.Path(INTEGRATE.__file__).resolve().parent
            / "recorder_bridge"
            / "browser_bridge.h"
        )
        signatures = INTEGRATE.parse_bridge_signatures(
            header.read_text(encoding="utf-8")
        )
        INTEGRATE.verify_hook_templates(signatures)

    def test_hook_template_verification_reports_superseded_call_shapes(self):
        signatures = {"BeginBlinkDomCheckpoint": 4}
        problems = INTEGRATE.describe_signature_mismatches(
            "BLINK_DOM_CHECKPOINT_HOOK",
            "  a11y_recorder::BeginBlinkDomCheckpoint(a, b, c);\n",
            signatures,
        )
        self.assertEqual(1, len(problems))
        self.assertIn("called with 3 arguments", problems[0])
        self.assertIn("bridge declares 4", problems[0])

    def test_hook_template_verification_reports_unknown_entry_points(self):
        problems = INTEGRATE.describe_signature_mismatches(
            "SOME_HOOK",
            "  a11y_recorder::RecordSomethingRemoved(a);\n",
            {"BeginBlinkDomCheckpoint": 4},
        )
        self.assertEqual(1, len(problems))
        self.assertIn("unknown recorder bridge entry point", problems[0])

    def test_patched_sources_are_verified_against_the_bridge(self):
        with tempfile.TemporaryDirectory() as directory:
            stale = Path(directory) / "mutation_observer.cc"
            stale.write_text(
                "void MutationObserver::Deliver() {\n"
                "  a11y_recorder::BeginBlinkDomCheckpoint(\n"
                "      document->GetDomNodeId(), checkpoint_id, 512);\n"
                "}\n",
                encoding="utf-8",
            )
            INTEGRATE._INTEGRATED_PATHS.clear()
            INTEGRATE.read_source(stale)
            try:
                with self.assertRaises(RuntimeError) as failure:
                    INTEGRATE.verify_integrated_sources(
                        {"BeginBlinkDomCheckpoint": 4}
                    )
            finally:
                INTEGRATE._INTEGRATED_PATHS.clear()
        message = str(failure.exception)
        self.assertIn("mutation_observer.cc:2", message)
        self.assertIn("called with 3 arguments", message)

    def test_superseded_templates_must_be_wired_into_a_migration(self):
        unused = "LEGACY_UNUSED_DOM_CHECKPOINT_HOOK"
        setattr(
            INTEGRATE,
            unused,
            "  a11y_recorder::BeginBlinkDomCheckpoint(a, b, c);\n",
        )
        try:
            with self.assertRaises(RuntimeError) as failure:
                INTEGRATE.verify_hook_templates(
                    {"BeginBlinkDomCheckpoint": 4}
                )
        finally:
            delattr(INTEGRATE, unused)
        self.assertIn(unused, str(failure.exception))
        self.assertIn("never used to upgrade", str(failure.exception))


    def test_verification_catches_a_hook_a_presence_guard_skipped(self):
        """A stale hook with no registered template must still be reported."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "mutation_observer.cc"
            path.write_text(
                '#include "third_party/blink/renderer/core/dom/'
                'mutation_observer.h"\n'
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                '#include "third_party/blink/renderer/core/dom/node.h"\n'
                '#include "third_party/blink/renderer/core/dom/'
                'node_traversal.h"\n'
                "\n"
                "class MutationObserverAgentData;\n"
                "void MutationObserver::DeliverMutations() {\n"
                "  for (auto& document : recorder_mutated_documents) {\n"
                "    a11y_recorder::BeginBlinkDomCheckpoint(\n"
                "        recorder_node_id, recorder_checkpoint, 512);\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )
            header = (
                MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h"
            )
            signatures = INTEGRATE.parse_bridge_signatures(
                header.read_text(encoding="utf-8")
            )
            INTEGRATE._INTEGRATED_PATHS.clear()
            try:
                INTEGRATE.patch_blink_mutation_observer(path)
                with self.assertRaises(RuntimeError) as failure:
                    INTEGRATE.verify_integrated_sources(signatures)
            finally:
                INTEGRATE._INTEGRATED_PATHS.clear()
        message = str(failure.exception)
        # The helper declaration adds four lines ahead of the stale call.
        self.assertIn("mutation_observer.cc:15", message)
        self.assertIn("BeginBlinkDomCheckpoint", message)
        self.assertIn("called with 3 arguments", message)



    def test_migrates_uncompilable_string_truncation_bodies(self):
        """A patched body the presence guard would skip must be replaced."""
        element_source = (
            '#include "third_party/blink/renderer/core/dom/element.h"\n'
            f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
            '#include "third_party/blink/renderer/core/dom/'
            'mutation_observer.h"\n'
            "\n"
            f"{INTEGRATE.INTERMEDIATE_BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER}"
            "\n"
            "void Element::DidAddAttribute(const QualifiedName& name,\n"
            "                              const AtomicString& value) {\n"
            f"{INTEGRATE.BLINK_ELEMENT_ATTRIBUTE_ADDED_HOOK}"
            "  AttributeChanged();\n"
            "}\n"
        )
        character_data_source = (
            '#include "third_party/blink/renderer/core/dom/character_data.h"\n'
            f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
            '#include "third_party/blink/renderer/core/dom/document.h"\n'
            '#include "third_party/blink/renderer/core/dom/'
            'mutation_observer.h"\n'
            "\n"
            "void CharacterData::SetDataAndUpdate(const String& new_data,\n"
            "                                     const TextDiffRange& diff,\n"
            "                                     UpdateSource source) {\n"
            "  String old_data = this->data();\n"
            f"{INTEGRATE.INTERMEDIATE_BLINK_CHARACTER_DATA_MUTATION_HOOK}"
            "  SetDataWithoutUpdate(new_data);\n"
            "}\n"
        )
        document_source = (
            '#include "third_party/blink/renderer/core/dom/document.h"\n'
            f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
            '#include "third_party/blink/renderer/core/dom/element.h"\n'
            '#include "third_party/blink/renderer/core/dom/attribute.h"\n'
            "\n"
            "void Document::FinishedParsing() {\n"
            "  DocumentParserTiming::From(*this).MarkParserStop();\n"
            "\n"
            f"{INTEGRATE.INTERMEDIATE_BLINK_DOM_CHECKPOINT_HOOK}"
            "\n"
            "  DispatchEvent();\n"
            "}\n"
            "\n"
            "void Document::NotifyChangeChildren(\n"
            "    const ContainerNode& container,\n"
            "    const ContainerNode::ChildrenChange& change) {\n"
            f"{INTEGRATE.BLINK_DOCUMENT_MUTATION_HOOK}"
            "  NotifySelection();\n"
            "}\n"
        )
        cases = (
            (
                "element.cc",
                element_source,
                INTEGRATE.INTERMEDIATE_BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER,
                INTEGRATE.BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER,
                INTEGRATE.patch_blink_element,
            ),
            (
                "character_data.cc",
                character_data_source,
                INTEGRATE.INTERMEDIATE_BLINK_CHARACTER_DATA_MUTATION_HOOK,
                INTEGRATE.BLINK_CHARACTER_DATA_MUTATION_HOOK,
                INTEGRATE.patch_blink_character_data,
            ),
            (
                "document.cc",
                document_source,
                INTEGRATE.INTERMEDIATE_BLINK_DOM_CHECKPOINT_HOOK,
                INTEGRATE.BLINK_DOM_CHECKPOINT_HOOK,
                INTEGRATE.patch_blink_document,
            ),
        )
        for name, source, intermediate, current, patch in cases:
            with self.subTest(source=name):
                self.assertIn(".Left(", intermediate)
                self.assertNotIn(".Left(", current)
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / name
                    path.write_text(source, encoding="utf-8")

                    patch(path)
                    first = path.read_text(encoding="utf-8")
                    patch(path)

                    self.assertEqual(
                        first, path.read_text(encoding="utf-8")
                    )
                    self.assertNotIn(".Left(", first)
                    self.assertNotIn(intermediate, first)
                    self.assertIn(current, first)


def cookie_source(*parts: str) -> str:
    """Joins anchor text into a small source that holds each part once."""
    return "\n// separator\n".join(parts)


class CookieIntegrationTests(unittest.TestCase):
    """Proves the cookie hooks are written once and hold no value read."""

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def assert_bridge_calls_match(self, text):
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches("patched", text, signatures),
        )

    def test_patches_the_cookie_jar_idempotently(self):
        source = cookie_source(
            '#include "third_party/blink/renderer/core/loader/cookie_jar.h"\n',
            "// Controls whether we apply an artificial delay to priming the\n",
            INTEGRATE.BLINK_COOKIE_JAR_WRITE_NO_URL_ANCHOR,
            INTEGRATE.BLINK_COOKIE_JAR_WRITE_SENT_ANCHOR,
            INTEGRATE.BLINK_COOKIE_JAR_READ_NO_URL_ANCHOR,
            INTEGRATE.BLINK_COOKIE_JAR_READ_FAILED_ANCHOR,
            INTEGRATE.BLINK_COOKIE_JAR_READ_RETURNED_ANCHOR,
        )
        patched = self.patch_twice(
            "cookie_jar.cc", source, INTEGRATE.patch_blink_cookie_jar
        )
        for hook in (
            INTEGRATE.BLINK_COOKIE_JAR_WRITE_NO_URL_HOOK,
            INTEGRATE.BLINK_COOKIE_JAR_WRITE_SENT_HOOK,
            INTEGRATE.BLINK_COOKIE_JAR_READ_NO_URL_HOOK,
            INTEGRATE.BLINK_COOKIE_JAR_READ_FAILED_HOOK,
            INTEGRATE.BLINK_COOKIE_JAR_READ_RETURNED_HOOK,
        ):
            self.assertEqual(1, patched.count(hook))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_BRIDGE_INCLUDE))
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_COOKIE_ORIGIN_HELPER_MARKER)
        )
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_DOCUMENT_COOKIE_HELPER_MARKER)
        )
        self.assertLess(
            patched.index(INTEGRATE.BLINK_COOKIE_ORIGIN_HELPER_MARKER),
            patched.index(INTEGRATE.BLINK_DOCUMENT_COOKIE_HELPER_MARKER),
        )
        self.assert_bridge_calls_match(patched)

    def test_patches_document_cookie_refusals_idempotently(self):
        source = cookie_source(
            INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n",
            INTEGRATE.BLINK_DOCUMENT_COOKIE_HELPER_ANCHOR,
            INTEGRATE.BLINK_DOCUMENT_COOKIE_READ_DISABLED_ANCHOR,
            INTEGRATE.BLINK_DOCUMENT_COOKIE_READ_SECURITY_ANCHOR,
            INTEGRATE.BLINK_DOCUMENT_COOKIE_WRITE_DISABLED_ANCHOR,
            INTEGRATE.BLINK_DOCUMENT_COOKIE_WRITE_SECURITY_ANCHOR,
        )
        patched = self.patch_twice(
            "document.cc", source, INTEGRATE.patch_blink_document_cookie
        )
        for hook in (
            INTEGRATE.BLINK_DOCUMENT_COOKIE_READ_DISABLED_HOOK,
            INTEGRATE.BLINK_DOCUMENT_COOKIE_READ_SECURITY_HOOK,
            INTEGRATE.BLINK_DOCUMENT_COOKIE_WRITE_DISABLED_HOOK,
            INTEGRATE.BLINK_DOCUMENT_COOKIE_WRITE_SECURITY_HOOK,
        ):
            self.assertEqual(1, patched.count(hook))
        for include in INTEGRATE.BLINK_COOKIE_ORIGIN_INCLUDES:
            self.assertEqual(1, patched.count(include))
        self.assert_bridge_calls_match(patched)

    def test_patches_the_cookie_store_idempotently(self):
        source = cookie_source(
            '#include "third_party/blink/renderer/modules/cookie_store/'
            'cookie_store.h"\n',
            INTEGRATE.BLINK_COOKIE_STORE_HELPER_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_GET_ALL_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_GET_EMPTY_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_GET_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_SET_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_DELETE_NAME_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_DELETE_OPTIONS_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_READ_ALL_RESULT_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_READ_ONE_RESULT_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_WRITE_NOTE_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_WRITE_RESULT_ANCHOR,
            INTEGRATE.BLINK_COOKIE_STORE_CHANGE_ANCHOR,
        )
        patched = self.patch_twice(
            "cookie_store.cc", source, INTEGRATE.patch_blink_cookie_store
        )
        for hook in (
            INTEGRATE.BLINK_COOKIE_STORE_GET_ALL_HOOK,
            INTEGRATE.BLINK_COOKIE_STORE_GET_EMPTY_HOOK,
            INTEGRATE.BLINK_COOKIE_STORE_GET_HOOK,
            INTEGRATE.BLINK_COOKIE_STORE_SET_HOOK,
            INTEGRATE.BLINK_COOKIE_STORE_DELETE_NAME_HOOK,
            INTEGRATE.BLINK_COOKIE_STORE_DELETE_OPTIONS_HOOK,
            INTEGRATE.BLINK_COOKIE_STORE_WRITE_NOTE_HOOK,
            INTEGRATE.BLINK_COOKIE_STORE_WRITE_RESULT_HOOK,
            INTEGRATE.BLINK_COOKIE_STORE_CHANGE_HOOK,
        ):
            self.assertEqual(1, patched.count(hook))
        self.assertEqual(
            2, patched.count(INTEGRATE.BLINK_COOKIE_STORE_READ_RESULT_HOOK)
        )
        self.assert_bridge_calls_match(patched)

    def test_a_cookie_hook_fails_when_its_anchor_is_absent(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "cookie_store.cc"
            path.write_text(
                '#include "third_party/blink/renderer/modules/cookie_store/'
                'cookie_store.h"\n'
                + INTEGRATE.BLINK_COOKIE_STORE_HELPER_ANCHOR,
                encoding="utf-8",
            )
            with self.assertRaises(RuntimeError):
                INTEGRATE.patch_blink_cookie_store(path)

    def test_patches_the_cookie_store_build_idempotently(self):
        source = (
            'blink_modules_sources("cookie_store") {\n'
            '  sources = [ "cookie_store.cc" ]\n\n'
            + INTEGRATE.BLINK_COOKIE_STORE_BUILD_DEPS
            + "}\n"
        )
        patched = self.patch_twice(
            "BUILD.gn", source, INTEGRATE.patch_blink_cookie_store_build
        )
        self.assertIn(INTEGRATE.BLINK_COOKIE_STORE_BUILD_PATCHED_DEPS, patched)

    def test_patches_browser_cookie_access_idempotently(self):
        cases = (
            (
                "render_frame_host_impl.cc",
                '#include "content/browser/renderer_host/'
                'render_frame_host_impl.h"\n',
                INTEGRATE.CONTENT_FRAME_COOKIE_HELPER_ANCHOR,
                INTEGRATE.CONTENT_FRAME_COOKIE_ACCESS_ANCHOR,
                INTEGRATE.CONTENT_FRAME_COOKIE_ACCESS_HOOK,
                INTEGRATE.patch_content_frame_cookie_access,
            ),
            (
                "navigation_request.cc",
                '#include "content/browser/renderer_host/'
                'navigation_request.h"\n',
                INTEGRATE.CONTENT_NAVIGATION_COOKIE_HELPER_ANCHOR,
                INTEGRATE.CONTENT_NAVIGATION_COOKIE_ACCESS_ANCHOR,
                INTEGRATE.CONTENT_NAVIGATION_COOKIE_ACCESS_HOOK,
                INTEGRATE.patch_content_navigation_cookie_access,
            ),
        )
        for name, include, helper_anchor, anchor, hook, patch in cases:
            with self.subTest(source=name):
                patched = self.patch_twice(
                    name,
                    cookie_source(include, helper_anchor, anchor),
                    patch,
                )
                self.assertEqual(1, patched.count(hook))
                self.assertEqual(
                    1,
                    patched.count(INTEGRATE.CONTENT_COOKIE_ACCESS_HELPER_MARKER),
                )
                self.assertLess(
                    patched.index(INTEGRATE.CONTENT_COOKIE_ACCESS_HELPER_MARKER),
                    patched.index(hook),
                )
                self.assert_bridge_calls_match(patched)

    def test_no_cookie_template_reads_a_cookie_value(self):
        names = [
            name
            for name in dir(INTEGRATE)
            if name.isupper()
            and "COOKIE" in name
            and isinstance(getattr(INTEGRATE, name), str)
        ]
        self.assertTrue(names)
        for name in names:
            with self.subTest(template=name):
                text = getattr(INTEGRATE, name)
                self.assertNotIn(".Value()", text)
                self.assertNotIn("->value()", text)
                self.assertNotIn("options->value", text)

    def test_cookie_text_readers_pass_their_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "cookie_text_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "cookie_text.cc"),
                    str(bridge / "cookie_text_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_network_header_classifier_passes_its_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "network_text_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "network_text.cc"),
                    str(bridge / "network_text_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)


class NetworkIntegrationTests(unittest.TestCase):
    """Proves the network hooks are written once and match the bridge."""

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def assert_bridge_calls_match(self, text):
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches("patched", text, signatures),
        )

    def observer_source(self, include, observer, hooks):
        return (
            f"{include}\n\nnamespace blink {{\n\n"
            f"{observer}::{observer}(\n    int) {{}}\n\n"
            + "".join(f"void F(\n{anchor}}}\n\n" for anchor, _ in hooks)
            + f"bool {observer}::InterestedInAllRequests() {{\n"
            "  return false;\n}\n\n}  // namespace blink\n"
        )

    def test_names_enumerators_in_kebab_case(self):
        cases = {
            "kCorsWithForcedPreflight": "cors-with-forced-preflight",
            "kSRIMessageSignatureMismatch": "sri-message-signature-mismatch",
            "kCSP": "csp",
            "kHttpCache": "http-cache",
            "kOmitBug_775438_Workaround": "omit-bug-775438-workaround",
            "kCoepFrameResourceNeedsCoepHeader":
                "coep-frame-resource-needs-coep-header",
        }
        for enumerator, name in cases.items():
            with self.subTest(enumerator=enumerator):
                self.assertEqual(
                    name, INTEGRATE.recorder_enum_value_name(enumerator)
                )

    def test_every_network_enumerator_has_a_distinct_name(self):
        for function, _, enumerators in INTEGRATE.BLINK_NETWORK_ENUMS:
            with self.subTest(function=function):
                names = [
                    INTEGRATE.recorder_enum_value_name(enumerator)
                    for enumerator in enumerators
                ]
                self.assertEqual(len(names), len(set(names)))
                self.assertNotIn("unknown", names)

    def test_patches_the_resource_load_observers_idempotently(self):
        cases = (
            (
                "resource_load_observer_for_frame.cc",
                '#include "third_party/blink/renderer/core/loader/'
                'resource_load_observer_for_frame.h"',
                "ResourceLoadObserverForFrame",
                INTEGRATE.BLINK_FRAME_NETWORK_HOOKS,
                INTEGRATE.BLINK_FRAME_NETWORK_SCOPE_HELPER_MARKER,
                INTEGRATE.BLINK_FRAME_MEMORY_CACHE_DEFINITION,
                INTEGRATE.patch_blink_frame_network_observer,
            ),
            (
                "resource_load_observer_for_worker.cc",
                '#include "third_party/blink/renderer/core/loader/'
                'resource_load_observer_for_worker.h"',
                "ResourceLoadObserverForWorker",
                INTEGRATE.BLINK_WORKER_NETWORK_HOOKS,
                INTEGRATE.BLINK_WORKER_NETWORK_SCOPE_HELPER_MARKER,
                INTEGRATE.BLINK_WORKER_MEMORY_CACHE_DEFINITION,
                INTEGRATE.patch_blink_worker_network_observer,
            ),
        )
        for name, include, observer, hooks, scope, definition, patch in cases:
            with self.subTest(source=name):
                patched = self.patch_twice(
                    name, self.observer_source(include, observer, hooks), patch
                )
                for _, replacement in hooks:
                    self.assertEqual(1, patched.count(replacement))
                self.assertEqual(1, patched.count(definition))
                for marker in (
                    INTEGRATE.BLINK_COOKIE_ORIGIN_HELPER_MARKER,
                    INTEGRATE.BLINK_NETWORK_HELPER_MARKER,
                    scope,
                ):
                    self.assertEqual(1, patched.count(marker))
                    self.assertLess(
                        patched.index(marker), patched.index(hooks[0][1])
                    )
                self.assertEqual(1, patched.count(INTEGRATE.BLINK_BRIDGE_INCLUDE))
                self.assert_bridge_calls_match(patched)

    def test_patches_the_memory_cache_notification_idempotently(self):
        fetcher = self.patch_twice(
            "resource_fetcher.cc",
            "void F() {\n" + INTEGRATE.BLINK_MEMORY_CACHE_FETCHER_ANCHOR + "}\n",
            INTEGRATE.patch_blink_resource_fetcher_memory_cache,
        )
        self.assertEqual(
            1, fetcher.count(INTEGRATE.BLINK_MEMORY_CACHE_FETCHER_HOOK)
        )
        observer = self.patch_twice(
            "resource_load_observer.h",
            "class ResourceLoadObserver {\n"
            "  virtual bool InterestedInAllRequests() = 0;\n};\n",
            INTEGRATE.patch_blink_resource_load_observer,
        )
        self.assertEqual(
            1, observer.count(INTEGRATE.BLINK_MEMORY_CACHE_OBSERVER_DECLARATION)
        )
        override = self.patch_twice(
            "resource_load_observer_for_frame.h",
            "class ResourceLoadObserverForFrame {\n"
            "  bool InterestedInAllRequests() override;\n};\n",
            INTEGRATE.patch_blink_network_observer_header,
        )
        self.assertEqual(
            1, override.count(INTEGRATE.BLINK_MEMORY_CACHE_OVERRIDE_DECLARATION)
        )

    def test_patches_the_request_identifiers_idempotently(self):
        cases = (
            (
                "frame_fetch_context.cc",
                '#include "third_party/blink/renderer/core/loader/'
                'frame_fetch_context.h"',
                INTEGRATE.BLINK_FRAME_REQUEST_ID_ANCHOR,
                INTEGRATE.BLINK_FRAME_REQUEST_ID_HOOK,
            ),
            (
                "worker_fetch_context.cc",
                '#include "third_party/blink/renderer/core/loader/'
                'worker_fetch_context.h"',
                INTEGRATE.BLINK_WORKER_REQUEST_ID_ANCHOR,
                INTEGRATE.BLINK_WORKER_REQUEST_ID_HOOK,
            ),
        )
        for name, include, anchor, hook in cases:
            with self.subTest(source=name):
                patched = self.patch_twice(
                    name,
                    f"{include}\n\nvoid F() {{\n{anchor}}}\n",
                    lambda path, include=include, anchor=anchor, hook=hook: (
                        INTEGRATE.patch_blink_fetch_context_request_ids(
                            path, include, anchor, hook
                        )
                    ),
                )
                self.assertEqual(1, patched.count(hook))
                self.assertIn("fetch_initiator_type_names::kInternal", hook)
                self.assert_bridge_calls_match(patched)
        navigation = self.patch_twice(
            "navigation_url_loader_impl.cc",
            '#include "content/browser/loader/navigation_url_loader_impl.h"\n'
            "\nvoid F() {\n"
            + INTEGRATE.CONTENT_NAVIGATION_REQUEST_ID_ANCHOR
            + "}\n",
            INTEGRATE.patch_content_navigation_request_id,
        )
        self.assertEqual(
            1, navigation.count(INTEGRATE.CONTENT_NAVIGATION_REQUEST_ID_HOOK)
        )
        self.assert_bridge_calls_match(navigation)

    def test_patches_the_wire_headers_idempotently(self):
        patched = self.patch_twice(
            "network_service_devtools_observer.cc",
            '#include "content/browser/devtools/'
            'network_service_devtools_observer.h"\n\nnamespace content {\n\n'
            + INTEGRATE.CONTENT_NETWORK_HEADERS_HELPER_ANCHOR
            + "    int) {}\n\nvoid F(\n"
            + INTEGRATE.CONTENT_RAW_REQUEST_ANCHOR
            + "}\n\nvoid G(\n"
            + INTEGRATE.CONTENT_RAW_RESPONSE_ANCHOR
            + "}\n\n}  // namespace content\n",
            INTEGRATE.patch_content_network_headers,
        )
        for hook in (
            INTEGRATE.CONTENT_RAW_REQUEST_HOOK,
            INTEGRATE.CONTENT_RAW_RESPONSE_HOOK,
        ):
            self.assertEqual(1, patched.count(hook))
            self.assertLess(
                patched.index(INTEGRATE.CONTENT_NETWORK_HEADERS_HELPER_MARKER),
                patched.index(hook),
            )
        self.assert_bridge_calls_match(patched)

    def test_patches_the_navigation_response_idempotently(self):
        patched = self.patch_twice(
            "web_contents_impl.cc",
            f"{INTEGRATE.CONTENT_NAVIGATION_INCLUDE}\n\nvoid F() {{\n"
            + INTEGRATE.CONTENT_NAVIGATION_COMPLETED_HOOK
            + "}\n",
            INTEGRATE.patch_web_contents_navigation_response,
        )
        self.assertEqual(
            1, patched.count(INTEGRATE.CONTENT_NAVIGATION_RESPONSE_HOOK)
        )
        self.assertLess(
            patched.index(INTEGRATE.CONTENT_NAVIGATION_COMPLETED_HOOK),
            patched.index(INTEGRATE.CONTENT_NAVIGATION_RESPONSE_HOOK),
        )
        self.assert_bridge_calls_match(patched)

    def test_no_network_template_reads_a_body_or_cookie_value(self):
        names = [
            name
            for name in dir(INTEGRATE)
            if name.isupper()
            and "NETWORK" in name
            and isinstance(getattr(INTEGRATE, name), str)
        ]
        self.assertTrue(names)
        for name in names:
            with self.subTest(template=name):
                text = getattr(INTEGRATE, name)
                self.assertNotIn(".Value()", text)
                self.assertNotIn("DidReceiveData", text)
                self.assertNotIn("GetBody", text)
                self.assertNotIn("cookie_line", text)

    def test_world_names_are_read_only_on_the_main_thread(self):
        # Blink keeps isolated world names and stable identifiers in maps that
        # assert the main thread, and worker loads and listeners run hooks on
        # worker threads, so every template read is guarded by the thread test.
        templates = [
            (name, value)
            for name, value in vars(INTEGRATE).items()
            if isinstance(value, str)
            and not name.startswith("STALE_")
            and INTEGRATE.UNGUARDED_WORLD_NAME_READ.search(value)
        ]
        self.assertGreater(len(templates), 0)
        for name, value in templates:
            self.assertEqual(
                INTEGRATE.describe_unguarded_world_name_reads(name, value), []
            )

    def test_stale_world_name_guards_are_upgraded_in_place(self):
        stale = (
            "          recorder_world && !recorder_world->IsMainWorld()\n"
            "              ? recorder_world->NonMainWorldHumanReadableName()"
            ".Utf8().c_str()\n"
            "              : \"\",\n"
            "  const DOMWrapperWorld& world = DOMWrapperWorld::Current(isolate);\n"
            + INTEGRATE.STALE_ORIGIN_WORLD_NAME_GUARD
            + "    origin.world_stable_id = world.NonMainWorldStableId().Utf8();\n"
            "  }\n"
        )
        self.assertNotEqual(
            INTEGRATE.describe_unguarded_world_name_reads("stale", stale), []
        )
        upgraded = INTEGRATE.upgrade_world_name_guards(stale)
        self.assertEqual(
            INTEGRATE.describe_unguarded_world_name_reads("upgraded", upgraded),
            [],
        )
        self.assertEqual(INTEGRATE.upgrade_world_name_guards(upgraded), upgraded)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "resource_load_observer_for_worker.cc"
            path.write_text(stale, encoding="utf-8")
            try:
                self.assertEqual(INTEGRATE.read_source(path), upgraded)
                self.assertEqual(path.read_text(encoding="utf-8"), upgraded)
            finally:
                INTEGRATE._INTEGRATED_PATHS.remove(path)

class InteractionIntegrationTests(unittest.TestCase):
    """Proves the interaction-state hooks are written once and match the bridge."""

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def assert_bridge_calls_match(self, text):
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches("patched", text, signatures),
        )

    def assert_patched(self, name, include, anchors, patch, hooks, markers):
        patched = self.patch_twice(
            name, cookie_source(include, *anchors), patch
        )
        for hook in hooks:
            self.assertEqual(1, patched.count(hook))
        for marker in markers:
            self.assertEqual(1, patched.count(marker))
            self.assertLess(patched.index(marker), patched.index(hooks[0]))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_BRIDGE_INCLUDE))
        for include_line in INTEGRATE.BLINK_INTERACTION_INCLUDES:
            self.assertEqual(1, patched.count(include_line))
        self.assert_bridge_calls_match(patched)
        return patched

    def test_patches_document_focus_changes_idempotently(self):
        patched = self.assert_patched(
            "document.cc",
            INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n",
            (
                INTEGRATE.BLINK_FOCUS_CHANGE_HELPER_ANCHOR,
                INTEGRATE.BLINK_FOCUS_CHANGE_ANCHOR,
            ),
            INTEGRATE.patch_blink_document_focus,
            (INTEGRATE.BLINK_FOCUS_CHANGE_HOOK,),
            (INTEGRATE.BLINK_FOCUS_CHANGE_HELPER_MARKER,),
        )
        # The focus helper only declares the script origin helper, so the
        # cookie patch still writes the definition later in the file.
        self.assertNotIn(INTEGRATE.BLINK_COOKIE_ORIGIN_HELPER_MARKER, patched)

    def test_patches_frame_selection_changes_idempotently(self):
        self.assert_patched(
            "frame_selection.cc",
            '#include "third_party/blink/renderer/core/editing/'
            'frame_selection.h"\n',
            (
                INTEGRATE.BLINK_SELECTION_CHANGE_HELPER_ANCHOR,
                INTEGRATE.BLINK_SELECTION_CHANGE_ANCHOR,
            ),
            INTEGRATE.patch_blink_frame_selection,
            (INTEGRATE.BLINK_SELECTION_CHANGE_HOOK,),
            (
                INTEGRATE.BLINK_COOKIE_ORIGIN_HELPER_MARKER,
                INTEGRATE.BLINK_SELECTION_CHANGE_HELPER_MARKER,
            ),
        )

    def test_patches_text_control_values_idempotently(self):
        cases = (
            (
                "html_input_element.cc",
                "html_input_element.h",
                (
                    INTEGRATE.BLINK_INPUT_SET_VALUE_HELPER_ANCHOR,
                    INTEGRATE.BLINK_INPUT_SET_VALUE_ANCHOR,
                ),
                INTEGRATE.patch_blink_input_element,
                (INTEGRATE.BLINK_INPUT_SET_VALUE_HOOK,),
            ),
            (
                "text_field_input_type.cc",
                "text_field_input_type.h",
                (
                    INTEGRATE.BLINK_TEXT_FIELD_EDIT_HELPER_ANCHOR,
                    INTEGRATE.BLINK_TEXT_FIELD_EDIT_ANCHOR,
                ),
                INTEGRATE.patch_blink_text_field_input_type,
                (INTEGRATE.BLINK_TEXT_FIELD_EDIT_HOOK,),
            ),
            (
                "html_text_area_element.cc",
                "html_text_area_element.h",
                (
                    INTEGRATE.BLINK_TEXT_AREA_HELPER_ANCHOR,
                    INTEGRATE.BLINK_TEXT_AREA_EDIT_ANCHOR,
                    INTEGRATE.BLINK_TEXT_AREA_SET_VALUE_ANCHOR,
                ),
                INTEGRATE.patch_blink_text_area_element,
                (
                    INTEGRATE.BLINK_TEXT_AREA_EDIT_HOOK,
                    INTEGRATE.BLINK_TEXT_AREA_SET_VALUE_HOOK,
                ),
            ),
        )
        for name, header, anchors, patch, hooks in cases:
            with self.subTest(source=name):
                self.assert_patched(
                    name,
                    '#include "third_party/blink/renderer/core/html/forms/'
                    + header
                    + '"\n',
                    anchors,
                    patch,
                    hooks,
                    (
                        INTEGRATE.BLINK_COOKIE_ORIGIN_HELPER_MARKER,
                        INTEGRATE.BLINK_TEXT_CONTROL_VALUE_HELPER_MARKER,
                    ),
                )

    def test_patches_active_descendant_references_idempotently(self):
        self.assert_patched(
            "element.cc",
            INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n",
            (
                INTEGRATE.BLINK_ACTIVE_DESCENDANT_HELPER_ANCHOR,
                INTEGRATE.BLINK_ACTIVE_DESCENDANT_ANCHOR,
            ),
            INTEGRATE.patch_blink_element_active_descendant,
            (INTEGRATE.BLINK_ACTIVE_DESCENDANT_HOOK,),
            (
                INTEGRATE.BLINK_COOKIE_ORIGIN_HELPER_MARKER,
                INTEGRATE.BLINK_ACTIVE_DESCENDANT_HELPER_MARKER,
            ),
        )

    def test_an_interaction_hook_fails_when_its_anchor_is_absent(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "frame_selection.cc"
            path.write_text(
                '#include "third_party/blink/renderer/core/editing/'
                'frame_selection.h"\n'
                + INTEGRATE.BLINK_SELECTION_CHANGE_HELPER_ANCHOR,
                encoding="utf-8",
            )
            with self.assertRaises(RuntimeError):
                INTEGRATE.patch_blink_frame_selection(path)

    def test_only_the_text_control_hook_reads_a_control_value(self):
        for name in (
            "BLINK_FOCUS_CHANGE_HELPER",
            "BLINK_SELECTION_CHANGE_HELPER",
            "BLINK_ACTIVE_DESCENDANT_HELPER",
        ):
            with self.subTest(template=name):
                self.assertNotIn(".Value()", getattr(INTEGRATE, name))
                self.assertNotIn("->Value()", getattr(INTEGRATE, name))


class LayoutIntegrationTests(unittest.TestCase):
    """Proves the layout checkpoint hook is written once and never forces work."""

    LOCAL_FRAME_VIEW_INCLUDE = (
        '#include "third_party/blink/renderer/core/frame/local_frame_view.h"'
    )

    def patch_twice(self, source):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "local_frame_view.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_local_frame_view(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_local_frame_view(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def test_patches_the_local_frame_view_idempotently(self):
        patched = self.patch_twice(
            cookie_source(
                self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
                INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
                INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
            )
        )
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER)
        )
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HOOK)
        )
        self.assertLess(
            patched.index(INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER),
            patched.index(INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HOOK),
        )
        for include_line in INTEGRATE.BLINK_LAYOUT_CHECKPOINT_INCLUDES:
            with self.subTest(include=include_line):
                self.assertEqual(1, patched.count(include_line + "\n"))
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "patched", patched, signatures
            ),
        )

    def test_upgrades_a_light_tree_helper_in_place(self):
        """A helper written before the composed traversal is rewritten."""
        source = cookie_source(
            self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
        )
        current = self.patch_twice(source)
        earlier_helper = (
            "namespace {\n\n"
            + INTEGRATE.BLINK_LAYOUT_STYLE_PROPERTY_ARRAY
            + "void RecorderRecordLayoutCheckpoint(LocalFrameView& frame_view) {\n"
            "  for (Node& recorder_node :\n"
            "       NodeTraversal::InclusiveDescendantsOf(*recorder_document)) {\n"
            "  }\n"
            "}\n\n"
            "}  // namespace\n\n"
        )
        earlier = current.replace(
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER, earlier_helper, 1
        )
        self.assertNotEqual(current, earlier)
        self.assertEqual(current, self.patch_twice(earlier))

    def test_the_helper_records_the_composed_tree_and_pseudo_elements(self):
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        self.assertNotIn("NodeTraversal::InclusiveDescendantsOf", helper)
        self.assertIn("recorder_element->GetShadowRoot()", helper)
        self.assertIn("RecorderAppendPseudoElements(", helper)
        self.assertIn("ForEachTransitionPseudo(", helper)
        self.assertIn("GetColumnPseudoElements()", helper)
        self.assertIn("TransformedText()", helper)
        # Reading a pseudo-element never creates one.
        self.assertNotIn("EnsurePseudoElement", helper)
        self.assertNotIn("CreatePseudoElementIfNeeded", helper)

    def test_upgrades_a_helper_that_indexes_the_property_array(self):
        legacy_helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        for legacy, current in INTEGRATE.BLINK_LAYOUT_CHECKPOINT_LEGACY_STYLE_LOOPS:
            legacy_helper = legacy_helper.replace(current, legacy, 1)
        self.assertRegex(legacy_helper, r"kRecorderLayoutStyleProperties\[\w")
        source = cookie_source(
            self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "local_frame_view.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_local_frame_view(path)
            current = path.read_text(encoding="utf-8")
            path.write_text(
                current.replace(
                    INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER, legacy_helper, 1
                ),
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_local_frame_view(path)
            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_upgrades_a_helper_that_records_an_earlier_property_list(self):
        earlier_array = (
            "constexpr CSSPropertyID kRecorderLayoutStyleProperties[] = {\n"
            + "".join(
                f"    CSSPropertyID::{INTEGRATE.blink_css_property_enum(name)},\n"
                for name in INTEGRATE.LAYOUT_STYLE_PROPERTIES[:75]
            )
            + "};\n"
        )
        current_helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        earlier_helper = current_helper.replace(
            INTEGRATE.BLINK_LAYOUT_STYLE_PROPERTY_ARRAY, earlier_array, 1
        )
        indexed_helper = earlier_helper
        for legacy, current in INTEGRATE.BLINK_LAYOUT_CHECKPOINT_LEGACY_STYLE_LOOPS:
            indexed_helper = indexed_helper.replace(current, legacy, 1)
        self.assertNotEqual(current_helper, earlier_helper)
        self.assertNotEqual(earlier_helper, indexed_helper)
        source = cookie_source(
            self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "local_frame_view.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_local_frame_view(path)
            current = path.read_text(encoding="utf-8")
            for older_helper in (earlier_helper, indexed_helper):
                with self.subTest(indexed=older_helper is indexed_helper):
                    path.write_text(
                        current.replace(current_helper, older_helper, 1),
                        encoding="utf-8",
                    )
                    INTEGRATE.patch_blink_local_frame_view(path)
                    self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_a_patched_tree_holds_exactly_one_property_array(self):
        source = cookie_source(
            self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "local_frame_view.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_local_frame_view(path)
            current = path.read_text(encoding="utf-8")
            path.write_text(
                current.replace(
                    INTEGRATE.BLINK_LAYOUT_STYLE_PROPERTY_ARRAY,
                    INTEGRATE.BLINK_LAYOUT_STYLE_PROPERTY_ARRAY * 2,
                    1,
                ),
                encoding="utf-8",
            )
            with self.assertRaises(RuntimeError):
                INTEGRATE.patch_blink_local_frame_view(path)

    def test_the_layout_checkpoint_is_followed_by_an_interaction_checkpoint(
        self,
    ):
        patched = self.patch_twice(
            cookie_source(
                self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
                INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
                INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
            )
        )
        declaration = INTEGRATE.BLINK_LAYOUT_INTERACTION_CHECKPOINT_DECLARATION
        self.assertEqual(1, patched.count(declaration))
        # The declaration must name the blink scope helper, not a function of
        # the unnamed namespace the layout helper opens.
        self.assertLess(
            patched.index(declaration),
            patched.index("namespace {\n", patched.index(declaration)),
        )
        complete = patched.index("a11y_recorder::CompleteBlinkLayoutCheckpoint(")
        interaction = patched.index(
            'RecorderRecordInteractionCheckpoint(*recorder_document,'
        )
        self.assertLess(complete, interaction)
        self.assertIn('"browser.layout", "rendering-update");', patched)

    def test_upgrades_a_layout_helper_without_the_interaction_checkpoint(self):
        """A 0.28 helper region, with no declaration ahead of it, is rewritten."""
        current_helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        call = """\
  RecorderRecordInteractionCheckpoint(*recorder_document,
                                      recorder_checkpoint_sequence,
                                      "browser.layout", "rendering-update");
"""
        earlier_helper = current_helper.replace(
            INTEGRATE.BLINK_LAYOUT_INTERACTION_CHECKPOINT_DECLARATION, "", 1
        ).replace(call, "", 1)
        self.assertNotIn("RecorderRecordInteractionCheckpoint", earlier_helper)
        source = cookie_source(
            self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "local_frame_view.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_local_frame_view(path)
            current = path.read_text(encoding="utf-8")
            path.write_text(
                current.replace(current_helper, earlier_helper, 1),
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_local_frame_view(path)
            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_the_layout_helper_never_indexes_a_raw_array(self):
        # Blink compiles with unsafe buffer usage as an error.
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        self.assertNotRegex(helper, r"kRecorderLayoutStyleProperties\[\w")

    def test_the_layout_hook_fails_when_its_anchor_is_absent(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "local_frame_view.cc"
            path.write_text(
                self.LOCAL_FRAME_VIEW_INCLUDE
                + "\n"
                + INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
                encoding="utf-8",
            )
            with self.assertRaises(RuntimeError):
                INTEGRATE.patch_blink_local_frame_view(path)

    def test_the_layout_helper_never_requests_style_or_layout(self):
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        for forcing_call in (
            "UpdateStyleAndLayout",
            "EnsureComputedStyle",
            "UpdateLifecycle",
            "GetBoundingClientRect()",
            "getBoundingClientRect",
            "UpdateLayout",
        ):
            with self.subTest(call=forcing_call):
                self.assertNotIn(forcing_call, helper)
        self.assertIn("GetBoundingClientRectNoLifecycleUpdate()", helper)

    def test_the_helper_records_the_documented_property_list(self):
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        identifiers = re.findall(r"CSSPropertyID::(k[A-Za-z]+),", helper)
        expected = [
            "k" + "".join(word.capitalize() for word in name.split("-"))
            for name in INTEGRATE.LAYOUT_STYLE_PROPERTIES
        ]
        self.assertEqual(expected, identifiers)
        self.assertEqual(
            len(set(INTEGRATE.LAYOUT_STYLE_PROPERTIES)),
            len(INTEGRATE.LAYOUT_STYLE_PROPERTIES),
        )
        root = Path(__file__).parent.parent
        document = (
            root / "docs" / "architecture"
            / "layout-and-style-checkpoint-evidence-model.md"
        ).read_text(encoding="utf-8")
        verifier = (
            root / "scripts" / "Verify-BlinkEvidence.ps1"
        ).read_text(encoding="utf-8")
        for name in INTEGRATE.LAYOUT_STYLE_PROPERTIES:
            with self.subTest(property=name):
                self.assertIn(f"`{name}`", document)
                self.assertIn(f"'{name}'", verifier)


class RealtimeIntegrationTests(unittest.TestCase):
    """Proves the realtime channel hooks are written once and match the bridge."""

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            try:
                patch(path)
                first = path.read_text(encoding="utf-8")
                patch(path)
                self.assertEqual(first, path.read_text(encoding="utf-8"))
            finally:
                if path in INTEGRATE._INTEGRATED_PATHS:
                    INTEGRATE._INTEGRATED_PATHS.remove(path)
            return first

    def assert_bridge_calls_match(self, text):
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches("patched", text, signatures),
        )

    def module_source(self, include, helper_anchor, hooks):
        return (
            f"{include}\n\nnamespace blink {{\n\n{helper_anchor}  return;\n}}\n\n"
            + "".join(f"void F() {{\n{anchor}}}\n\n" for anchor, _ in hooks)
            + "}  // namespace blink\n"
        )

    def cases(self):
        return (
            (
                "websocket_channel_impl.cc",
                '#include "third_party/blink/renderer/modules/websockets/'
                'websocket_channel_impl.h"',
                "bool WebSocketChannelImpl::Connect(\n",
                INTEGRATE.BLINK_WEBSOCKET_HOOKS,
                INTEGRATE.patch_blink_websocket_channel,
                True,
            ),
            (
                "event_source.cc",
                '#include "third_party/blink/renderer/modules/eventsource/'
                'event_source.h"',
                "void EventSource::OnMessageEvent(const AtomicString& "
                "event_type,\n",
                (
                    (
                        INTEGRATE.BLINK_EVENT_SOURCE_MESSAGE_ANCHOR,
                        INTEGRATE.BLINK_EVENT_SOURCE_MESSAGE_HOOK,
                    ),
                ),
                INTEGRATE.patch_blink_event_source,
                False,
            ),
            (
                "web_transport.cc",
                '#include "third_party/blink/renderer/modules/webtransport/'
                'web_transport.h"',
                "// RecentlyForgottenStreamIdSet implementation\n",
                INTEGRATE.BLINK_WEB_TRANSPORT_HOOKS,
                INTEGRATE.patch_blink_web_transport,
                True,
            ),
        )

    def test_patches_each_realtime_source_idempotently(self):
        for name, include, helper_anchor, hooks, patch, origin in self.cases():
            with self.subTest(source=name):
                patched = self.patch_twice(
                    name, self.module_source(include, helper_anchor, hooks), patch
                )
                for _, hook in hooks:
                    self.assertEqual(1, patched.count(hook))
                self.assertEqual(
                    1, patched.count(INTEGRATE.BLINK_REALTIME_HELPER_MARKER)
                )
                self.assertEqual(
                    1 if origin else 0,
                    patched.count(INTEGRATE.BLINK_COOKIE_ORIGIN_HELPER_MARKER),
                )
                self.assertLess(
                    patched.index(INTEGRATE.BLINK_REALTIME_HELPER_MARKER),
                    patched.index(helper_anchor),
                )
                self.assert_bridge_calls_match(patched)

    def test_a_realtime_hook_fails_when_its_anchor_is_absent(self):
        for name, include, helper_anchor, hooks, patch, _ in self.cases():
            with self.subTest(source=name):
                source = self.module_source(include, helper_anchor, hooks[:-1])
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / name
                    path.write_text(source, encoding="utf-8")
                    with self.assertRaises(RuntimeError):
                        patch(path)

    def test_only_script_calls_carry_a_script_origin(self):
        # A received message, a handshake, a failure, and a close the network
        # reported have no script call, so their hooks read no origin.
        without_origin = (
            INTEGRATE.BLINK_WEBSOCKET_RECEIVE_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_HANDSHAKE_REQUEST_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_HANDSHAKE_RESPONSE_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_FAIL_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_DISCONNECT_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_DROP_HOOK,
            INTEGRATE.BLINK_EVENT_SOURCE_MESSAGE_HOOK,
            INTEGRATE.BLINK_WEB_TRANSPORT_ESTABLISHED_HOOK,
            INTEGRATE.BLINK_WEB_TRANSPORT_CLEANUP_HOOK,
        )
        with_origin = (
            INTEGRATE.BLINK_WEBSOCKET_CREATED_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_SEND_TEXT_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_SEND_BLOB_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_SEND_BUFFER_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_CLOSE_HOOK,
            INTEGRATE.BLINK_WEB_TRANSPORT_CREATED_HOOK,
            INTEGRATE.BLINK_WEB_TRANSPORT_CLOSE_HOOK,
        )
        for hook in without_origin:
            self.assertNotIn("RecorderCookieCallOrigin", hook)
        for hook in with_origin:
            self.assertIn("RecorderCookieCallOrigin", hook)

    def test_binary_payloads_are_not_read(self):
        for hook in (
            INTEGRATE.BLINK_WEBSOCKET_SEND_BLOB_HOOK,
            INTEGRATE.BLINK_WEBSOCKET_SEND_BUFFER_HOOK,
        ):
            self.assertNotIn("ByteSpan", hook.split("if (a11y_recorder")[1])
            self.assertIn("std::string()", hook)
        self.assertIn(
            "receiving_message_type_is_text_ ? RecorderRealtimeChunksText",
            INTEGRATE.BLINK_WEBSOCKET_RECEIVE_HOOK,
        )

    def test_patches_the_realtime_builds_idempotently(self):
        with tempfile.TemporaryDirectory() as directory:
            modules = Path(directory)
            sources = {
                "websockets": (
                    'blink_modules_sources("websockets") {\n'
                    '  sources = [ "websocket_channel_impl.cc" ]\n\n'
                    + INTEGRATE.BLINK_WEBSOCKETS_BUILD_DEPS
                    + "}\n"
                ),
                "eventsource": (
                    'blink_modules_sources("eventsource") {\n  sources = [\n'
                    + INTEGRATE.BLINK_EVENT_SOURCE_BUILD_ANCHOR
                ),
                "webtransport": (
                    'blink_modules_sources("webtransport") {\n  sources = [\n'
                    + INTEGRATE.BLINK_WEB_TRANSPORT_BUILD_ANCHOR
                    + '\nsource_set("unit_tests") {\n}\n'
                ),
            }
            for name, text in sources.items():
                (modules / name).mkdir()
                (modules / name / "BUILD.gn").write_text(text, encoding="utf-8")
            paths = [modules / name / "BUILD.gn" for name in sources]
            try:
                INTEGRATE.patch_blink_realtime_builds(modules)
                first = [path.read_text(encoding="utf-8") for path in paths]
                INTEGRATE.patch_blink_realtime_builds(modules)
                self.assertEqual(
                    first, [path.read_text(encoding="utf-8") for path in paths]
                )
            finally:
                for path in paths:
                    if path in INTEGRATE._INTEGRATED_PATHS:
                        INTEGRATE._INTEGRATED_PATHS.remove(path)
            self.assertIn(INTEGRATE.BLINK_WEBSOCKETS_BUILD_PATCHED_DEPS, first[0])
            for text in first[1:]:
                self.assertEqual(1, text.count('"//chromium/recorder_bridge"'))
                self.assertIn('  deps = [ "//chromium/recorder_bridge" ]\n}\n', text)


class NetworkServiceCookieNameTests(unittest.TestCase):
    """Proves the network service reports handshake cookies by name only."""

    SOURCE = (
        '#include "services/network/websocket.h"\n\n'
        '#include "build/build_config.h"\n'
        '#include "net/base/auth.h"\n\n'
        "namespace network {\nnamespace {\n\n"
        "mojom::WebSocketHandshakeResponsePtr ToMojo(\n"
        "    std::unique_ptr<net::WebSocketHandshakeResponseInfo> response,\n"
        "    bool has_raw_headers_access) {\n"
        "  while (response->headers->EnumerateHeaderLines(&iter, &name, &value)) {\n"
        "  }\n}\n\n}  // namespace\n\n"
        "void Handler::OnStartOpeningHandshake() {\n"
        "  while (it.GetNext()) {\n  }\n}\n\n}  // namespace network\n"
    )
    BUILD = (
        'component("network_service") {\n'
        '  deps = [\n    "//base",\n    "//base:build_time",\n'
        '    "//net",\n    "//url",\n  ]\n\n'
        "  if (is_linux) {\n    deps += [ \":sandbox\" ]\n  }\n}\n"
    )

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            try:
                patch(path)
                first = path.read_text(encoding="utf-8")
                patch(path)
                self.assertEqual(first, path.read_text(encoding="utf-8"))
            finally:
                if path in INTEGRATE._INTEGRATED_PATHS:
                    INTEGRATE._INTEGRATED_PATHS.remove(path)
            return first

    def test_patches_the_websocket_source_idempotently(self):
        text = self.patch_twice(
            "websocket.cc", self.SOURCE, INTEGRATE.patch_network_websocket
        )
        self.assertEqual(1, text.count("bool RecorderReportsCookieNames() {"))
        self.assertEqual(1, text.count("RecorderSetCookieName(value)"))
        self.assertEqual(1, text.count("RecorderCookieHeaderNames(it.value())"))
        # BUILDFLAG is only defined once the build configuration is included.
        self.assertLess(
            text.index('#include "build/build_config.h"'),
            text.index("#if BUILDFLAG(IS_WIN)"),
        )
        # The helpers live in the anonymous namespace ToMojo uses.
        self.assertLess(
            text.index("bool RecorderReportsCookieNames() {"),
            text.index("mojom::WebSocketHandshakeResponsePtr ToMojo("),
        )

    def test_hooks_run_only_without_raw_header_access(self):
        self.assertIn(
            "!has_raw_headers_access && RecorderReportsCookieNames()",
            INTEGRATE.NETWORK_WEBSOCKET_RESPONSE_HOOK,
        )
        self.assertIn(
            "!impl_->has_raw_headers_access_ && RecorderReportsCookieNames()",
            INTEGRATE.NETWORK_WEBSOCKET_REQUEST_HOOK,
        )
        # Only Cookie and Set-Cookie are reported. Authorization headers keep
        # being stripped by Chromium's own filter.
        for hook in (
            INTEGRATE.NETWORK_WEBSOCKET_RESPONSE_HOOK,
            INTEGRATE.NETWORK_WEBSOCKET_REQUEST_HOOK,
        ):
            self.assertNotIn("Authorization", hook)

    def test_the_hooks_forward_no_original_value(self):
        # Each forwarded header is built from the name-only text alone.
        self.assertIn(
            "mojom::HttpHeader::New(name, names_only)",
            INTEGRATE.NETWORK_WEBSOCKET_RESPONSE_HOOK,
        )
        self.assertIn(
            "mojom::HttpHeader::New(it.name(), names_only)",
            INTEGRATE.NETWORK_WEBSOCKET_REQUEST_HOOK,
        )

    def test_the_switch_names_match_the_bridge(self):
        switches = (
            MODULE_PATH.parent / "recorder_bridge" / "recorder_switches.h"
        ).read_text(encoding="utf-8")
        self.assertIn(
            'kRecordingNetworkServiceSwitch[] =\n'
            '    "a11y-recorder-recording-network-service";',
            switches,
        )
        self.assertIn(
            '"network.mojom.NetworkService"', switches
        )

    def test_fails_when_a_websocket_anchor_is_absent(self):
        source = self.SOURCE.replace("  while (it.GetNext()) {\n", "")
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "websocket.cc"
            path.write_text(source, encoding="utf-8")
            with self.assertRaises(RuntimeError):
                INTEGRATE.patch_network_websocket(path)

    def test_appends_the_build_dependency_after_the_deps_list(self):
        text = self.patch_twice(
            "BUILD.gn", self.BUILD, INTEGRATE.patch_network_service_build
        )
        self.assertEqual(
            1, text.count('"//chromium/recorder_bridge:cookie_names"')
        )
        self.assertLess(
            text.index('    "//url",\n  ]\n'),
            text.index('deps += [ "//chromium/recorder_bridge:cookie_names" ]'),
        )

    def test_the_cookie_name_target_needs_no_chromium_dependency(self):
        build = (
            MODULE_PATH.parent / "recorder_bridge" / "BUILD.gn"
        ).read_text(encoding="utf-8")
        start = build.index('source_set("cookie_names") {')
        target = build[start:build.index("\n}\n", start)]
        self.assertNotIn("deps", target)
        self.assertIn('"cookie_text.cc"', target)
        self.assertIn('public_deps = [ ":cookie_names" ]', build)


if __name__ == "__main__":
    unittest.main()
