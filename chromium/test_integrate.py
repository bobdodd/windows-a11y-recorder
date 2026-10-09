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


# The at-target dispatch Blink performs for an EventTarget that is not a Node.
EVENT_TARGET_DISPATCH_SOURCE = (
    "\n"
    "DispatchEventResult EventTarget::DispatchEventInternal(Event& event) {\n"
    "  event.SetTarget(this);\n"
    "  event.SetCurrentTarget(this);\n"
    "  event.SetEventPhase(Event::PhaseType::kAtTarget);\n"
    "  DispatchEventResult dispatch_result = FireEventListeners(event);\n"
    "  event.SetEventPhase(Event::PhaseType::kNone);\n"
    "  return dispatch_result;\n"
    "}\n"
)
# The entry point the Node event dispatcher's source opens with.
EVENT_DISPATCHER_DISPATCH_EVENT_SOURCE = (
    "DispatchEventResult EventDispatcher::DispatchEvent(Node& node, "
    "Event& event) {\n"
    "  return EventDispatcher(node, event).Dispatch();\n"
    "}\n"
    "\n"
)
# A window's dispatch of its own load and pageshow events.
LOCAL_DOM_WINDOW_SOURCE = (
    '#include "third_party/blink/renderer/core/frame/local_dom_window.h"\n'
    "\n"
    "#include <memory>\n"
    "\n"
    "DispatchEventResult LocalDOMWindow::DispatchEvent(Event& event,\n"
    "                                                  EventTarget* target) {\n"
    "  event.SetTrusted(true);\n"
    "  event.SetTarget(target ? target : this);\n"
    "  event.SetCurrentTarget(this);\n"
    "  event.SetEventPhase(Event::PhaseType::kAtTarget);\n"
    "\n"
    '  DEVTOOLS_TIMELINE_TRACE_EVENT("EventDispatch",\n'
    "                                inspector_event_dispatch_event::Data, "
    "event,\n"
    "                                GetIsolate());\n"
    "  return FireEventListeners(event);\n"
    "}\n"
)
# IndexedDB's own propagation from a request to its transaction and database.
IDB_EVENT_DISPATCHER_SOURCE = (
    '#include "third_party/blink/renderer/modules/indexeddb/'
    'idb_event_dispatcher.h"\n'
    "\n"
    "namespace blink {\n"
    "\n"
    "DispatchEventResult IDBEventDispatcher::Dispatch(\n"
    "    Event& event,\n"
    "    HeapVector<Member<EventTarget>>& event_targets) {\n"
    "  wtf_size_t size = event_targets.size();\n"
    "  DCHECK(size);\n"
    "\n"
    "  event.SetEventPhase(Event::PhaseType::kCapturingPhase);\n"
    "  for (wtf_size_t i = size - 1; i; --i) {  // Don't do the first element.\n"
    "    event.SetCurrentTarget(event_targets[i].Get());\n"
    "    event_targets[i]->FireEventListeners(event);\n"
    "    if (event.PropagationStopped())\n"
    "      goto doneDispatching;\n"
    "  }\n"
    "\n"
    "doneDispatching:\n"
    "  event.SetCurrentTarget(nullptr);\n"
    "  event.SetEventPhase(Event::PhaseType::kNone);\n"
    "  return EventTarget::GetDispatchEventResult(event);\n"
    "}\n"
    "\n"
    "}  // namespace blink\n"
)
IDB_BUILD_SOURCE = (
    'blink_modules_sources("indexeddb") {\n'
    "  sources = [\n"
    '    "shared_idb_database_connection.cc",\n'
    '    "shared_idb_database_connection.h",\n'
    "  ]\n"
    "\n"
    "  public_deps = [\n"
    '    "//third_party/blink/renderer/modules/indexeddb:mojo",\n'
    "  ]\n"
    "}\n"
)


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
                "namespace blink {\n"
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
                "}\n"
                + EVENT_TARGET_DISPATCH_SOURCE,
                encoding="utf-8",
            )
            event_dispatcher.write_text(
                '#include "third_party/blink/renderer/core/dom/events/'
                'event_dispatcher.h"\n'
                '#include "build/build_config.h"\n'
                "\n"
                + EVENT_DISPATCHER_DISPATCH_EVENT_SOURCE
                + "DispatchEventResult EventDispatcher::Dispatch() {\n"
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
        self.assertIn('kProtocolVersion[] = "0.57"', bridge_protocol)
        self.assertIn('CurrentVersion = "0.57"', contracts)

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
            self.assertIn("kRecorderMaximumDomCheckpointNodes = 2147483647", first)
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
            # Protocol 0.42: a change queues a delivery in every parsing state.
            self.assertIn(
                "    const ContainerNode::ChildrenChange& change) {\n"
                "  MutationObserver::EnqueueRecorderDomCheckpoint(*this);\n",
                first,
            )
            self.assertNotIn("if (HasFinishedParsing())", first)
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
            # Protocol 0.42: a document that parses is delivered too.
            self.assertNotIn("recorder_document->HasFinishedParsing()", first)
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
                "kRecorderMaximumDomAttributesPerNode = 2147483647", document_first
            )
            self.assertIn(
                "kRecorderMaximumDomValueLength = 2147483647", document_first
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

    def test_records_structural_dom_changes_from_the_document(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "document.cc"
            path.write_text(
                self.document_source(INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER),
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_document(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_document(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        self.assertEqual(1, first.count(INTEGRATE.BLINK_DOM_CHANGE_HELPER))
        self.assertEqual(
            1, first.count(INTEGRATE.BLINK_DOCUMENT_DOM_CHANGE_DECLARATION)
        )
        hook = (
            INTEGRATE.BLINK_DOCUMENT_NOTIFY_CHANGE_CHILDREN_ANCHOR
            + INTEGRATE.BLINK_DOCUMENT_MUTATION_HOOK
            + INTEGRATE.BLINK_DOCUMENT_DOM_CHANGE_HOOK
        )
        self.assertEqual(1, first.count(hook))
        # The declaration precedes the hook, and the definition follows the
        # checkpoint it shares its walk with.
        self.assertLess(
            first.index(INTEGRATE.BLINK_DOCUMENT_DOM_CHANGE_DECLARATION),
            first.index(hook),
        )
        self.assertLess(
            first.index(INTEGRATE.BLINK_DOM_CHANGE_HELPER),
            first.index("void Document::FinishedParsing() {"),
        )

    def test_upgrades_hooks_that_skip_a_document_while_it_parses(self):
        # Protocol 0.42: a checkout patched at 0.41 records nothing while a
        # document parses; each of its hooks is upgraded.
        with tempfile.TemporaryDirectory() as directory:
            document = Path(directory) / "document.cc"
            document.write_text(
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
            INTEGRATE.patch_blink_document(document)
            current = document.read_text(encoding="utf-8")
            legacy = current.replace(
                INTEGRATE.BLINK_DOCUMENT_NOTIFY_CHANGE_CHILDREN_ANCHOR
                + INTEGRATE.BLINK_DOCUMENT_MUTATION_HOOK,
                INTEGRATE.BLINK_DOCUMENT_NOTIFY_CHANGE_CHILDREN_ANCHOR
                + INTEGRATE.LEGACY_FINISHED_ONLY_BLINK_DOCUMENT_MUTATION_HOOK,
            ).replace(
                INTEGRATE.BLINK_DOM_CHANGE_HELPER_PARSING_INCLUDED,
                INTEGRATE.BLINK_DOM_CHANGE_HELPER_PARSING_EXCLUDED,
            )
            self.assertNotEqual(current, legacy)
            self.assertIn(
                INTEGRATE.LEGACY_PARSING_EXCLUDED_BLINK_DOM_CHANGE_HELPER, legacy
            )
            document.write_text(legacy, encoding="utf-8")
            INTEGRATE.patch_blink_document(document)
            self.assertEqual(current, document.read_text(encoding="utf-8"))

            character_data = Path(directory) / "character_data.cc"
            character_data.write_text(
                '#include "third_party/blink/renderer/core/dom/character_data.h"\n'
                "\n"
                "void CharacterData::SetDataAndUpdate() {\n"
                "  String old_data = this->data();\n"
                "  data_ = new_data;\n"
                "}\n",
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_character_data(character_data)
            current = character_data.read_text(encoding="utf-8")
            legacy = current.replace(
                INTEGRATE.BLINK_CHARACTER_DATA_MUTATION_HOOK,
                INTEGRATE.LEGACY_PARSE_TIME_EXCLUDED_BLINK_CHARACTER_DATA_MUTATION_HOOK,
            )
            self.assertNotEqual(current, legacy)
            character_data.write_text(legacy, encoding="utf-8")
            INTEGRATE.patch_blink_character_data(character_data)
            self.assertEqual(current, character_data.read_text(encoding="utf-8"))

    def test_walks_the_dom_when_the_parser_is_created(self):
        # Protocol 0.42: Document::ImplicitOpen walks the DOM once it has
        # created the parser, after its declaration of the walk.
        source = (
            "void Document::open() {\n"
            "  ImplicitOpen(kForceSynchronousParsing);\n"
            "}\n"
            "\n"
            "DocumentParser* Document::ImplicitOpen(\n"
            "    ParserSynchronizationPolicy parser_sync_policy) {\n"
            "  RemoveChildren();\n"
            "  parser_ = CreateParser();\n"
            "  DocumentParserTiming::From(*this).MarkParserStart();\n"
            "  SetParsingState(kParsing);\n"
            "  SetReadyState(kLoading);\n"
            "  return parser_.Get();\n"
            "}\n"
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "document.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_document_started_parsing(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_document_started_parsing(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            self.assertIn(
                "  SetParsingState(kParsing);\n"
                "  // Windows A11y Recorder (protocol 0.42): the DOM when the parser is\n"
                "  // created, the state the parser's changes apply to.\n"
                "  if (IsActive())\n"
                '    RecorderRecordDomCheckpoint(*this, "started-parsing");\n',
                first,
            )
            # The walk is followed by the recreation's browser page mark.
            self.assertIn(
                INTEGRATE.BLINK_DOCUMENT_STARTED_PARSING_HOOK + "  SetReadyState(kLoading);\n",
                first,
            )
            self.assertLess(
                first.index("void RecorderRecordDomCheckpoint("),
                first.index("DocumentParser* Document::ImplicitOpen("),
            )
            self.assertEqual(1, first.count("void RecorderRecordDomCheckpoint("))

    def test_dom_change_helper_records_what_the_design_states(self):
        helper = INTEGRATE.BLINK_DOM_CHANGE_HELPER
        # Protocol 0.42: changes are recorded while the document parses too.
        self.assertNotIn("Parsing()", helper)
        self.assertNotIn("HasFinishedParsing()", helper)
        # Every child list change type is handled explicitly.
        for change_type in (
            "kElementInserted",
            "kNonElementInserted",
            "kElementRemoved",
            "kNonElementRemoved",
            "kAllChildrenRemoved",
            "kTextChanged",
            "kFinishedBuildingDocumentFragmentTree",
        ):
            self.assertIn(change_type, helper)
        for entry in (
            "RecordBlinkDomNodeInserted(",
            "RecordBlinkDomInsertedNode(",
            "RecordBlinkDomInsertedNodeAttribute(",
            "RecordBlinkDomInsertedNodeCharacterData(",
            "RecordBlinkDomInsertedShadowRoot(",
            "RecordBlinkDomInsertedSlotAssignment(",
            "CompleteBlinkDomInsertion(",
            "RecordBlinkDomNodeRemoved(",
            "RecordBlinkDomChildrenRemoved(",
            "RecordBlinkDomShadowRootChanged(",
            "RecordBlinkDomSlotAssignmentChanged(",
        ):
            self.assertIn(f"a11y_recorder::{entry}", helper)
        # Nothing is cut: every value and every assigned node is recorded.
        self.assertIn("kRecorderMaximumDomValueLength = 2147483647", helper)
        self.assertIn("kRecorderMaximumAssignedNodes = 2147483647", helper)
        self.assertNotIn("->AssignedNodes()", helper)
        self.assertNotIn("RecalcAssignment()", helper)

    def test_upgrades_a_character_data_hook_that_excludes_the_parser(self):
        legacy = INTEGRATE.LEGACY_PARSER_EXCLUDED_BLINK_CHARACTER_DATA_MUTATION_HOOK
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "character_data.cc"
            path.write_text(
                '#include "third_party/blink/renderer/core/dom/'
                'character_data.h"\n'
                f"{INTEGRATE.BLINK_BRIDGE_INCLUDE}\n"
                '#include "third_party/blink/renderer/core/dom/document.h"\n'
                '#include "third_party/blink/renderer/core/dom/'
                'mutation_observer.h"\n'
                "\n"
                "void CharacterData::SetDataAndUpdate() {\n"
                "  String old_data = this->data();\n"
                + legacy
                + "}\n",
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_character_data(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_character_data(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        self.assertNotIn(legacy, first)
        self.assertEqual(
            1, first.count(INTEGRATE.BLINK_CHARACTER_DATA_MUTATION_HOOK)
        )

    def test_upgrades_a_layout_change_definition_without_scroll_offsets(self):
        definition = INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION
        legacy = INTEGRATE.LEGACY_UNSCROLLED_BLINK_LAYOUT_CHANGES_DEFINITION
        self.assertIn(legacy, INTEGRATE.BLINK_LAYOUT_CHANGES_LEGACY_DEFINITIONS)
        self.assertNotIn("RecorderNoteScrollOffset", legacy)
        self.assertIn("void RecorderNoteScrollOffset(", definition)
        self.assertIn("std::move(recorder_scroll_offsets));", definition)
        # The offsets are read before the transform nodes, so the scroll
        # translation each names is read in the same change set.
        self.assertLess(
            definition.index("recorder_scroll_offsets.push_back("),
            definition.index("recorder_transforms.push_back("),
        )

    def test_upgrades_a_dom_helper_that_skips_unwalked_deliveries(self):
        legacy = INTEGRATE.LEGACY_UNSKIPPED_BLINK_DOM_CHECKPOINT_HELPER
        self.assertNotEqual(legacy, INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "document.cc"
            path.write_text(self.document_source(legacy), encoding="utf-8")
            INTEGRATE.patch_blink_document(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_document(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        self.assertNotIn(legacy, first)
        self.assertEqual(1, first.count(INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER))

    def test_upgrades_a_dom_helper_without_character_data(self):
        legacy = INTEGRATE.LEGACY_UNTEXTED_BLINK_DOM_CHECKPOINT_HELPER
        self.assertNotIn("RecordBlinkDomCheckpointNodeCharacterData", legacy)
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
            1,
            first.count(
                '#include "third_party/blink/renderer/core/dom/'
                'character_data.h"\n'
            ),
        )

    def test_dom_helper_records_character_data_after_its_node(self):
        helper = INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER
        node = helper.index("a11y_recorder::RecordBlinkDomCheckpointNode(")
        data = helper.index(
            "a11y_recorder::RecordBlinkDomCheckpointNodeCharacterData("
        )
        children = helper.index("for (Node* recorder_child")
        self.assertLess(node, data)
        self.assertLess(data, children)
        self.assertIn("DynamicTo<CharacterData>(recorder_node)", helper)
        self.assertIn("recorder_character_data_count);", helper)

    def test_no_current_hook_limits_the_number_of_nodes(self):
        hooks = {
            "accessibility": INTEGRATE.CONTENT_RENDERER_ACCESSIBILITY_HOOK,
            "dom": INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER,
            "interaction": INTEGRATE.BLINK_INTERACTION_CHECKPOINT_HELPER,
            "layout": INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER,
        }
        for name, value in INTEGRATE.EARLIER_NODE_LIMITS:
            limited = f"  constexpr int {name} = {value};\n"
            unbounded = f"  constexpr int {name} = 2147483647;\n"
            owners = [hook for hook, text in hooks.items() if unbounded in text]
            with self.subTest(name=name):
                self.assertEqual(1, len(owners))
                for text in hooks.values():
                    self.assertNotIn(limited, text)

    def test_removes_the_node_limits_of_an_earlier_revision(self):
        current = INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER
        interaction = INTEGRATE.BLINK_INTERACTION_CHECKPOINT_HELPER
        earlier = current.replace(
            INTEGRATE.UNBOUNDED_NODE_COUNT_COMMENT
            + "  constexpr int kRecorderMaximumDomCheckpointNodes = 2147483647;\n",
            "  constexpr int kRecorderMaximumDomCheckpointNodes = 512;\n",
        ).replace(
            "// Records a structural checkpoint of the whole composed tree.",
            "// Records one bounded structural checkpoint of the composed tree.",
        )
        earlier_interaction = interaction.replace(
            INTEGRATE.UNBOUNDED_NODE_COUNT_COMMENT
            + "  constexpr int kRecorderMaximumInteractionTextControls = "
            "2147483647;\n",
            "  constexpr int kRecorderMaximumInteractionTextControls = 512;\n",
        )
        self.assertNotEqual(current, earlier)
        self.assertIn("one bounded structural checkpoint", earlier)
        self.assertNotEqual(interaction, earlier_interaction)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "document.cc"
            path.write_text(self.document_source(), encoding="utf-8")
            INTEGRATE.patch_blink_document(path)
            patched = path.read_text(encoding="utf-8")
            path.write_text(
                patched.replace(current, earlier).replace(
                    interaction, earlier_interaction
                ),
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_document(path)
            self.assertEqual(patched, path.read_text(encoding="utf-8"))

    def test_no_current_hook_limits_the_content_it_records(self):
        hooks = {
            "dom": INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER,
            "interaction": INTEGRATE.BLINK_INTERACTION_CHECKPOINT_HELPER,
            "attribute": INTEGRATE.BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER,
            "character data": INTEGRATE.BLINK_CHARACTER_DATA_MUTATION_HOOK,
            "text control": INTEGRATE.BLINK_TEXT_CONTROL_VALUE_HELPER,
            "layout": INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER,
            "realtime": INTEGRATE.BLINK_REALTIME_HELPER,
        }
        for indent, kind, name, value in INTEGRATE.EARLIER_CONTENT_LIMITS:
            limited = f"{indent}constexpr {kind} {name} = {value};\n"
            unbounded = (
                INTEGRATE.unbounded_content_comment(name, indent)
                + f"{indent}constexpr {kind} {name} = 2147483647;\n"
            )
            owners = [hook for hook, text in hooks.items() if unbounded in text]
            with self.subTest(name=name, indent=len(indent)):
                self.assertGreaterEqual(len(owners), 1)
                for text in hooks.values():
                    self.assertNotIn(limited, text)
        self.assertNotIn("kScanLimit", INTEGRATE.BLINK_REALTIME_HELPER)

    def test_removes_the_content_limits_of_an_earlier_revision(self):
        def earlier(text):
            for indent, kind, name, value in INTEGRATE.EARLIER_CONTENT_LIMITS:
                text = text.replace(
                    INTEGRATE.unbounded_content_comment(name, indent)
                    + f"{indent}constexpr {kind} {name} = 2147483647;\n",
                    f"{indent}constexpr {kind} {name} = {value};\n",
                )
            return text

        for hook in (
            INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER,
            INTEGRATE.BLINK_INTERACTION_CHECKPOINT_HELPER,
            INTEGRATE.BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER,
            INTEGRATE.BLINK_CHARACTER_DATA_MUTATION_HOOK,
            INTEGRATE.BLINK_TEXT_CONTROL_VALUE_HELPER,
        ):
            with self.subTest(hook=hook[:60]):
                limited = earlier(hook)
                self.assertNotEqual(hook, limited)
                self.assertEqual(
                    hook, INTEGRATE.remove_earlier_content_limits(limited)
                )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "document.cc"
            path.write_text(self.document_source(), encoding="utf-8")
            INTEGRATE.patch_blink_document(path)
            patched = path.read_text(encoding="utf-8")
            path.write_text(earlier(patched), encoding="utf-8")
            self.assertNotEqual(patched, earlier(patched))
            INTEGRATE.patch_blink_document(path)
            self.assertEqual(patched, path.read_text(encoding="utf-8"))

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
        self.assertIn(
            "kRecorderMaximumInteractionTextControls = 2147483647", helper
        )
        self.assertIn(
            "kRecorderMaximumInteractionValueLength = 2147483647", helper
        )

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
            self.assertIn(
                "  if (source != kUpdateFromParser || isConnected()) {\n",
                first,
            )
            self.assertIn("kRecorderMaximumDomValueLength = 2147483647", first)
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

    def test_records_dispatches_outside_the_node_dispatcher(self):
        """Non-Node, window, and IndexedDB dispatches open their own records.

        Blink fires these listeners without its Node event dispatcher, so
        each entry point opens a dispatch only when no hook has opened one for
        the same event, and completes only a dispatch it opened. Every call
        matches the bridge signature, and a second run changes nothing.
        """
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            event_target = root / "event_target.cc"
            self._write_event_target(event_target)
            window = root / "local_dom_window.cc"
            window.write_text(LOCAL_DOM_WINDOW_SOURCE, encoding="utf-8")
            idb = root / "idb_event_dispatcher.cc"
            idb.write_text(IDB_EVENT_DISPATCHER_SOURCE, encoding="utf-8")
            idb_build = root / "BUILD.gn"
            idb_build.write_text(IDB_BUILD_SOURCE, encoding="utf-8")
            dispatcher = root / "event_dispatcher.cc"
            self._write_event_dispatcher(dispatcher)
            signatures = INTEGRATE.parse_bridge_signatures(
                (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
                .read_text(encoding="utf-8")
            )

            def patch_all() -> dict[Path, str]:
                INTEGRATE.patch_blink_event_target(event_target)
                INTEGRATE.patch_blink_local_dom_window(window)
                INTEGRATE.patch_blink_idb_event_dispatcher(idb)
                INTEGRATE.patch_blink_idb_build(idb_build)
                INTEGRATE.patch_blink_event_dispatcher(dispatcher)
                return {
                    path: path.read_text(encoding="utf-8")
                    for path in (
                        event_target, window, idb, idb_build, dispatcher
                    )
                }

            INTEGRATE._INTEGRATED_PATHS.clear()
            try:
                first = patch_all()
                INTEGRATE.verify_integrated_sources(signatures)
                second = patch_all()
            finally:
                INTEGRATE._INTEGRATED_PATHS.clear()

        self.assertEqual(first, second)
        target_text = first[event_target]
        self.assertEqual(
            1, target_text.count(INTEGRATE.BLINK_EVENT_TARGET_DISPATCH_HOOK)
        )
        self.assertEqual(
            1, target_text.count(INTEGRATE.BLINK_EVENT_SCOPE_HELPER_MARKER)
        )
        self.assertEqual(
            1, target_text.count(INTEGRATE.BLINK_TARGET_DISPATCH_HELPER_MARKER)
        )
        # Every listener record names its execution context.
        self.assertEqual(
            3, target_text.count("RecorderEventScopeFor(recorder_context)")
        )
        # The helpers precede every hook that reads them.
        self.assertLess(
            target_text.index(INTEGRATE.BLINK_EVENT_SCOPE_HELPER_MARKER),
            target_text.index("RecorderEventScopeFor(recorder_context)"),
        )
        window_text = first[window]
        self.assertEqual(
            1, window_text.count(INTEGRATE.BLINK_WINDOW_DISPATCH_HOOK)
        )
        self.assertNotIn("  return FireEventListeners(event);\n", window_text)
        self.assertLess(
            window_text.index(INTEGRATE.BLINK_TARGET_DISPATCH_HELPER_MARKER),
            window_text.index("DispatchEventResult LocalDOMWindow::"),
        )
        idb_text = first[idb]
        self.assertEqual(
            1, idb_text.count(INTEGRATE.BLINK_IDB_DISPATCH_START_HOOK)
        )
        self.assertEqual(
            1, idb_text.count(INTEGRATE.BLINK_IDB_DISPATCH_COMPLETED_HOOK)
        )
        # The dispatch flag is declared before the first jump past it.
        self.assertLess(
            idb_text.index("const bool recorder_dispatch_opened"),
            idb_text.index("goto doneDispatching;"),
        )
        self.assertLess(
            idb_text.index("namespace blink {"),
            idb_text.index(INTEGRATE.BLINK_TARGET_DISPATCH_HELPER_MARKER),
        )
        self.assertIn(
            '  deps = [ "//chromium/recorder_bridge" ]\n', first[idb_build]
        )
        self.assertIn(
            "RecorderEventScopeFor(node_->GetExecutionContext())",
            first[dispatcher],
        )
        self.assertLess(
            first[dispatcher].index(INTEGRATE.BLINK_EVENT_SCOPE_HELPER_MARKER),
            first[dispatcher].index("DispatchEventResult EventDispatcher::"),
        )

    def test_migrates_a_dispatch_hook_that_named_no_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_dispatcher.cc"
            self._write_event_dispatcher(path)
            INTEGRATE.patch_blink_event_dispatcher(path)
            current = path.read_text(encoding="utf-8")

            without_scope = current.replace(
                INTEGRATE.BLINK_DISPATCH_HOOK,
                INTEGRATE.LEGACY_BLINK_DISPATCH_HOOK_WITHOUT_EVENT_SCOPE,
                1,
            ).replace(INTEGRATE.BLINK_EVENT_SCOPE_HELPER, "", 1)
            self.assertNotIn("RecorderEventScopeFor(", without_scope)
            path.write_text(without_scope, encoding="utf-8")

            INTEGRATE.patch_blink_event_dispatcher(path)

            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_migrates_listener_hooks_that_named_no_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event_target.cc"
            self._write_event_target(path)
            INTEGRATE.patch_blink_event_target(path)
            current = path.read_text(encoding="utf-8")

            without_scope = (
                current.replace(
                    ",\n          RecorderEventScopeFor(recorder_context));", ");"
                )
                .replace(
                    ",\n        RecorderEventScopeFor(recorder_context));", ");"
                )
                .replace(INTEGRATE.BLINK_TARGET_DISPATCH_HELPER, "", 1)
                .replace(INTEGRATE.BLINK_EVENT_SCOPE_HELPER, "", 1)
                .replace(
                    INTEGRATE.BLINK_EVENT_TARGET_DISPATCH_HOOK,
                    INTEGRATE.BLINK_EVENT_TARGET_DISPATCH_ANCHOR,
                    1,
                )
            )
            self.assertNotIn("RecorderEventScopeFor(", without_scope)
            path.write_text(without_scope, encoding="utf-8")

            INTEGRATE.patch_blink_event_target(path)

            self.assertEqual(current, path.read_text(encoding="utf-8"))

    def _write_event_target(self, path: Path) -> None:
        path.write_text(
            '#include "third_party/blink/renderer/core/dom/events/'
            'event_target.h"\n'
            '#include "base/time/time.h"\n'
            "\n"
            "namespace blink {\n"
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
            "}\n"
            + EVENT_TARGET_DISPATCH_SOURCE,
            encoding="utf-8",
        )

    def _write_event_dispatcher(self, path: Path) -> None:
        path.write_text(
            '#include "third_party/blink/renderer/core/dom/events/'
            'event_dispatcher.h"\n'
            '#include "build/build_config.h"\n'
            "\n"
            + EVENT_DISPATCHER_DISPATCH_EVENT_SOURCE
            + "DispatchEventResult EventDispatcher::Dispatch() {\n"
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


# The end of namespace blink, which the layout change definition precedes.
BLINK_NAMESPACE_END = "}  // namespace blink\n"


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

    def test_evidence_queue_passes_its_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "evidence_queue_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    "-pthread",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "evidence_queue.cc"),
                    str(bridge / "evidence_queue_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_evidence_cost_passes_its_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "evidence_cost_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    "-pthread",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "evidence_cost.cc"),
                    str(bridge / "evidence_cost_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_layout_changes_pass_their_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "layout_changes_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "layout_changes.cc"),
                    str(bridge / "layout_changes_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_full_walks_pass_their_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "full_walks_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "full_walks.cc"),
                    str(bridge / "full_walks_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_animation_settings_pass_their_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "animation_settings_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "animation_settings_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_popup_widget_shown_records_the_windows_animation_settings(self):
        bridge = MODULE_PATH.parent / "recorder_bridge"
        source = (bridge / "browser_bridge.cc").read_text(encoding="utf-8")
        build = (bridge / "BUILD.gn").read_text(encoding="utf-8")
        self.assertIn('"animation_settings.h",', build)
        for action in (
            "SPI_GETCLIENTAREAANIMATION",
            "SPI_GETUIEFFECTS",
            "SPI_GETMENUANIMATION",
            "SPI_GETMENUFADE",
            "SPI_GETCOMBOBOXANIMATION",
        ):
            self.assertIn(action, source)
        self.assertIn(
            'payload.Set("windowsAnimationSettings", WindowsAnimationSettingsValue());',
            source,
        )

    def test_bridge_walks_documents_only_where_the_schedule_asks(self):
        bridge = MODULE_PATH.parent / "recorder_bridge"
        source = (bridge / "browser_bridge.cc").read_text(encoding="utf-8")
        protocol = (bridge / "recorder_protocol.cc").read_text(encoding="utf-8")
        build = (bridge / "BUILD.gn").read_text(encoding="utf-8")
        self.assertIn('"full_walks.cc",', build)
        self.assertIn('"full_walks.h",', build)
        # The interval is required in the bootstrap and passed to children.
        self.assertIn('value.FindInt("fullWalkInterval")', protocol)
        self.assertIn('value.Set("fullWalkInterval"', protocol)
        self.assertIn("SetFullWalkInterval(configuration.full_walk_interval);", source)
        # Each checkpoint names why it was walked.
        self.assertEqual(2, source.count('payload.Set("walkReason"'))
        dom = source[source.index("uint64_t BeginBlinkDomCheckpoint("):]
        self.assertLess(
            dom.index("DomWalkReason(document_node_id, reason)"),
            dom.index("AssignDomCheckpointIdentity()"),
        )
        layout = source[source.index("uint64_t BeginBlinkLayoutCheckpoint("):]
        self.assertLess(
            layout.index("LayoutWalkReason(document_node_id)"),
            layout.index("storage.next_checkpoint_id++"),
        )
        # A lost record of either channel is counted before it is reported.
        held = source[source.index("void HoldOmittedEvidence("):]
        self.assertLess(
            held.index("CountEvidenceLoss(channel, count);"),
            held.index("OmittedEvidenceCounts()"),
        )
        # A change set is the source of presentation and interaction records.
        self.assertIn('payload.Set("layoutChangeSetId"', source)
        self.assertIn('payload.Set("sourceChangeSetId"', source)
        self.assertIn("LayoutChangeSetSource(change_set_sequence)", source)

    def test_a_change_set_without_a_checkpoint_records_the_update(self):
        definition = INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION
        self.assertNotIn(INTEGRATE.BLINK_LAYOUT_CHANGES_SOURCE_CALL, definition)
        hook = definition.index(INTEGRATE.BLINK_LAYOUT_CHANGES_SOURCE_HOOK)
        tail = definition[hook:]
        self.assertLess(
            tail.index("a11y_recorder::RecordBlinkLayoutChanges("),
            tail.index("RecorderRequestLayoutPresentation("),
        )
        self.assertLess(
            tail.index("RecorderRequestLayoutPresentation("),
            tail.index("RecorderRecordInteractionCheckpoint("),
        )
        self.assertIn("if (recorder_change_set_source == 0) {", tail)
        self.assertIn(
            INTEGRATE.LEGACY_UNSOURCED_BLINK_LAYOUT_CHANGES_DEFINITION,
            INTEGRATE.BLINK_LAYOUT_CHANGES_LEGACY_DEFINITIONS,
        )

    def test_a_change_record_states_the_bounds_of_each_quad(self):
        definition = INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION
        geometry = INTEGRATE.BLINK_LAYOUT_CHANGES_QUAD_RECT_GEOMETRY
        self.assertIn(geometry, definition)
        self.assertNotIn(
            INTEGRATE.BLINK_LAYOUT_CHANGES_SINGLE_RECT_GEOMETRY, definition
        )
        # The quads are read whatever the projection, since a later
        # transform can rotate a node whose record is not repeated.
        self.assertLess(
            geometry.index("Vector<gfx::QuadF> recorder_quads;"),
            geometry.index("Preserves2dAxisAlignment()"),
        )
        self.assertIn("if (recorder_quads.size() > 1) {", geometry)
        self.assertIn("recorder_changed.local_quad_rects.push_back(", geometry)
        self.assertIn(
            INTEGRATE.LEGACY_SINGLE_RECT_BLINK_LAYOUT_CHANGES_DEFINITION,
            INTEGRATE.BLINK_LAYOUT_CHANGES_LEGACY_DEFINITIONS,
        )

    def test_a_single_rect_definition_is_upgraded(self):
        path = Path("local_frame_view.cc")
        text = (
            '#include "third_party/blink/renderer/core/frame/local_frame_view.h"\n'
            + INTEGRATE.BLINK_LAYOUT_CHANGES_DECLARATION
            + INTEGRATE.BLINK_LAYOUT_CHANGES_SCROLL_DECLARATION
            + INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
            + INTEGRATE.BLINK_LAYOUT_CHANGES_HOOK
            + INTEGRATE.LEGACY_SINGLE_RECT_BLINK_LAYOUT_CHANGES_DEFINITION
            + "}  // namespace blink\n"
        )
        upgraded = INTEGRATE.add_layout_changes_to_local_frame_view(text, path)
        self.assertEqual(
            upgraded.count(INTEGRATE.BLINK_LAYOUT_CHANGES_QUAD_RECT_GEOMETRY), 1
        )
        self.assertNotIn(
            INTEGRATE.BLINK_LAYOUT_CHANGES_SINGLE_RECT_GEOMETRY, upgraded
        )
        self.assertEqual(
            INTEGRATE.add_layout_changes_to_local_frame_view(upgraded, path),
            upgraded,
        )

    def test_dom_helper_records_the_interaction_state_of_an_unwalked_delivery(
        self,
    ):
        helper = INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER
        skipped = helper.index("if (recorder_checkpoint_sequence == 0) {")
        body = helper[skipped:helper.index("    return;\n  }\n", skipped)]
        self.assertIn('std::string_view(recorder_reason) == "post-mutation"', body)
        self.assertIn(
            'RecorderRecordInteractionCheckpoint(recorder_document, 0, "browser.dom",',
            body,
        )
        self.assertIn(
            "#include <string_view>", INTEGRATE.BLINK_DOM_CHECKPOINT_INCLUDES
        )

    def test_bridge_measures_the_cost_of_each_kind_of_evidence(self):
        bridge = MODULE_PATH.parent / "recorder_bridge"
        bridge_source = (bridge / "browser_bridge.cc").read_text(
            encoding="utf-8"
        )
        protocol_source = (bridge / "recorder_protocol.cc").read_text(
            encoding="utf-8"
        )
        build = (bridge / "BUILD.gn").read_text(encoding="utf-8")
        header = (bridge / "browser_bridge.h").read_text(encoding="utf-8")

        self.assertIn('"evidence_cost.cc",', build)
        self.assertIn(
            "SetCostReporter(&WriteCostReport, kCostReportIntervalNanoseconds)",
            bridge_source,
        )
        # Every entry point that records evidence is measured under its name.
        for name in re.findall(r"^\w[\w:<>*& ]*?\b((?:Record|Begin|Complete|Note)\w+)\(",
                               header, re.M):
            with self.subTest(name=name):
                self.assertIn(f'A11Y_RECORDER_COST("{name}");', bridge_source)
        for span in (
            "span:dispatch-path",
            "span:dom-checkpoint",
            "span:accessibility-checkpoint",
            "span:interaction-checkpoint",
            "span:layout-checkpoint",
        ):
            with self.subTest(span=span):
                self.assertEqual(
                    bridge_source.count(f'CostSpanSlot("{span}")'),
                    3 if span == "span:dispatch-path" else 2,
                )
        self.assertIn('RegisterCostKind("queue.push-waited")', protocol_source)
        self.assertIn('A11Y_RECORDER_COST("writer.write");', protocol_source)
        for part in ("writer.payload", "writer.serialize", "writer.pipe-write"):
            with self.subTest(part=part):
                self.assertIn(f'RegisterCostKind("{part}")', protocol_source)
        self.assertIn('RegisterCountKind("count:writer.bytes")', protocol_source)

    def test_bridge_writes_evidence_from_its_writer_thread(self):
        protocol_source = (
            MODULE_PATH.parent / "recorder_bridge" / "recorder_protocol.cc"
        ).read_text(encoding="utf-8")
        bridge_source = (
            MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.cc"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "base::PlatformThread::Create(0, this, &writer_thread_)",
            protocol_source,
        )
        self.assertIn(
            "queue_.Push(std::move(evidence), &StampEvidence, &waited)",
            protocol_source,
        )
        # Records are timestamped by the queue, in queue order, never by the
        # observing thread before it reaches the queue.
        self.assertNotIn("browser_timestamp_ticks =", bridge_source)
        self.assertIn(
            "client->SetWriteFailureHandler(&HoldFailedEvidenceWrite);",
            bridge_source,
        )
        self.assertIn(
            "QueueBlinkEvidence(client, std::move(evidence));", bridge_source
        )


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

    def test_patches_option_selectedness_idempotently(self):
        patched = self.assert_patched(
            "html_option_element.cc",
            '#include "third_party/blink/renderer/core/html/forms/'
            'html_option_element.h"\n',
            (
                INTEGRATE.BLINK_OPTION_SELECTEDNESS_HELPER_ANCHOR,
                INTEGRATE.BLINK_OPTION_SELECTEDNESS_ANCHOR,
            ),
            INTEGRATE.patch_blink_option_element,
            (INTEGRATE.BLINK_OPTION_SELECTEDNESS_HOOK,),
            (
                INTEGRATE.BLINK_COOKIE_ORIGIN_HELPER_MARKER,
                INTEGRATE.BLINK_OPTION_SELECTEDNESS_HELPER_MARKER,
            ),
        )
        # The record follows the state change, after the early return for an
        # unchanged state.
        hook = INTEGRATE.BLINK_OPTION_SELECTEDNESS_HOOK
        self.assertLess(
            hook.index("is_selected_ = selected;"),
            hook.index("RecorderRecordOptionSelectedness("),
        )

    def test_patches_the_page_popup_idempotently(self):
        # In the order Chromium's file holds them.
        hooks = [anchor for anchor, _ in INTEGRATE.BLINK_PAGE_POPUP_HOOKS]
        anchors = [INTEGRATE.BLINK_PAGE_POPUP_HELPER_ANCHOR, hooks[0],
                   hooks[1], INTEGRATE.BLINK_PAGE_POPUP_REQUEST_ANCHOR,
                   *hooks[2:]]
        source = cookie_source(
            INTEGRATE.BLINK_PAGE_POPUP_OWN_INCLUDE + "\n", *anchors
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "web_page_popup_impl.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_page_popup(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_page_popup(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        for _, hook in INTEGRATE.BLINK_PAGE_POPUP_HOOKS:
            with self.subTest(hook=hook.splitlines()[0]):
                self.assertEqual(1, first.count(hook))
        for include_line in INTEGRATE.BLINK_PAGE_POPUP_INCLUDES:
            with self.subTest(include=include_line):
                self.assertEqual(1, first.count(include_line + "\n"))
        self.assertEqual(
            1, first.count(INTEGRATE.BLINK_PAGE_POPUP_HELPER_MARKER)
        )
        self.assertEqual(
            1, first.count(INTEGRATE.BLINK_PAGE_POPUP_REQUEST_MARKER)
        )
        self.assert_bridge_calls_match(first)
        # The helpers come before the chrome client that uses them, and the
        # request after it.
        self.assertLess(
            first.index(INTEGRATE.BLINK_PAGE_POPUP_HELPER_MARKER),
            first.index(INTEGRATE.BLINK_PAGE_POPUP_HELPER_ANCHOR),
        )
        self.assertLess(
            first.index(INTEGRATE.BLINK_PAGE_POPUP_CLIENT_HOOK),
            first.index(INTEGRATE.BLINK_PAGE_POPUP_REQUEST_MARKER),
        )

    def test_a_2a3939b_popup_type_read_is_upgraded(self):
        hooks = [anchor for anchor, _ in INTEGRATE.BLINK_PAGE_POPUP_HOOKS]
        anchors = [INTEGRATE.BLINK_PAGE_POPUP_HELPER_ANCHOR, hooks[0],
                   hooks[1], INTEGRATE.BLINK_PAGE_POPUP_REQUEST_ANCHOR,
                   *hooks[2:]]
        legacy_helper = INTEGRATE.BLINK_PAGE_POPUP_HELPER.replace(
            INTEGRATE.STAGE_2A3939B_PAGE_POPUP_TYPE_FIX,
            INTEGRATE.STAGE_2A3939B_PAGE_POPUP_TYPE_READ,
        )
        self.assertNotEqual(legacy_helper, INTEGRATE.BLINK_PAGE_POPUP_HELPER)
        source = cookie_source(
            INTEGRATE.BLINK_PAGE_POPUP_OWN_INCLUDE + "\n", *anchors
        ).replace(
            INTEGRATE.BLINK_PAGE_POPUP_HELPER_ANCHOR,
            legacy_helper + INTEGRATE.BLINK_PAGE_POPUP_HELPER_ANCHOR,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "web_page_popup_impl.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_page_popup(path)
            patched = path.read_text(encoding="utf-8")
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_PAGE_POPUP_HELPER))
        self.assertNotIn("recorder_input->FormControlTypeAsString()", patched)

    def test_the_page_popup_patch_fails_when_an_anchor_is_absent(self):
        # In the order Chromium's file holds them.
        hooks = [anchor for anchor, _ in INTEGRATE.BLINK_PAGE_POPUP_HOOKS]
        anchors = [INTEGRATE.BLINK_PAGE_POPUP_HELPER_ANCHOR, hooks[0],
                   hooks[1], INTEGRATE.BLINK_PAGE_POPUP_REQUEST_ANCHOR,
                   *hooks[2:]]
        for missing in range(len(anchors)):
            kept = anchors[:missing] + anchors[missing + 1:]
            with self.subTest(missing=anchors[missing].splitlines()[0]):
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / "web_page_popup_impl.cc"
                    path.write_text(
                        cookie_source(
                            INTEGRATE.BLINK_PAGE_POPUP_OWN_INCLUDE + "\n",
                            *kept,
                        ),
                        encoding="utf-8",
                    )
                    with self.assertRaises(RuntimeError):
                        INTEGRATE.patch_blink_page_popup(path)

    def test_a_043_page_popup_is_upgraded_to_044(self):
        # A checkout patched by protocol 0.43 has the owner record without the
        # owner frame token, the requested rectangle with its source, and the
        # placed hook in SetScreenRects, which 0.44 removes.
        hooks = [anchor for anchor, _ in INTEGRATE.BLINK_PAGE_POPUP_HOOKS]
        anchors = [INTEGRATE.BLINK_PAGE_POPUP_HELPER_ANCHOR, hooks[0],
                   hooks[1], INTEGRATE.BLINK_PAGE_POPUP_REQUEST_ANCHOR,
                   *hooks[2:]]
        legacy_helper = INTEGRATE.BLINK_PAGE_POPUP_HELPER.replace(
            INTEGRATE.STAGE_044_PAGE_POPUP_OPENED_FN,
            INTEGRATE.LEGACY_043_PAGE_POPUP_OPENED_FN,
        ).replace(
            INTEGRATE.STAGE_044_PAGE_POPUP_WINDOW_RECT_FN,
            INTEGRATE.LEGACY_043_PAGE_POPUP_WINDOW_RECT_FN,
        )
        self.assertNotIn("GetLocalFrameToken", legacy_helper)
        source = cookie_source(
            INTEGRATE.BLINK_PAGE_POPUP_OWN_INCLUDE + "\n",
            *anchors,
            INTEGRATE.LEGACY_043_PAGE_POPUP_SCREEN_RECTS_ORIGINAL,
        ).replace(
            INTEGRATE.BLINK_PAGE_POPUP_HELPER_ANCHOR,
            legacy_helper + INTEGRATE.BLINK_PAGE_POPUP_HELPER_ANCHOR,
        ).replace(
            INTEGRATE.BLINK_PAGE_POPUP_WINDOW_RECT_ANCHOR,
            INTEGRATE.LEGACY_043_PAGE_POPUP_WINDOW_RECT_HOOK,
        ).replace(
            INTEGRATE.LEGACY_043_PAGE_POPUP_SCREEN_RECTS_ORIGINAL,
            INTEGRATE.LEGACY_043_PAGE_POPUP_SCREEN_RECTS_HOOK,
        )
        patched = self.patch_twice(
            "web_page_popup_impl.cc", source, INTEGRATE.patch_blink_page_popup
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_PAGE_POPUP_HELPER))
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_PAGE_POPUP_WINDOW_RECT_HOOK)
        )
        self.assertNotIn('"placed"', patched)
        self.assertNotIn('"requested"', patched)
        self.assertEqual(
            1,
            patched.count(
                INTEGRATE.LEGACY_043_PAGE_POPUP_SCREEN_RECTS_ORIGINAL
            ),
        )
        self.assert_bridge_calls_match(patched)

    def browser_popup_widget_cases(self):
        return (
            (
                "render_frame_host_impl.cc",
                '#include "content/browser/renderer_host/'
                'render_frame_host_impl.h"\n',
                (INTEGRATE.CONTENT_POPUP_WIDGET_CREATED_ANCHOR,),
                INTEGRATE.patch_content_popup_widget_created,
            ),
            (
                "web_contents_impl.cc",
                '#include "content/browser/web_contents/web_contents_impl.h"\n',
                (
                    INTEGRATE.CONTENT_POPUP_WIDGET_SHOWN_HELPER_ANCHOR,
                    *(a for a, _ in INTEGRATE.CONTENT_POPUP_WIDGET_SHOWN_HOOKS),
                ),
                INTEGRATE.patch_content_popup_widget_shown,
            ),
            (
                "render_widget_host_impl.cc",
                INTEGRATE.CONTENT_WIDGET_HOST_OWN_INCLUDE + "\n",
                (
                    INTEGRATE.CONTENT_WIDGET_HOST_HELPER_ANCHOR,
                    *(a for a, _ in INTEGRATE.CONTENT_WIDGET_HOST_HOOKS),
                ),
                INTEGRATE.patch_content_render_widget_host,
            ),
            (
                "render_widget_host_view_aura.cc",
                INTEGRATE.CONTENT_WIDGET_VIEW_OWN_INCLUDE + "\n",
                tuple(a for a, _ in INTEGRATE.CONTENT_WIDGET_VIEW_HOOKS),
                INTEGRATE.patch_content_render_widget_host_view,
            ),
        )

    def test_a_popup_window_is_recorded_hidden_only_when_it_was_shown(self):
        # Protocol 0.45: each hook reads whether the window was shown before
        # hiding it, and records after it is hidden.
        for anchor, hook in INTEGRATE.CONTENT_WIDGET_VIEW_HOOKS:
            with self.subTest(hook=hook[:40]):
                self.assertLess(
                    hook.index("window_->TargetVisibility()"),
                    hook.index("window_->Hide();"),
                )
                self.assertLess(
                    hook.index("window_->Hide();"),
                    hook.index("RecorderRecordPopupWidgetHidden("),
                )
                self.assertIn("widget_type_ == WidgetType::kPopup", hook)
        self.assertIn('"hidden");', INTEGRATE.CONTENT_WIDGET_VIEW_HIDE_HOOK)
        self.assertIn(
            '"destroyed");', INTEGRATE.CONTENT_WIDGET_VIEW_CLEAN_UP_HOOK
        )

    def test_the_browser_popup_widget_hooks_are_written_once(self):
        for name, include, anchors, patch in self.browser_popup_widget_cases():
            with self.subTest(name=name):
                patched = self.patch_twice(
                    name, cookie_source(include, *anchors), patch
                )
                self.assertEqual(
                    1, patched.count(INTEGRATE.CONTENT_NAVIGATION_INCLUDE)
                )
                self.assert_bridge_calls_match(patched)

    def test_the_browser_popup_widget_patch_fails_when_an_anchor_is_absent(
        self,
    ):
        for name, include, anchors, patch in self.browser_popup_widget_cases():
            for missing in range(len(anchors)):
                kept = anchors[:missing] + anchors[missing + 1:]
                with self.subTest(name=name, missing=missing):
                    with tempfile.TemporaryDirectory() as directory:
                        path = Path(directory) / name
                        path.write_text(
                            cookie_source(include, *kept), encoding="utf-8"
                        )
                        with self.assertRaises(RuntimeError):
                            patch(path)

    def test_a_refused_popup_is_recorded_before_it_is_destroyed(self):
        for hook in (
            INTEGRATE.CONTENT_POPUP_WIDGET_INACTIVE_HOOK,
            INTEGRATE.CONTENT_POPUP_WIDGET_NOT_VISIBLE_HOOK,
            INTEGRATE.CONTENT_POPUP_WIDGET_EXCLUSION_HOOK,
        ):
            self.assertLess(
                hook.index("RecorderRecordPopupWidgetShown("),
                hook.index("ShutdownAndDestroyWidget(true);"),
            )
        # The transformed rectangle is kept before ConstrainPopupBounds
        # replaces it.
        constrain = INTEGRATE.CONTENT_POPUP_WIDGET_CONSTRAIN_HOOK
        self.assertLess(
            constrain.index("recorder_transformed_rect = transformed_rect;"),
            constrain.index("ConstrainPopupBounds(transformed_rect)"),
        )

    def test_a_popup_is_closed_once_whichever_path_runs(self):
        # Close records only when its cancel left the page in place, which is
        # when ClosePopup, and so its record, did not run.
        hook = INTEGRATE.BLINK_PAGE_POPUP_BROWSER_CLOSE_HOOK
        self.assertLess(
            hook.index("ClearPagePopupClient();"),
            hook.index('RecorderRecordPagePopupClosed(page_.Get(), "browser");'),
        )
        self.assertIn(
            'running_inside_close ? "browser" : "renderer"',
            INTEGRATE.BLINK_PAGE_POPUP_CLOSE_HOOK,
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
                BLINK_NAMESPACE_END,
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

    def test_adds_the_layout_change_set_to_the_local_frame_view(self):
        patched = self.patch_twice(
            cookie_source(
                self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
                INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
                INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
                BLINK_NAMESPACE_END,
            )
        )
        # The change set follows the checkpoint of the same update, in the
        # hook that replaces the checkpoint-only hook.
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_LAYOUT_CHANGES_HOOK))
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_LAYOUT_CHANGES_DECLARATION)
        )
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION)
        )
        self.assertLess(
            patched.index(INTEGRATE.BLINK_LAYOUT_CHANGES_DECLARATION),
            patched.index(INTEGRATE.BLINK_LAYOUT_CHANGES_HOOK),
        )
        self.assertLess(
            patched.index(INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION),
            patched.index(BLINK_NAMESPACE_END),
        )
        hook = INTEGRATE.BLINK_LAYOUT_CHANGES_HOOK
        self.assertLess(
            hook.index("RecorderRecordLayoutCheckpoint("),
            hook.index("RecorderRecordLayoutChanges("),
        )
        for include_line in INTEGRATE.BLINK_LAYOUT_CHANGES_INCLUDES:
            with self.subTest(include=include_line):
                self.assertEqual(1, patched.count(include_line + "\n"))

    def test_upgrades_an_earlier_layout_change_definition_in_place(self):
        source = cookie_source(
            self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
            BLINK_NAMESPACE_END,
        )
        current = self.patch_twice(source)
        for legacy in INTEGRATE.BLINK_LAYOUT_CHANGES_LEGACY_DEFINITIONS:
            with self.subTest(legacy=legacy[:40]):
                self.assertNotEqual(INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION, legacy)
                earlier = current.replace(
                    INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION, legacy, 1
                )
                self.assertNotEqual(current, earlier)
                self.assertEqual(current, self.patch_twice(earlier))

    def test_the_change_set_reads_held_geometry_without_forcing_work(self):
        definition = INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION
        self.assertIn("GetBoundingClientRectNoLifecycleUpdateNoAdjustment", definition)
        self.assertIn("GeometryMapper::SourceToDestinationProjection(", definition)
        self.assertIn("MatrixWithOriginApplied()", definition)
        self.assertIn("a11y_recorder::RecordBlinkLayoutChanges(", definition)
        # A child whose layout result is reused is noted with its parent's
        # new result, since its resolved insets and margins can change.
        self.assertIn("recorder_fragment.Children()", definition)
        # Noted nodes are held weakly, so a noted node that is collected is
        # never kept alive by the recorder.
        self.assertIn("WeakMember<const Node>", definition)
        for forcing in ("UpdateStyleAndLayout", "UpdateAllLifecyclePhases",
                        "EnsureComputedStyle", "getBoundingClientRect("):
            with self.subTest(forcing=forcing):
                self.assertNotIn(forcing, definition)

    def test_adds_each_layout_change_note_once(self):
        for name, declaration, hooks in (
            (
                "element.cc",
                INTEGRATE.BLINK_LAYOUT_CHANGE_NOTE_DECLARATION,
                INTEGRATE.BLINK_ELEMENT_LAYOUT_CHANGE_HOOKS,
            ),
            (
                "text.cc",
                INTEGRATE.BLINK_LAYOUT_CHANGE_NOTE_DECLARATION,
                INTEGRATE.BLINK_TEXT_LAYOUT_CHANGE_HOOKS,
            ),
            (
                "node.cc",
                INTEGRATE.BLINK_LAYOUT_CHANGE_NOTE_DECLARATION,
                INTEGRATE.BLINK_NODE_LAYOUT_CHANGE_HOOKS,
            ),
            (
                "style_engine.cc",
                INTEGRATE.BLINK_LAYOUT_CHANGE_NOTE_DECLARATION,
                INTEGRATE.BLINK_STYLE_ENGINE_LAYOUT_CHANGE_HOOKS,
            ),
            (
                "layout_object.cc",
                INTEGRATE.BLINK_LAYOUT_CHANGE_NOTE_DECLARATION,
                INTEGRATE.BLINK_LAYOUT_OBJECT_LAYOUT_CHANGE_HOOKS,
            ),
            (
                "layout_box.cc",
                INTEGRATE.BLINK_LAYOUT_RESULT_NOTE_DECLARATION,
                INTEGRATE.BLINK_LAYOUT_BOX_LAYOUT_CHANGE_HOOKS,
            ),
            (
                "pre_paint_tree_walk.cc",
                INTEGRATE.BLINK_LAYOUT_OBJECT_CHANGE_NOTE_DECLARATION,
                INTEGRATE.BLINK_PRE_PAINT_LAYOUT_CHANGE_HOOKS,
            ),
            (
                "paint_layer_scrollable_area.cc",
                INTEGRATE.BLINK_SCROLL_OFFSET_NOTE_DECLARATION,
                INTEGRATE.BLINK_PAINT_LAYER_SCROLLABLE_AREA_HOOKS,
            ),
            (
                "element.cc",
                INTEGRATE.BLINK_DOM_CHANGE_DECLARATION,
                INTEGRATE.BLINK_ELEMENT_DOM_CHANGE_HOOKS,
            ),
            (
                "shadow_root.cc",
                INTEGRATE.BLINK_DOM_CHANGE_DECLARATION,
                (
                    (
                        INTEGRATE.BLINK_SHADOW_ROOT_REFERENCE_TARGET_ANCHOR,
                        INTEGRATE.BLINK_SHADOW_ROOT_REFERENCE_TARGET_HOOK,
                    ),
                ),
            ),
            (
                "slot_assignment.cc",
                INTEGRATE.BLINK_DOM_CHANGE_DECLARATION,
                (
                    (
                        INTEGRATE.BLINK_SLOT_ASSIGNMENT_ANCHOR,
                        INTEGRATE.BLINK_SLOT_ASSIGNMENT_HOOK,
                    ),
                ),
            ),
        ):
            with self.subTest(file=name):
                source = cookie_source(
                    "namespace blink {\n", *(anchor for anchor, _ in hooks)
                )
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / name
                    path.write_text(source, encoding="utf-8")
                    INTEGRATE.patch_blink_layout_change_notes(
                        path, declaration, hooks
                    )
                    first = path.read_text(encoding="utf-8")
                    INTEGRATE.patch_blink_layout_change_notes(
                        path, declaration, hooks
                    )
                    self.assertEqual(first, path.read_text(encoding="utf-8"))
                self.assertEqual(1, first.count(declaration))
                self.assertLess(
                    first.index(declaration), first.index(hooks[0][1])
                )
                for _, hook in hooks:
                    self.assertEqual(1, first.count(hook))

    def test_records_style_attribute_changes_made_through_the_cssom(self):
        helper = INTEGRATE.BLINK_ELEMENT_STYLE_ATTRIBUTE_HELPER
        # The attribute is written as getAttribute() writes it, and the
        # change is recorded without queuing a DOM checkpoint.
        self.assertIn("getAttribute(html_names::kStyleAttr)", helper)
        self.assertIn("AttributesWithoutUpdate()", helper)
        self.assertIn("RecordBlinkDomAttributeChanged(", helper)
        self.assertNotIn("EnqueueRecorderDomCheckpoint", helper)
        self.assertIn("isConnected()", helper)
        self.assertIn("RecorderRecordsDomChanges(", helper)
        # Every CSSOM path reaches InvalidateStyleAttribute, which reads the
        # held text before the change and records after it.
        start = INTEGRATE.BLINK_ELEMENT_INVALIDATE_STYLE_ATTRIBUTE_START_HOOK
        end = INTEGRATE.BLINK_ELEMENT_INVALIDATE_STYLE_ATTRIBUTE_END_HOOK
        self.assertIn("RecorderHeldStyleAttribute(*this)", start)
        self.assertIn("!g_recorder_inline_style_changing", end)
        # InlineStyleChanged records only after its mutation observers'
        # record is queued, so their old value is the one Blink gives them.
        inline_end = INTEGRATE.BLINK_ELEMENT_INLINE_STYLE_CHANGED_END_HOOK
        self.assertLess(
            inline_end.index("SynchronizeAttribute(html_names::kStyleAttr);"),
            inline_end.index("RecorderRecordStyleAttributeChange("),
        )
        inline_start = INTEGRATE.BLINK_ELEMENT_INLINE_STYLE_CHANGED_START_HOOK
        self.assertLess(
            inline_start.index("g_recorder_inline_style_changing = true;"),
            inline_start.index("InvalidateStyleAttribute("),
        )
        self.assertLess(
            inline_start.index("InvalidateStyleAttribute("),
            inline_start.index("g_recorder_inline_style_changing = false;"),
        )

    def test_records_a_shadow_root_again_once_its_flags_are_set(self):
        for hook in (
            INTEGRATE.BLINK_ELEMENT_SHADOW_ROOT_FLAGS_HOOK,
            INTEGRATE.BLINK_ELEMENT_DECLARATIVE_SHADOW_ROOT_FLAGS_HOOK,
        ):
            with self.subTest(hook=hook):
                self.assertLess(
                    hook.index("SetAvailableToElementInternals("),
                    hook.index("RecorderRecordDomShadowRootChanged(shadow_root);"),
                )

    def test_upgrades_a_light_tree_helper_in_place(self):
        """A helper written before the composed traversal is rewritten."""
        source = cookie_source(
            self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
                BLINK_NAMESPACE_END,
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

    def test_the_helper_measures_its_traversal_and_reuses_style_readings(self):
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        self.assertIn(
            "a11y_recorder::RecordBlinkLayoutCheckpointCost(recorder_cost);",
            helper,
        )
        self.assertLess(
            helper.index("RecordBlinkLayoutCheckpointCost(recorder_cost)"),
            helper.index("a11y_recorder::CompleteBlinkLayoutCheckpoint("),
        )
        self.assertIn(".IsLayoutDependent(recorder_style,", helper)
        # One place reads a value; every listed value is recorded for every
        # element, whether read or copied from the previous reading.
        self.assertEqual(
            1, helper.count(".CSSValueFromComputedStyle(")
        )
        self.assertIn(
            "recorder_record.computed_style.push_back(std::move(recorder_entry));",
            helper,
        )
        # A reading is reused only for the same held style object, never for
        # a value that depends on layout in either reading, and never in a
        # verifying checkpoint, which reads every value and compares.
        self.assertIn("Persistent<const ComputedStyle> style;", helper)
        self.assertIn(
            "recorder_previous->second.style.Get() == recorder_style", helper
        )
        self.assertIn(
            "recorder_reuse && !recorder_layout_dependent &&\n"
            "            !recorder_reading.layout_dependent[recorder_slot];",
            helper,
        )
        self.assertIn("if (recorder_reusable && !recorder_verify_styles) {", helper)
        self.assertIn("kRecorderStyleVerificationInterval = 10;", helper)
        self.assertIn(
            "RecordBlinkLayoutStyleReuseDifference(\n"
            "                recorder_entry.property_name);",
            helper,
        )
        # The measurement reads clocks and counts; it never forces work.
        for forcing in ("UpdateStyleAndLayout", "UpdateAllLifecyclePhases",
                        "EnsureComputedStyle"):
            with self.subTest(forcing=forcing):
                self.assertNotIn(forcing, helper)

    def test_upgrades_a_helper_that_indexes_the_property_array(self):
        legacy_helper = INTEGRATE.LEGACY_FIXED_LIST_BLINK_LAYOUT_CHECKPOINT_HELPER
        for legacy, current in INTEGRATE.BLINK_LAYOUT_CHECKPOINT_LEGACY_STYLE_LOOPS:
            legacy_helper = legacy_helper.replace(current, legacy, 1)
        self.assertRegex(legacy_helper, r"kRecorderLayoutStyleProperties\[\w")
        source = cookie_source(
            self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
                BLINK_NAMESPACE_END,
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
        fixed_list_helper = (
            INTEGRATE.LEGACY_FIXED_LIST_BLINK_LAYOUT_CHECKPOINT_HELPER
        )
        earlier_helper = fixed_list_helper.replace(
            INTEGRATE.BLINK_LAYOUT_STYLE_PROPERTY_ARRAY, earlier_array, 1
        )
        indexed_helper = earlier_helper
        for legacy, current in INTEGRATE.BLINK_LAYOUT_CHECKPOINT_LEGACY_STYLE_LOOPS:
            indexed_helper = indexed_helper.replace(current, legacy, 1)
        self.assertNotEqual(fixed_list_helper, earlier_helper)
        self.assertNotEqual(earlier_helper, indexed_helper)
        source = cookie_source(
            self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
                BLINK_NAMESPACE_END,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "local_frame_view.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_local_frame_view(path)
            current = path.read_text(encoding="utf-8")
            for older_helper in (fixed_list_helper, earlier_helper, indexed_helper):
                with self.subTest(indexed=older_helper is indexed_helper):
                    path.write_text(
                        current.replace(current_helper, older_helper, 1),
                        encoding="utf-8",
                    )
                    INTEGRATE.patch_blink_local_frame_view(path)
                    self.assertEqual(current, path.read_text(encoding="utf-8"))

    def test_the_helper_holds_no_fixed_property_array(self):
        # Protocol 0.37 reads the list getComputedStyle() uses at run time,
        # and a checkout holding the fixed array is upgraded whole.
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        self.assertNotIn("kRecorderLayoutStyleProperties", helper)
        self.assertNotIn("CSSPropertyID::k", helper)
        self.assertIn(
            "kRecorderLayoutStyleProperties[] = {",
            INTEGRATE.LEGACY_FIXED_LIST_BLINK_LAYOUT_CHECKPOINT_HELPER,
        )

    def test_the_layout_checkpoint_is_followed_by_an_interaction_checkpoint(
        self,
    ):
        patched = self.patch_twice(
            cookie_source(
                self.LOCAL_FRAME_VIEW_INCLUDE + "\n",
                INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
                INTEGRATE.BLINK_LAYOUT_CHECKPOINT_ANCHOR,
                BLINK_NAMESPACE_END,
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
                BLINK_NAMESPACE_END,
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

    def test_the_helper_records_every_computable_property(self):
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        self.assertIn(
            "CSSComputedStyleDeclaration::ComputableProperties(\n"
            "      recorder_document.GetExecutionContext());",
            helper,
        )
        self.assertIn(
            "RecorderLayoutStylePropertyNames(*recorder_document)", helper
        )
        self.assertIn(
            "RecorderReadCustomProperties(*recorder_document, *recorder_style,\n"
            "                                   recorder_record.custom_properties);",
            helper,
        )
        # ComputedStyleCSSValueMapping::Get is private in the reference
        # checkout; GetVariables is its public reader.
        self.assertIn(
            "ComputedStyleCSSValueMapping::GetVariables(\n"
            "          recorder_style, recorder_document.GetPropertyRegistry(),\n"
            "          CSSValuePhase::kResolvedValue);",
            helper,
        )
        self.assertNotIn("ComputedStyleCSSValueMapping::Get(", helper)
        self.assertIn("CodeUnitCompareLessThan(recorder_a, recorder_b)", helper)
        self.assertIn("CSSValuePhase::kResolvedValue", helper)
        definition = INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION
        self.assertIn(INTEGRATE.BLINK_LAYOUT_CHANGES_COMPUTABLE_STYLE, definition)
        self.assertNotIn("kRecorderLayoutStyleProperties", definition)
        self.assertIn(
            INTEGRATE.LEGACY_FIXED_LIST_BLINK_LAYOUT_CHANGES_DEFINITION,
            INTEGRATE.BLINK_LAYOUT_CHANGES_LEGACY_DEFINITIONS,
        )

    def test_both_readings_record_box_fragments(self):
        # Protocol 0.38: the checkpoint and the change set read each node's
        # box fragments with the one reader, after its other fields.
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        self.assertEqual(1, helper.count("void RecorderReadBoxFragments("))
        self.assertEqual(1, helper.count("void RecorderReadBoxFragment("))
        call = "RecorderReadBoxFragments(recorder_layout_object, recorder_record);"
        self.assertEqual(1, helper.count(call))
        self.assertLess(
            helper.index("void RecorderReadBoxFragments("),
            helper.index("RecorderRecordLayoutCheckpoint("),
        )
        self.assertLess(
            helper.index(call),
            helper.index("a11y_recorder::RecordBlinkLayoutCheckpointNode("),
        )
        for reading in (
            "recorder_box->PhysicalFragments()",
            "recorder_fragment.PostLayoutChildren()",
            "recorder_fragment.GetBreakToken()",
            "recorder_fragment.HasScrollableOverflow()",
            "recorder_replaced->ComputeNaturalSizingInfo()",
            "recorder_box->StyleRef().EffectiveZoom()",
        ):
            with self.subTest(reading=reading):
                self.assertIn(reading, helper)
        # A break before has no sequence number to read.
        self.assertIn(
            "    if (!recorder_token->IsBreakBefore()) {\n"
            "      recorder_out.sequence_number = recorder_token->SequenceNumber();",
            helper,
        )
        definition = INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION
        self.assertEqual(1, definition.count(call))
        self.assertNotIn(
            call, INTEGRATE.LEGACY_UNFRAGMENTED_BLINK_LAYOUT_CHANGES_DEFINITION
        )
        self.assertEqual(
            INTEGRATE.LEGACY_UNFRAGMENTED_BLINK_LAYOUT_CHANGES_DEFINITION,
            INTEGRATE.BLINK_LAYOUT_CHANGES_LEGACY_DEFINITIONS[2],
        )
        for include in (
            "layout/block_break_token.h",
            "layout/layout_replaced.h",
            "layout/natural_sizing_info.h",
            "physical_fragment_link.h",
        ):
            with self.subTest(include=include):
                self.assertTrue(
                    any(include in line
                        for line in INTEGRATE.BLINK_LAYOUT_CHANGES_INCLUDES)
                )

    def test_box_fragments_hold_their_items_text_and_glyphs(self):
        # Protocol 0.39: a fragment that holds lines records its items, each
        # text item its glyph runs, and the block its text content, read from
        # the first such fragment.
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        for definition in (
            "void RecorderReadGlyph(",
            "void RecorderReadFragmentItems(",
        ):
            with self.subTest(definition=definition):
                self.assertEqual(1, helper.count(definition))
                self.assertLess(
                    helper.index(definition),
                    helper.index("void RecorderReadBoxFragment("),
                )
        for reading in (
            "recorder_fragment.Items()",
            "recorder_items.Items()",
            "recorder_item.RectInContainerFragment()",
            "recorder_item.DescendantsCount()",
            "recorder_item.StartOffset()",
            "recorder_item.GeneratedText()",
            "recorder_item.ResolvedDirection()",
            "recorder_item.IsHiddenForPaint()",
            "recorder_item.UsesFirstLineStyle()",
            "recorder_shape->ForEachGlyph(0, RecorderReadGlyph, "
            "&recorder_reading)",
            "recorder_platform.FontFamilyName()",
            "recorder_typeface->getPostScriptName(&recorder_name)",
            "recorder_items.NormalText()",
            "recorder_items.FirstLineText()",
        ):
            with self.subTest(reading=reading):
                self.assertIn(reading, helper)
        # The glyph reading holds a garbage-collected font by a raw pointer,
        # which Blink's garbage-collection plugin allows only on the stack.
        self.assertIn(
            "struct RecorderGlyphReading {\n  STACK_ALLOCATED();\n\n public:\n",
            helper,
        )
        # Only a text item, not generated text, has a range of the text.
        self.assertIn(
            "    if (recorder_item.Type() == FragmentItem::kText) {\n"
            "      recorder_record.range_present = true;",
            helper,
        )
        # The node's text is taken once, from its first fragment with items,
        # and its own fragments keep none.
        self.assertIn(
            "if (recorder_read.text_present && !recorder_fragments.text_present) {",
            helper,
        )
        self.assertIn("    recorder_read.text_present = false;\n", helper)
        for include in (
            "shaping/shape_result_view.h",
            "fonts/simple_font_data.h",
            "fonts/font_platform_data.h",
            "fonts/canvas_rotation_in_vertical.h",
            "fonts/glyph.h",
            "core/SkTypeface.h",
            "core/SkString.h",
        ):
            with self.subTest(include=include):
                self.assertTrue(
                    any(include in line
                        for line in INTEGRATE.BLINK_LAYOUT_CHANGES_INCLUDES)
                )

    def test_a_checkout_at_protocol_0_37_takes_the_box_fragment_reading(self):
        # The definition a 0.37 checkout holds is the first one recognised,
        # and only the node reader's end differs from the current one.
        legacy = INTEGRATE.LEGACY_UNFRAGMENTED_BLINK_LAYOUT_CHANGES_DEFINITION
        current = INTEGRATE.LEGACY_TEXT_SKIPPING_BLINK_LAYOUT_CHANGES_DEFINITION
        self.assertNotIn(legacy, current)
        self.assertEqual(
            current,
            legacy.replace(
                INTEGRATE.BLINK_LAYOUT_CHANGES_UNFRAGMENTED_END,
                INTEGRATE.BLINK_LAYOUT_CHANGES_UNFRAGMENTED_END.replace(
                    "  return recorder_changed;",
                    "  RecorderReadBoxFragments(recorder_layout_object, "
                    "recorder_record);\n  return recorder_changed;",
                ),
            ),
        )

    def test_a_change_set_records_a_text_node_without_a_layout_object(self):
        # Protocol 0.41: a checkout at 0.40 holds the definition that left
        # such a node out, and is upgraded; only the skip differs.
        legacy = INTEGRATE.LEGACY_TEXT_SKIPPING_BLINK_LAYOUT_CHANGES_DEFINITION
        current = INTEGRATE.LEGACY_UNIDENTIFIED_SCROLL_BLINK_LAYOUT_CHANGES_DEFINITION
        self.assertEqual(
            legacy, INTEGRATE.BLINK_LAYOUT_CHANGES_LEGACY_DEFINITIONS[1]
        )
        self.assertIn(INTEGRATE.BLINK_LAYOUT_CHANGES_TEXT_SKIP, legacy)
        self.assertNotIn(INTEGRATE.BLINK_LAYOUT_CHANGES_TEXT_SKIP, current)
        self.assertEqual(
            current,
            legacy.replace(
                INTEGRATE.BLINK_LAYOUT_CHANGES_TEXT_SKIP,
                INTEGRATE.BLINK_LAYOUT_CHANGES_TEXT_RECORDED,
            ),
        )
        self.assertNotIn("IsTextNode() && !recorder_node.GetLayoutObject()", current)

    def test_a_scroll_offset_records_its_scroll_element_id(self):
        # Protocol 0.49: a checkout at 0.48 holds the definition without the
        # scroller's compositor element ID, and is upgraded; only that differs.
        legacy = INTEGRATE.LEGACY_UNIDENTIFIED_SCROLL_BLINK_LAYOUT_CHANGES_DEFINITION
        current = INTEGRATE.BLINK_LAYOUT_CHANGES_DEFINITION
        self.assertEqual(
            legacy, INTEGRATE.BLINK_LAYOUT_CHANGES_LEGACY_DEFINITIONS[0]
        )
        self.assertEqual(
            current,
            legacy.replace(
                INTEGRATE.BLINK_LAYOUT_CHANGES_SCROLL_PUSH,
                INTEGRATE.BLINK_LAYOUT_CHANGES_SCROLL_ELEMENT_ID,
            ),
        )
        self.assertEqual(
            1,
            current.count(
                "recorder_scroll.scroll_element_id =\n"
                "        recorder_area->GetScrollElementId().GetInternalValue();\n"
                "    recorder_scroll_offsets.push_back(std::move(recorder_scroll));"
            ),
        )
        bridge = (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.cc").read_text(
            encoding="utf-8"
        )
        self.assertIn('payload.Set("scrollElementId",', bridge)
        self.assertIn("scroll.scroll_element_id == 0\n                    ? base::Value()", bridge)
        layout = (MODULE_PATH.parent / "recorder_bridge" / "layout_changes.h").read_text(
            encoding="utf-8"
        )
        self.assertIn("  uint64_t scroll_element_id = 0;\n};", layout)

    def test_the_fixed_list_before_protocol_0_37_stays_documented(self):
        # The list recorded before protocol 0.37 is kept to recognise the
        # helpers written with it.
        legacy = INTEGRATE.LEGACY_FIXED_LIST_BLINK_LAYOUT_CHECKPOINT_HELPER
        identifiers = re.findall(r"CSSPropertyID::(k[A-Za-z]+),", legacy)
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
        for name in INTEGRATE.LAYOUT_STYLE_PROPERTIES:
            with self.subTest(property=name):
                self.assertIn(f"`{name}`", document)


class PresentationIntegrationTests(unittest.TestCase):
    """Proves the presentation swap promise is written once and only observes."""

    WIDGET_INCLUDE = (
        '#include "third_party/blink/renderer/core/frame/'
        'web_frame_widget_impl.h"'
    )

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def header_source(self):
        return cookie_source(
            '#include "base/time/time.h"\n',
            INTEGRATE.BLINK_PRESENTATION_FREE_DECLARATION_ANCHOR,
            INTEGRATE.BLINK_PRESENTATION_WIDGET_HEADER_DECLARATION_ANCHOR,
            INTEGRATE.BLINK_PRESENTATION_WIDGET_HEADER_FRIEND_ANCHOR,
        )

    def test_patches_the_widget_header_idempotently(self):
        patched = self.patch_twice(
            "web_frame_widget_impl.h",
            self.header_source(),
            INTEGRATE.patch_blink_web_frame_widget_header,
        )
        self.assertEqual(
            1,
            patched.count(
                INTEGRATE.BLINK_PRESENTATION_WIDGET_HEADER_DECLARATION
            ),
        )
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_PRESENTATION_WIDGET_HEADER_FRIEND)
        )
        # The declaration follows NotifyPresentationTime, in the public section,
        # and the friend follows ReportTimeSwapPromise's.
        self.assertLess(
            patched.index("void NotifyPresentationTime("),
            patched.index("void RecorderRequestPresentationEvidence("),
        )
        self.assertLess(
            patched.index("friend class ReportTimeSwapPromise;"),
            patched.index("friend class RecorderPresentationSwapPromise;"),
        )
        # Protocol 0.43: the widget-level request is declared once, at
        # namespace scope, before the widget class.
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_PRESENTATION_FREE_DECLARATION)
        )
        self.assertLess(
            patched.index("void RecorderRequestWidgetPresentation("),
            patched.index("class CORE_EXPORT WebFrameWidgetImpl"),
        )

    def test_patches_the_widget_idempotently(self):
        patched = self.patch_twice(
            "web_frame_widget_impl.cc",
            cookie_source(
                self.WIDGET_INCLUDE + "\n",
                INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
                INTEGRATE.BLINK_WIDGET_INPUT_ANCHOR,
            ),
            INTEGRATE.patch_blink_web_frame_widget,
        )
        self.assertEqual(
            1, patched.count(INTEGRATE.blink_registered_presentation_widget_block())
        )
        self.assertLess(
            patched.index(INTEGRATE.blink_registered_presentation_widget_block()),
            patched.index(INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR),
        )
        for include_line in INTEGRATE.BLINK_PRESENTATION_WIDGET_INCLUDES:
            with self.subTest(include=include_line):
                self.assertEqual(1, patched.count(include_line + "\n"))

    def test_a_042_swap_promise_is_upgraded_whole(self):
        source = cookie_source(
            self.WIDGET_INCLUDE + "\n",
            INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
            INTEGRATE.BLINK_WIDGET_INPUT_ANCHOR,
        ).replace(
            INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
            INTEGRATE.STAGE_042_BLINK_PRESENTATION_WIDGET_BLOCK
            + INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
        )
        patched = self.patch_twice(
            "web_frame_widget_impl.cc",
            source,
            INTEGRATE.patch_blink_web_frame_widget,
        )
        self.assertEqual(
            1, patched.count(INTEGRATE.blink_registered_presentation_widget_block())
        )
        self.assertNotIn(
            INTEGRATE.STAGE_042_BLINK_PRESENTATION_WIDGET_BLOCK, patched
        )
        self.assertNotIn("MakeCrossThreadWeakHandle(widget)", patched)

    def test_an_unrecognised_swap_promise_is_refused(self):
        source = cookie_source(
            self.WIDGET_INCLUDE + "\n",
            "class RecorderPresentationSwapPromise : public cc::SwapPromise {};\n"
            + INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "web_frame_widget_impl.cc"
            path.write_text(source, encoding="utf-8")
            with self.assertRaises(RuntimeError):
                INTEGRATE.patch_blink_web_frame_widget(path)

    def test_a_page_popup_shares_the_widget_request(self):
        block = INTEGRATE.BLINK_PRESENTATION_WIDGET_BLOCK
        # The promise reaches its widget through a weak WidgetBase pointer,
        # so a page popup, which is not garbage collected, can use it.
        self.assertIn("base::WeakPtr<WidgetBase> widget_base", block)
        self.assertIn("widget_base->GetWeakPtr()", block)
        self.assertIn("void RecorderRequestWidgetPresentation(", block)
        self.assertIn(
            "RecorderRequestPagePopupPresentation(",
            INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER,
        )
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        self.assertLess(
            helper.index("RecorderRequestPagePopupPresentation("),
            helper.index("FrameWidgetImpl() : nullptr;"),
        )

    def test_the_widget_patches_fail_when_an_anchor_is_absent(self):
        cases = (
            (
                "web_frame_widget_impl.h",
                self.header_source().replace(
                    "friend class ReportTimeSwapPromise;", ""
                ),
                INTEGRATE.patch_blink_web_frame_widget_header,
            ),
            (
                "web_frame_widget_impl.cc",
                self.WIDGET_INCLUDE + "\n",
                INTEGRATE.patch_blink_web_frame_widget,
            ),
        )
        for name, source, patch in cases:
            with self.subTest(file=name):
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / name
                    path.write_text(source, encoding="utf-8")
                    with self.assertRaises(RuntimeError):
                        patch(path)

    def test_the_swap_promise_breaks_only_where_chromium_does(self):
        """The promise keeps the same break rule as ReportTimeSwapPromise."""
        block = INTEGRATE.BLINK_PRESENTATION_WIDGET_BLOCK
        self.assertIn(
            "reason != DidNotSwapReason::SWAP_FAILS &&\n"
            "        reason != DidNotSwapReason::COMMIT_NO_UPDATE;",
            block,
        )
        self.assertIn("fetch_add(1)", block)
        # The feedback callback is registered on the main thread only.
        self.assertIn("PostCrossThreadTask(", block)
        self.assertIn("MainThreadTaskRunner()", block)

    def test_the_swap_promise_never_requests_a_frame(self):
        block = INTEGRATE.BLINK_PRESENTATION_WIDGET_BLOCK
        for forbidden in (
            "SetNeedsCommit",
            "SetNeedsAnimate",
            "SetNeedsUpdateLayers",
            "RequestPresentationTimeForNextFrame",
            "ScheduleAnimation",
        ):
            with self.subTest(call=forbidden):
                self.assertNotIn(forbidden, block)

    def test_every_layout_checkpoint_requests_its_presentation(self):
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        complete = helper.index("a11y_recorder::CompleteBlinkLayoutCheckpoint(")
        request = helper.index("  RecorderRequestLayoutPresentation(recorder_frame,")
        interaction = helper.index(
            "RecorderRecordInteractionCheckpoint(*recorder_document,"
        )
        self.assertLess(complete, request)
        self.assertLess(request, interaction)
        self.assertIn('"no-widget", -1, false', helper)

    def test_the_presentation_templates_match_the_bridge_header(self):
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        for label in (
            "BLINK_PRESENTATION_WIDGET_BLOCK",
            "BLINK_LAYOUT_CHECKPOINT_HELPER",
        ):
            with self.subTest(template=label):
                self.assertEqual(
                    [],
                    INTEGRATE.describe_signature_mismatches(
                        label, getattr(INTEGRATE, label), signatures
                    ),
                )



class RecreationInputIntegrationTests(unittest.TestCase):
    """Proves the recreation's input hooks are written once, where they act."""

    WIDGET_INCLUDE = PresentationIntegrationTests.WIDGET_INCLUDE

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def proxy_source(self):
        return cookie_source(
            INTEGRATE.BLINK_INPUT_HANDLER_PROXY_OWN_INCLUDE + "\n",
            "EventDisposition InputHandlerProxy::RouteToTypeSpecificHandler(\n"
            + INTEGRATE.BLINK_INPUT_HANDLER_PROXY_ANCHOR,
        )

    def test_the_compositor_thread_drops_all_but_mouse_events_once(self):
        patched = self.patch_twice(
            "input_handler_proxy.cc",
            self.proxy_source(),
            INTEGRATE.patch_blink_input_handler_proxy,
        )
        self.assertEqual(
            1, patched.count("a11y_recorder::RecreationRefusesCompositorInput()")
        )
        self.assertNotIn("a11y_recorder::IsRecreationMode()", patched)
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n"))
        # The check comes before any scroll handling of the event.
        self.assertLess(
            patched.index("a11y_recorder::RecreationRefusesCompositorInput()"),
            patched.index("if (event.IsGestureScroll() &&"),
        )
        # Mouse events reach cc's scrollbar controller, and the scroll
        # gestures cc makes for a scrollbar are handled; the rest is dropped.
        self.assertIn("!WebInputEvent::IsMouseEventType(event.GetType()) &&", patched)
        self.assertIn(
            "        (static_cast<const WebGestureEvent&>(event).SourceDevice() ==\n"
            "             WebGestureDevice::kScrollbar ||\n"
            "         static_cast<const WebGestureEvent&>(event).SourceDevice() ==\n"
            "             WebGestureDevice::kTouchpad))) {\n"
            "    return DROP_EVENT;",
            patched,
        )
        # The wheel event itself is still dropped, before the page sees it.
        self.assertNotIn("kMouseWheel", INTEGRATE.BLINK_INPUT_HANDLER_PROXY_HOOK)
        self.assertNotIn("DID_NOT_HANDLE", INTEGRATE.BLINK_INPUT_HANDLER_PROXY_HOOK)

    def test_the_main_thread_shows_only_the_context_menu_once(self):
        patched = self.patch_twice(
            "web_frame_widget_impl.cc",
            cookie_source(
                self.WIDGET_INCLUDE + "\n",
                INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
                "devtools->HandleInputEvent(input_event);\n"
                + INTEGRATE.BLINK_WIDGET_INPUT_ANCHOR
                + "  return WidgetEventHandler::HandleInputEvent(coalesced_event,\n",
            ),
            INTEGRATE.patch_blink_web_frame_widget,
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_WIDGET_INPUT_HOOK))
        hook = patched.index("a11y_recorder::IsRecreationMode()")
        # After the DevTools overlay has had the event, before the page does.
        self.assertLess(patched.index("devtools->HandleInputEvent(input_event);"), hook)
        self.assertLess(hook, patched.index("WidgetEventHandler::HandleInputEvent("))
        block = INTEGRATE.BLINK_WIDGET_INPUT_HOOK
        self.assertIn("WebMouseEvent::Button::kRight", block)
        self.assertIn("GetShowContextMenuOnMouseUp()", block)
        self.assertIn("MouseContextMenu(recorder_mouse);", block)
        self.assertIn("return WebInputEventResult::kHandledSuppressed;", block)
        # A scroll gesture cc made for a scrollbar is not refused.
        self.assertIn(
            "!(input_event.IsGestureScroll() &&\n"
            "        (static_cast<const WebGestureEvent&>(input_event).SourceDevice() ==\n"
            "             WebGestureDevice::kScrollbar ||\n"
            "         static_cast<const WebGestureEvent&>(input_event).SourceDevice() ==\n"
            "             WebGestureDevice::kTouchpad)) &&",
            block,
        )
        # A browser page's widget takes input: the refusal tests the local
        # root document's scheme.
        self.assertIn(
            "a11y_recorder::IsRecreationBrowserPageScheme(\n"
            "            String(LocalRootImpl()->GetFrame()->GetDocument()->Url().Protocol())",
            block,
        )

    def test_hooks_of_the_first_input_refusal_are_upgraded(self):
        proxy = self.proxy_source().replace(
            INTEGRATE.BLINK_INPUT_HANDLER_PROXY_ANCHOR,
            INTEGRATE.STAGE_045_BLINK_INPUT_HANDLER_PROXY_HOOK,
        )
        patched = self.patch_twice(
            "input_handler_proxy.cc", proxy, INTEGRATE.patch_blink_input_handler_proxy
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_INPUT_HANDLER_PROXY_HOOK))
        self.assertNotIn(INTEGRATE.STAGE_045_BLINK_INPUT_HANDLER_PROXY_HOOK, patched)
        widget = self.patch_twice(
            "web_frame_widget_impl.cc",
            cookie_source(
                self.WIDGET_INCLUDE + "\n",
                INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
                "devtools->HandleInputEvent(input_event);\n"
                + INTEGRATE.STAGE_045_BLINK_WIDGET_INPUT_HOOK
                + "  return WidgetEventHandler::HandleInputEvent(coalesced_event,\n",
            ),
            INTEGRATE.patch_blink_web_frame_widget,
        )
        self.assertEqual(1, widget.count(INTEGRATE.BLINK_WIDGET_INPUT_HOOK))
        self.assertNotIn(INTEGRATE.STAGE_045_BLINK_WIDGET_INPUT_HOOK, widget)

    def test_hooks_that_refused_the_scrollbars_are_upgraded(self):
        proxy = self.proxy_source().replace(
            INTEGRATE.BLINK_INPUT_HANDLER_PROXY_ANCHOR,
            INTEGRATE.STAGE_0172_BLINK_INPUT_HANDLER_PROXY_HOOK,
        )
        patched = self.patch_twice(
            "input_handler_proxy.cc", proxy, INTEGRATE.patch_blink_input_handler_proxy
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_INPUT_HANDLER_PROXY_HOOK))
        self.assertNotIn(INTEGRATE.STAGE_0172_BLINK_INPUT_HANDLER_PROXY_HOOK, patched)
        widget = self.patch_twice(
            "web_frame_widget_impl.cc",
            cookie_source(
                self.WIDGET_INCLUDE + "\n",
                INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
                "devtools->HandleInputEvent(input_event);\n"
                + INTEGRATE.STAGE_0172_BLINK_WIDGET_INPUT_HOOK
                + "  return WidgetEventHandler::HandleInputEvent(coalesced_event,\n",
            ),
            INTEGRATE.patch_blink_web_frame_widget,
        )
        self.assertEqual(1, widget.count(INTEGRATE.BLINK_WIDGET_INPUT_HOOK))
        self.assertNotIn(INTEGRATE.STAGE_0172_BLINK_WIDGET_INPUT_HOOK, widget)

    def test_hooks_that_refused_the_wheel_are_upgraded(self):
        proxy = self.proxy_source().replace(
            INTEGRATE.BLINK_INPUT_HANDLER_PROXY_ANCHOR,
            INTEGRATE.STAGE_6A83_BLINK_INPUT_HANDLER_PROXY_HOOK,
        )
        patched = self.patch_twice(
            "input_handler_proxy.cc", proxy, INTEGRATE.patch_blink_input_handler_proxy
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_INPUT_HANDLER_PROXY_HOOK))
        self.assertNotIn(INTEGRATE.STAGE_6A83_BLINK_INPUT_HANDLER_PROXY_HOOK, patched)
        widget = self.patch_twice(
            "web_frame_widget_impl.cc",
            cookie_source(
                self.WIDGET_INCLUDE + "\n",
                INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
                "devtools->HandleInputEvent(input_event);\n"
                + INTEGRATE.STAGE_6A83_BLINK_WIDGET_INPUT_HOOK
                + "  return WidgetEventHandler::HandleInputEvent(coalesced_event,\n",
            ),
            INTEGRATE.patch_blink_web_frame_widget,
        )
        self.assertEqual(1, widget.count(INTEGRATE.BLINK_WIDGET_INPUT_HOOK))
        self.assertNotIn(INTEGRATE.STAGE_6A83_BLINK_WIDGET_INPUT_HOOK, widget)

    def test_a_browser_pages_parser_marks_its_process_once(self):
        hook = INTEGRATE.BLINK_DOCUMENT_STARTED_PARSING_HOOK
        self.assertIn("a11y_recorder::MarkRecreationBrowserPageProcess();", hook)
        self.assertIn("a11y_recorder::IsRecreationBrowserPageScheme(", hook)
        source = (
            "DocumentParser* Document::ImplicitOpen(\n"
            + INTEGRATE.BLINK_DOCUMENT_STARTED_PARSING_ANCHOR
            + INTEGRATE.STAGE_045_BLINK_DOCUMENT_STARTED_PARSING_HOOK
        )
        patched = self.patch_twice(
            "document.cc", source, INTEGRATE.patch_blink_document_started_parsing
        )
        self.assertEqual(1, patched.count(hook))
        self.assertEqual(1, patched.count("MarkRecreationBrowserPageProcess"))

    def test_the_browser_page_schemes_pass_their_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        build = (bridge / "BUILD.gn").read_text(encoding="utf-8")
        self.assertIn('"recreation_input.h",', build)
        source = (bridge / "browser_bridge.cc").read_text(encoding="utf-8")
        self.assertIn("return IsBrowserPageScheme(scheme);", source)
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "recreation_input_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "recreation_input_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_the_platform_component_gains_the_bridge_once(self):
        source = (
            'component("other") {\n  deps = [\n    "//base",\n  ]\n}\n'
            'component("platform") {\n'
            "  public_deps = [\n    \":platform_export\",\n  ]\n"
            "  deps = [\n    \"//base\",\n  ]\n}\n"
        )
        patched = self.patch_twice(
            "BUILD.gn", source, INTEGRATE.patch_blink_platform_build
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_PLATFORM_DEP))
        self.assertLess(
            patched.index('component("platform")'),
            patched.index(INTEGRATE.BLINK_PLATFORM_DEP),
        )

    def test_the_input_patches_fail_when_an_anchor_is_absent(self):
        cases = (
            (
                "input_handler_proxy.cc",
                INTEGRATE.BLINK_INPUT_HANDLER_PROXY_OWN_INCLUDE + "\n",
                INTEGRATE.patch_blink_input_handler_proxy,
            ),
            (
                "web_frame_widget_impl.cc",
                cookie_source(
                    self.WIDGET_INCLUDE + "\n",
                    INTEGRATE.BLINK_PRESENTATION_WIDGET_ANCHOR,
                ),
                INTEGRATE.patch_blink_web_frame_widget,
            ),
            (
                "BUILD.gn",
                'component("other") {\n  deps = [\n  ]\n}\n',
                INTEGRATE.patch_blink_platform_build,
            ),
        )
        for name, source, patch in cases:
            with self.subTest(file=name):
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / name
                    path.write_text(source, encoding="utf-8")
                    with self.assertRaises(RuntimeError):
                        patch(path)


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

    def test_replaces_the_chunk_join_of_an_earlier_revision(self):
        # A source patched before the message text limits were removed joins
        # only the first 64 KiB of a text message.
        self.assertIn(
            "kScanLimit = 65536", INTEGRATE.EARLIER_REALTIME_CHUNKS_TEXT
        )
        for name, include, helper_anchor, hooks, patch, _ in self.cases():
            with self.subTest(source=name):
                patched = self.patch_twice(
                    name, self.module_source(include, helper_anchor, hooks), patch
                )
                self.assertNotIn("kScanLimit", patched)
                earlier = patched.replace(
                    INTEGRATE.BLINK_REALTIME_CHUNKS_TEXT,
                    INTEGRATE.EARLIER_REALTIME_CHUNKS_TEXT,
                )
                self.assertNotEqual(patched, earlier)
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / name
                    path.write_text(earlier, encoding="utf-8")
                    patch(path)
                    self.assertEqual(patched, path.read_text(encoding="utf-8"))

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



class CompositorRecordIntegrationTests(unittest.TestCase):
    """Protocol 0.48 (slice 4b sub-step 1): the compositor's drawn values."""

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def host_impl_source(self):
        return (
            INTEGRATE.CC_LAYER_TREE_HOST_IMPL_OWN_INCLUDE
            + "\n\n#include <map>\n\nnamespace cc {\n\n"
            + INTEGRATE.CC_PRESENTED_ANCHOR
            + "}\n\n"
            + INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS_ANCHOR
            + "  auto compositor_frame = GenerateCompositorFrame(frame);\n"
            + INTEGRATE.CC_DRAW_LAYERS_ANCHOR
            + "  layer_tree_frame_sink_->SubmitCompositorFrame(\n}\n\n"
            + "".join(anchor + "}\n\n" for anchor, _ in INTEGRATE.CC_MUTATED_HOOKS)
            + "}  // namespace cc\n"
        )

    def test_the_compositor_records_each_submitted_frame_once(self):
        patched = self.patch_twice(
            "layer_tree_host_impl.cc",
            self.host_impl_source(),
            INTEGRATE.patch_cc_layer_tree_host_impl,
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n"))
        self.assertEqual(1, patched.count("#include <set>\n"))
        self.assertEqual(1, patched.count(INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS))
        self.assertEqual(1, patched.count(INTEGRATE.CC_DRAW_LAYERS_HOOK))
        self.assertEqual(1, patched.count(INTEGRATE.CC_PRESENTED_HOOK))
        # The helpers come before DrawLayers, and the frame is recorded once
        # its token is known, before it is submitted.
        self.assertLess(
            patched.index("void RecorderRecordCompositorFrame("),
            patched.index("LayerTreeHostImpl::DrawLayers(FrameData* frame) {"),
        )
        self.assertLess(
            patched.index("frame->frame_token = frame_token;"),
            patched.index("RecorderRecordCompositorFrame(id_, active_tree()"),
        )
        self.assertLess(
            patched.index("RecorderRecordCompositorFrame(id_, active_tree()"),
            patched.index("SubmitCompositorFrame("),
        )
        for anchor, property_name in INTEGRATE.CC_MUTATED_HOOKS:
            with self.subTest(property_name=property_name):
                self.assertEqual(
                    1, patched.count(INTEGRATE.cc_mutated_hook(anchor, property_name))
                )
        helpers = INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS
        # The browser's own compositor is not recorded.
        self.assertIn("if (!settings_.is_layer_tree_for_ui) {", INTEGRATE.CC_DRAW_LAYERS_HOOK)
        self.assertIn("if (!settings_.is_layer_tree_for_ui) {", INTEGRATE.CC_PRESENTED_HOOK)
        self.assertIn("node->local.rc(row, column)", helpers)
        self.assertIn("scroll_tree.current_scroll_offset(node.element_id)", helpers)
        self.assertIn("a11y_recorder::RecordCompositorFrame(", helpers)
        self.assertIn(
            "a11y_recorder::RecordCompositorFramePresented(", INTEGRATE.CC_PRESENTED_HOOK
        )

    def test_a_tree_patched_by_part_1a_is_upgraded_in_place(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "layer_tree_host_impl.cc"
            path.write_text(self.host_impl_source(), encoding="utf-8")
            helpers = INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS
            hook = INTEGRATE.CC_DRAW_LAYERS_HOOK
            try:
                INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS = (
                    INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS_1A
                )
                INTEGRATE.CC_DRAW_LAYERS_HOOK = INTEGRATE.CC_DRAW_LAYERS_HOOK_1A
                INTEGRATE.patch_cc_layer_tree_host_impl(path)
            finally:
                INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS = helpers
                INTEGRATE.CC_DRAW_LAYERS_HOOK = hook
            INTEGRATE.patch_cc_layer_tree_host_impl(path)
            upgraded = path.read_text(encoding="utf-8")
        fresh = self.patch_twice(
            "layer_tree_host_impl.cc",
            self.host_impl_source(),
            INTEGRATE.patch_cc_layer_tree_host_impl,
        )
        self.assertEqual(fresh, upgraded)
        self.assertNotIn(INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS_1A, upgraded)
        self.assertNotIn(INTEGRATE.CC_DRAW_LAYERS_HOOK_1A, upgraded)
        self.assertEqual(1, upgraded.count("void RecorderRecordCompositorFrame("))

    def test_a_tree_patched_at_protocol_0_49_records_whether_a_scroll_is_composited(self):
        # Protocol 0.50: the helpers as 0.48 and 0.49 inserted them are
        # replaced in place; only the scroll offset's numbers differ.
        legacy = INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS_0_49
        current = INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS
        self.assertEqual(
            current,
            legacy.replace(
                INTEGRATE.CC_FRAME_SCROLL_NUMBERS,
                INTEGRATE.CC_FRAME_SCROLL_COMPOSITED_NUMBERS,
            ),
        )
        self.assertIn("node.is_composited ? 1.0 : 0.0", current)
        for reason in (
            "kHasBackgroundAttachmentFixedObjects",
            "kNotOpaqueForTextAndLCDText",
            "kPreferNonCompositedScrolling",
            "kBackgroundNeedsRepaintOnScroll",
        ):
            self.assertEqual(1, current.count("MainThreadRepaintReason::" + reason))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "layer_tree_host_impl.cc"
            path.write_text(self.host_impl_source(), encoding="utf-8")
            try:
                INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS = legacy
                INTEGRATE.patch_cc_layer_tree_host_impl(path)
            finally:
                INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS = current
            INTEGRATE.patch_cc_layer_tree_host_impl(path)
            upgraded = path.read_text(encoding="utf-8")
        fresh = self.patch_twice(
            "layer_tree_host_impl.cc",
            self.host_impl_source(),
            INTEGRATE.patch_cc_layer_tree_host_impl,
        )
        self.assertEqual(fresh, upgraded)
        self.assertEqual(1, upgraded.count(INTEGRATE.CC_FRAME_SCROLL_COMPOSITED_NUMBERS))
        bridge = (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.cc").read_text(
            encoding="utf-8"
        )
        self.assertIn('offset.Set("isComposited", value.numbers[2] != 0);', bridge)
        self.assertIn('offset.Set("mainThreadRepaintReasons", std::move(reasons));', bridge)

    def test_each_frame_names_the_progress_its_paint_worklet_records_were_painted_with(self):
        helpers = INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS
        # The paint worklet helpers come before the frame recorder that
        # calls them, which reads the active tree and keeps the pending
        # tree's results.
        self.assertTrue(helpers.startswith(INTEGRATE.CC_PAINT_WORKLET_HELPERS))
        self.assertIn(INTEGRATE.CC_FRAME_SIGNATURE, helpers)
        self.assertNotIn(INTEGRATE.CC_FRAME_SIGNATURE_1A, helpers)
        self.assertLess(
            helpers.index("RecorderReadPaintWorkletProgress(host_id, tree, pending_tree, &values);"),
            helpers.index(INTEGRATE.CC_FRAME_SCROLL_COMMENT),
        )
        self.assertIn("pending_tree(),", INTEGRATE.CC_DRAW_LAYERS_HOOK)
        self.assertIn('"background-color-progress"', helpers)
        self.assertIn('"clip-path-progress"', helpers)
        self.assertIn("value.present = false;", helpers)
        source = (
            INTEGRATE.CC_CLIENT_OWN_INCLUDE
            + "\n\nnamespace cc {\n\n"
            + INTEGRATE.CC_CLIENT_DECLARATION_ANCHOR
            + "#if DCHECK_IS_ON()\n#endif\n\n"
            + INTEGRATE.CC_CLIENT_RESULTS_ANCHOR
            + "    }\n  }\n}\n\n}  // namespace cc\n"
        )
        patched = self.patch_twice(
            "client_layer_tree_host_impl.cc",
            source,
            INTEGRATE.patch_cc_client_layer_tree_host_impl,
        )
        self.assertEqual(1, patched.count(INTEGRATE.CC_CLIENT_DECLARATION))
        self.assertEqual(1, patched.count(INTEGRATE.CC_CLIENT_RESULTS_HOOK))
        self.assertLess(
            patched.index("RecorderNotePaintWorkletResults(id(), results);"),
            patched.index("FindPendingTreeLayerById"),
        )

    def test_what_the_native_paint_worklets_painted_is_recorded_once(self):
        with tempfile.TemporaryDirectory() as directory:
            csspaint = Path(directory) / "csspaint"
            nativepaint = csspaint / "nativepaint"
            nativepaint.mkdir(parents=True)
            (nativepaint / "background_color_paint_definition.cc").write_text(
                INTEGRATE.BLINK_BACKGROUND_COLOR_PAINT_OWN_INCLUDE
                + "\n\nPaintRecord BackgroundColorPaintDefinition::Paint() {\n"
                + INTEGRATE.BLINK_BACKGROUND_COLOR_PAINTED_ANCHOR
                + "}\n",
                encoding="utf-8",
            )
            (nativepaint / "clip_path_paint_definition.cc").write_text(
                INTEGRATE.BLINK_CLIP_PATH_PAINT_OWN_INCLUDE
                + "\n\nclass ClipPathPaintWorkletInput {\n"
                + INTEGRATE.BLINK_CLIP_PATH_TRANSLATION_ANCHOR
                + "};\n\nPaintRecord ClipPathPaintDefinition::Paint() {\n"
                + INTEGRATE.BLINK_CLIP_PATH_PAINTED_ANCHOR
                + "}\n",
                encoding="utf-8",
            )
            (csspaint / "BUILD.gn").write_text(
                'blink_modules_sources("csspaint") {\n  sources = [\n  ]\n\n'
                + INTEGRATE.BLINK_CSSPAINT_BUILD_ANCHOR,
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_native_paint_definitions(csspaint)
            files = sorted(path for path in csspaint.rglob("*") if path.is_file())
            first = [path.read_text(encoding="utf-8") for path in files]
            INTEGRATE.patch_blink_native_paint_definitions(csspaint)
            self.assertEqual(first, [path.read_text(encoding="utf-8") for path in files])
        build, background, clip = first
        self.assertEqual(1, build.count('"//chromium/recorder_bridge"'))
        for patched in (background, clip):
            self.assertEqual(1, patched.count(INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n"))
            self.assertEqual(1, patched.count("a11y_recorder::RecordPaintWorkletPainted("))
        self.assertEqual(1, background.count(INTEGRATE.BLINK_BACKGROUND_COLOR_PAINTED_HOOK))
        self.assertEqual(1, clip.count(INTEGRATE.BLINK_CLIP_PATH_TRANSLATION_HOOK))
        self.assertEqual(1, clip.count(INTEGRATE.BLINK_CLIP_PATH_PAINTED_HOOK))
        # The path is recorded as drawn, before it is painted.
        self.assertLess(
            clip.index("RecordPaintWorkletPainted("),
            clip.index("cc::InspectablePaintRecorder paint_recorder;"),
        )

    def test_a_tree_patched_by_part_1b_is_upgraded_in_place(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "layer_tree_host_impl.cc"
            path.write_text(self.host_impl_source(), encoding="utf-8")
            helpers = INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS
            hook = INTEGRATE.CC_DRAW_LAYERS_HOOK
            try:
                INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS = (
                    INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS_1B
                )
                INTEGRATE.CC_DRAW_LAYERS_HOOK = INTEGRATE.CC_DRAW_LAYERS_HOOK_1B
                INTEGRATE.patch_cc_layer_tree_host_impl(path)
            finally:
                INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS = helpers
                INTEGRATE.CC_DRAW_LAYERS_HOOK = hook
            self.assertIn(
                INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS_1B,
                path.read_text(encoding="utf-8"),
            )
            INTEGRATE.patch_cc_layer_tree_host_impl(path)
            upgraded = path.read_text(encoding="utf-8")
        fresh = self.patch_twice(
            "layer_tree_host_impl.cc",
            self.host_impl_source(),
            INTEGRATE.patch_cc_layer_tree_host_impl,
        )
        self.assertEqual(fresh, upgraded)
        self.assertNotIn(INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS_1B, upgraded)
        self.assertNotIn(INTEGRATE.CC_DRAW_LAYERS_HOOK_1B, upgraded)
        self.assertEqual(1, upgraded.count("void RecorderRecordCompositorFrame("))
        self.assertEqual(1, upgraded.count("void RecorderReadImageFrames("))

    def test_each_frame_names_the_frame_each_animated_image_is_drawn_at(self):
        helpers = INTEGRATE.CC_COMPOSITOR_FRAME_HELPERS
        self.assertTrue(
            helpers.startswith(
                INTEGRATE.CC_PAINT_WORKLET_HELPERS + INTEGRATE.CC_IMAGE_FRAME_HELPERS
            )
        )
        self.assertIn(INTEGRATE.CC_FRAME_SIGNATURE, helpers)
        self.assertNotIn(INTEGRATE.CC_FRAME_SIGNATURE_1B, helpers)
        self.assertLess(
            helpers.index("RecorderReadImageFrames(host_id, images, &values);"),
            helpers.index(INTEGRATE.CC_FRAME_SCROLL_COMMENT),
        )
        self.assertIn('"image-frame"', helpers)
        self.assertIn("images->RecorderActiveFrameIndexes()", helpers)
        self.assertIn("image_animation_controller_.get(),", INTEGRATE.CC_DRAW_LAYERS_HOOK)
        # The frame hook is timed whole, after the check that the recorder is
        # connected, and so is the paint worklet results hook.
        self.assertLess(
            helpers.index(INTEGRATE.CC_FRAME_CLIENT_CHECK),
            helpers.index('A11Y_RECORDER_HOOK_COST("hook:compositor-frame");'),
        )
        self.assertEqual(1, helpers.count('A11Y_RECORDER_HOOK_COST("hook:compositor-frame");'))
        self.assertEqual(
            1, helpers.count('A11Y_RECORDER_HOOK_COST("hook:paint-worklet-results");')
        )
        header = self.patch_twice(
            "image_animation_controller.h",
            "class ImageAnimationController {\n public:\n"
            + INTEGRATE.CC_IMAGE_ANIMATION_CONTROLLER_ANCHOR
            + "\n private:\n  class AnimationState {\n   public:\n"
            + INTEGRATE.CC_IMAGE_ANIMATION_STATE_ANCHOR
            + "  };\n  AnimationStateMap animation_state_map_;\n};\n",
            INTEGRATE.patch_cc_image_animation_controller,
        )
        self.assertEqual(1, header.count(INTEGRATE.CC_IMAGE_ANIMATION_CONTROLLER_ACCESSOR))
        self.assertIn("state.active_index()", header)

    def test_paint_worklet_hooks_of_part_1b_are_upgraded_and_timed(self):
        def tree(background_hook, clip_hook):
            directory = tempfile.mkdtemp()
            csspaint = Path(directory) / "csspaint"
            nativepaint = csspaint / "nativepaint"
            nativepaint.mkdir(parents=True)
            (nativepaint / "background_color_paint_definition.cc").write_text(
                INTEGRATE.BLINK_BACKGROUND_COLOR_PAINT_OWN_INCLUDE
                + "\n\nPaintRecord BackgroundColorPaintDefinition::Paint() {\n"
                + background_hook
                + "}\n",
                encoding="utf-8",
            )
            (nativepaint / "clip_path_paint_definition.cc").write_text(
                INTEGRATE.BLINK_CLIP_PATH_PAINT_OWN_INCLUDE
                + "\n\nclass ClipPathPaintWorkletInput {\n"
                + INTEGRATE.BLINK_CLIP_PATH_TRANSLATION_ANCHOR
                + "};\n\nPaintRecord ClipPathPaintDefinition::Paint() {\n"
                + clip_hook
                + "}\n",
                encoding="utf-8",
            )
            (csspaint / "BUILD.gn").write_text(
                'blink_modules_sources("csspaint") {\n  sources = [\n  ]\n\n'
                + INTEGRATE.BLINK_CSSPAINT_BUILD_ANCHOR,
                encoding="utf-8",
            )
            INTEGRATE.patch_blink_native_paint_definitions(csspaint)
            INTEGRATE.patch_blink_native_paint_definitions(csspaint)
            files = sorted(path for path in csspaint.rglob("*") if path.is_file())
            return [path.read_text(encoding="utf-8") for path in files]

        fresh = tree(
            INTEGRATE.BLINK_BACKGROUND_COLOR_PAINTED_ANCHOR,
            INTEGRATE.BLINK_CLIP_PATH_PAINTED_ANCHOR,
        )
        upgraded = tree(
            INTEGRATE.BLINK_BACKGROUND_COLOR_PAINTED_HOOK_1B,
            INTEGRATE.BLINK_CLIP_PATH_PAINTED_HOOK_1B,
        )
        self.assertEqual(fresh, upgraded)
        _, background, clip = fresh
        self.assertNotIn(INTEGRATE.BLINK_BACKGROUND_COLOR_PAINTED_HOOK_1B, background)
        self.assertNotIn(INTEGRATE.BLINK_CLIP_PATH_PAINTED_HOOK_1B, clip)
        self.assertEqual(
            1, background.count('A11Y_RECORDER_HOOK_COST("hook:background-color-painted");')
        )
        self.assertEqual(1, clip.count('A11Y_RECORDER_HOOK_COST("hook:clip-path-painted");'))

    def test_the_cc_component_depends_on_the_bridge_once(self):
        source = (
            'cc_component("cc") {\n  sources = [\n  ]\n\n'
            '  deps = [\n    "//base",\n  ]\n}\n'
        )
        patched = self.patch_twice("BUILD.gn", source, INTEGRATE.patch_cc_build)
        self.assertEqual(1, patched.count(INTEGRATE.CC_BUILD_DEP))

    def test_an_animation_started_on_the_compositor_is_recorded_once(self):
        source = (
            INTEGRATE.BLINK_COMPOSITOR_ANIMATIONS_OWN_INCLUDE
            + "\n\nnamespace blink {\n\n"
            + INTEGRATE.BLINK_COMPOSITOR_ANIMATION_HELPERS_ANCHOR
            + "    const Element& element) {\n"
            + INTEGRATE.BLINK_COMPOSITOR_ANIMATION_STARTED_ANCHOR
            + "}\n\n}  // namespace blink\n"
        )
        patched = self.patch_twice(
            "compositor_animations.cc",
            source,
            INTEGRATE.patch_blink_compositor_animations,
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_COMPOSITOR_ANIMATION_STARTED_HOOK))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_COMPOSITOR_ANIMATION_HELPERS))
        # The models are read before the loop moves them to the compositor.
        self.assertLess(
            patched.index("a11y_recorder::RecordCompositorAnimationStarted("),
            patched.index("compositor_animation.AddKeyframeModel(std::move(keyframe_model));"),
        )

    def test_an_animation_leaving_the_compositor_is_recorded_once(self):
        source = (
            INTEGRATE.BLINK_KEYFRAME_EFFECT_OWN_INCLUDE
            + "\n\nbool KeyframeEffect::CancelAnimationOnCompositor(\n"
            + "    CompositorAnimation* compositor_animation) {\n"
            + INTEGRATE.BLINK_COMPOSITOR_ANIMATION_ENDED_ANCHOR
            + "  compositor_keyframe_model_ids_.clear();\n}\n"
        )
        patched = self.patch_twice(
            "keyframe_effect.cc", source, INTEGRATE.patch_blink_keyframe_effect
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_COMPOSITOR_ANIMATION_ENDED_HOOK))
        self.assertLess(
            patched.index("a11y_recorder::RecordCompositorAnimationEnded("),
            patched.index("compositor_keyframe_model_ids_.clear();"),
        )

    def test_each_animation_and_its_removal_are_recorded_once(self):
        source = (
            INTEGRATE.BLINK_ANIMATION_OWN_INCLUDE
            + "\n\n#include <limits>\n\nnamespace blink {\n\n"
            + INTEGRATE.BLINK_ANIMATION_DISPOSE_ANCHOR
            + "  DisassociateTriggers();\n}\n\n"
            + INTEGRATE.BLINK_ANIMATION_CONTEXT_DESTROYED_ANCHOR
            + "  inactive_ = true;\n}\n\n"
            + "void Animation::NotifyProbe() {\n"
            + INTEGRATE.BLINK_ANIMATION_UPDATED_ANCHOR
            + "}\n\n}  // namespace blink\n"
        )
        patched = self.patch_twice(
            "animation.cc", source, INTEGRATE.patch_blink_animation
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_ANIMATION_UPDATED_HOOK))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_ANIMATION_DISPOSE_HOOK))
        self.assertEqual(
            1, patched.count(INTEGRATE.BLINK_ANIMATION_CONTEXT_DESTROYED_HOOK)
        )
        for include in INTEGRATE.BLINK_ANIMATION_INCLUDES:
            with self.subTest(include=include):
                self.assertEqual(1, patched.count(include + "\n"))
        # The record follows the probe DevTools is fed from, and is made
        # only while the recorder is connected.
        self.assertLess(
            patched.index("probe::AnimationUpdated(document_, this);"),
            patched.index("a11y_recorder::RecordAnimationUpdated("),
        )
        self.assertEqual(3, patched.count("a11y_recorder::IsRecorderActive()"))
        self.assertEqual(
            1, patched.count('A11Y_RECORDER_HOOK_COST("hook:animation-updated");')
        )

    def test_the_bridge_holds_the_animation_records(self):
        bridge = MODULE_PATH.parent / "recorder_bridge"
        source = (bridge / "browser_bridge.cc").read_text(encoding="utf-8")
        for event_type in ("animation-updated", "animation-removed"):
            with self.subTest(event_type=event_type):
                self.assertIn(
                    f'SendBlinkEvidence("browser.animation", "{event_type}",', source
                )
        # An animation is recorded again only when its description changed,
        # and its current time and progress are not part of that comparison.
        compare = source[source.index("bool SameAnimationDescription("):]
        compare = compare[: compare.index("\n}\n")]
        for field in ("current_time_milliseconds", "progress", "current_iteration"):
            with self.subTest(field=field):
                self.assertNotIn(f"a.{field} ", compare)
                self.assertNotIn(f"a.{field} ==", compare)
        self.assertIn("a.play_state == b.play_state", compare)

    def test_each_presentation_request_names_its_compositors_widget(self):
        block = INTEGRATE.blink_registered_presentation_widget_block()
        self.assertEqual(1, block.count("a11y_recorder::RegisterCompositorWidget("))
        self.assertLess(
            block.index("a11y_recorder::RegisterCompositorWidget("),
            block.index("a11y_recorder::BeginBlinkPresentationRequest(\n          document_node_id, document_token"),
        )

    def test_the_bridge_holds_the_compositor_records(self):
        bridge = MODULE_PATH.parent / "recorder_bridge"
        source = (bridge / "browser_bridge.cc").read_text(encoding="utf-8")
        for event_type in (
            "compositor-animation-started",
            "compositor-animation-ended",
            "compositor-frame",
            "compositor-frame-presented",
        ):
            with self.subTest(event_type=event_type):
                self.assertIn(
                    f'SendBlinkEvidence("browser.compositor", "{event_type}",', source
                )


if __name__ == "__main__":
    unittest.main()


class RecreationIntegrationTests(unittest.TestCase):
    """The recreation mode's switch and its recorded styles hook."""

    STYLE_RESOLVER_SOURCE = (
        '#include "third_party/blink/renderer/core/css/resolver/'
        'style_resolver.h"\n'
        "\n"
        "#include <optional>\n"
        "\n"
        "void StyleResolver::MatchAllRules(StyleResolverState& state,\n"
        "                                  ElementRuleCollector& collector,\n"
        "                                  bool include_smil_properties) {\n"
        "  Element& element = state.GetElement();\n"
        "  MatchAuthorRules(element, collector);\n"
        "\n"
        "  if (element.IsStyledElement() && !state.IsForPseudoElement()) {\n"
        "    collector.BeginAddingAuthorRulesForTreeScope("
        "element.GetTreeScope());\n"
        "  }\n"
        "}\n"
        "\n"
        "const ComputedStyle& StyleResolver::StyleForViewport() {\n"
        "  return *builder.TakeStyle();\n"
        "}\n"
    )

    def test_adds_recorded_styles_last_in_rule_matching_once(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "style_resolver.cc"
            path.write_text(self.STYLE_RESOLVER_SOURCE, encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))

        self.assertEqual(1, first.count(INTEGRATE.BLINK_RECREATION_STYLE_HOOK))
        for include in INTEGRATE.BLINK_RECREATION_STYLE_INCLUDES:
            self.assertEqual(1, first.count(include + "\n"))
        # The recorded declarations come after every other author rule of
        # the element, at the end of MatchAllRules.
        match_all_rules = first.index("void StyleResolver::MatchAllRules(")
        author_rules = first.index("MatchAuthorRules(element, collector);")
        hook = first.index(INTEGRATE.BLINK_RECREATION_STYLE_HOOK)
        viewport = first.index("StyleResolver::StyleForViewport()")
        self.assertLess(match_all_rules, author_rules)
        self.assertLess(author_rules, hook)
        self.assertLess(hook, viewport)
        self.assertEqual(
            "\n}\n\nconst ComputedStyle& StyleResolver::StyleForViewport()",
            first[
                hook + len(INTEGRATE.BLINK_RECREATION_STYLE_HOOK) : viewport
                + len("StyleResolver::StyleForViewport()")
            ],
        )
        # Only under the switch, important, attached to the element, and not
        # kept in the matched properties cache.
        hook_text = INTEGRATE.BLINK_RECREATION_STYLE_HOOK
        self.assertIn("a11y_recorder::IsRecreationMode()", hook_text)
        self.assertIn("/*important=*/true", hook_text)
        self.assertIn("/*is_inline_style=*/true", hook_text)
        self.assertIn("/*is_cacheable=*/false", hook_text)
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(0, signatures["IsRecreationMode"])
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches("patched", first, signatures),
        )

    def test_upgrades_the_first_compositor_opacity_style_hook(self):
        legacy = INTEGRATE.STAGE_1E0B_BLINK_RECREATION_STYLE_HOOK
        self.assertIn("String::FromUTF8(", legacy)
        self.assertNotIn("String::FromUTF8(", INTEGRATE.BLINK_RECREATION_STYLE_HOOK)
        self.assertIn(
            '("opacity: " + *recorder_compositor_opacity).c_str()));',
            INTEGRATE.BLINK_RECREATION_STYLE_HOOK,
        )
        source = self.STYLE_RESOLVER_SOURCE.replace(
            INTEGRATE.BLINK_RECREATION_STYLE_ANCHOR,
            legacy + INTEGRATE.BLINK_RECREATION_STYLE_ANCHOR,
            1,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "style_resolver.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        self.assertNotIn(legacy, first)
        self.assertEqual(1, first.count(INTEGRATE.BLINK_RECREATION_STYLE_HOOK))

    def test_upgrades_the_inferred_display_style_hook_to_the_compositor_opacity(self):
        self.assertNotIn(
            INTEGRATE.STAGE_9915_BLINK_RECREATION_STYLE_HOOK,
            INTEGRATE.BLINK_RECREATION_STYLE_HOOK,
        )
        source = self.STYLE_RESOLVER_SOURCE.replace(
            INTEGRATE.BLINK_RECREATION_STYLE_ANCHOR,
            INTEGRATE.STAGE_9915_BLINK_RECREATION_STYLE_HOOK
            + INTEGRATE.BLINK_RECREATION_STYLE_ANCHOR,
            1,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "style_resolver.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        self.assertNotIn(INTEGRATE.STAGE_9915_BLINK_RECREATION_STYLE_HOOK, first)
        self.assertEqual(1, first.count(INTEGRATE.BLINK_RECREATION_STYLE_HOOK))
        hook = INTEGRATE.BLINK_RECREATION_STYLE_HOOK
        # The compositor's opacity is set last, as recorded, and never on a
        # user agent shadow copy.
        self.assertIn('AtomicString("data-a11y-recorded-compositor")', hook)
        self.assertIn("!element.IsInUserAgentShadowRoot()) {\n      recorder_compositor_opacity =", hook)
        self.assertLess(
            hook.index("recorder_impose(recorder_inferred_display);"),
            hook.index('("opacity: " + *recorder_compositor_opacity)'),
        )
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [], INTEGRATE.describe_signature_mismatches("patched", first, signatures)
        )

    def test_upgrades_the_stage_1a_style_hook_to_the_inferred_display(self):
        self.assertNotIn(
            INTEGRATE.STAGE_1A_BLINK_RECREATION_STYLE_HOOK,
            INTEGRATE.BLINK_RECREATION_STYLE_HOOK,
        )
        source = self.STYLE_RESOLVER_SOURCE.replace(
            INTEGRATE.BLINK_RECREATION_STYLE_ANCHOR,
            INTEGRATE.STAGE_1A_BLINK_RECREATION_STYLE_HOOK
            + INTEGRATE.BLINK_RECREATION_STYLE_ANCHOR,
            1,
        )
        self.assertIn(INTEGRATE.STAGE_1A_BLINK_RECREATION_STYLE_HOOK, source)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "style_resolver.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        self.assertNotIn(INTEGRATE.STAGE_1A_BLINK_RECREATION_STYLE_HOOK, first)
        self.assertEqual(1, first.count(INTEGRATE.BLINK_RECREATION_STYLE_HOOK))
        hook = INTEGRATE.BLINK_RECREATION_STYLE_HOOK
        # Only the two inferred values are accepted, and the inferred display
        # is set after the recorded style, so it replaces a recorded display.
        self.assertIn('"data-a11y-recorded-no-layout-object"', hook)
        self.assertIn('recorder_no_layout_object == "none"     ? "display: none"', hook)
        self.assertIn("element.IsInUserAgentShadowRoot()         ? nullptr", hook)
        self.assertIn('recorder_no_layout_object == "contents" ? "display: contents"', hook)
        self.assertLess(
            hook.index("recorder_impose(recorder_recorded_style);"),
            hook.index("recorder_impose(recorder_inferred_display);"),
        )
        self.assertIn("/*important=*/true", hook)
        # Blink's String names its prefix test starts_with; the hook uses no
        # such call, and no Node type.
        self.assertNotIn("StartsWith", hook)

    def test_the_switch_is_passed_to_renderers_without_a_recorder(self):
        bridge = MODULE_PATH.parent / "recorder_bridge"
        switches = (bridge / "recorder_switches.h").read_text(encoding="utf-8")
        self.assertIn(
            'inline constexpr char kRecreationSwitch[] = '
            '"a11y-recorder-recreation";',
            switches,
        )
        source = (bridge / "browser_bridge.cc").read_text(encoding="utf-8")
        start = source.index("bool AppendRecorderBootstrapToChildProcess(")
        end = source.index("\n}\n", start)
        function = source[start:end]
        appended = function.index("command_line->AppendSwitch(kRecreationSwitch);")
        # Passed before the bootstrap metadata is looked for, so a browser
        # started without a recorder passes it too, and only to renderers.
        self.assertLess(
            appended, function.index("kChildBootstrapMetadataEnvironment")
        )
        condition = function[: appended]
        self.assertIn("process_type == kChromiumRendererProcess", condition)
        self.assertIn(
            "base::CommandLine::ForCurrentProcess()->HasSwitch(kRecreationSwitch)",
            condition,
        )

    INSPECTOR_CSS_AGENT_SOURCE = (
        '#include "third_party/blink/renderer/core/inspector/'
        'inspector_css_agent.h"\n'
        "\n"
        "namespace blink {\n"
        "\n"
        "protocol::Response InspectorCSSAgent::getMatchedStylesForNode(\n"
        "    int node_id) {\n"
        "  // Matched rules.\n"
        "  *matched_css_rules = BuildArrayForMatchedRuleList(\n"
        "      resolver.MatchedRules(), element, ghost_rules, "
        "element_pseudo_id,\n"
        "      pseudo_argument);\n"
        "\n"
        "  // Inherited styles.\n"
        "  *inherited_entries =\n"
        "      std::make_unique<protocol::Array<"
        "protocol::CSS::InheritedStyleEntry>>();\n"
        "  for (InspectorCSSMatchedRules* match : resolver.ParentRules()) {\n"
        "    std::unique_ptr<protocol::CSS::InheritedStyleEntry> entry;\n"
        "    (*inherited_entries)->emplace_back(std::move(entry));\n"
        "  }\n"
        "}\n"
        "\n"
        "}  // namespace blink\n"
    )

    def test_reports_recorded_styles_to_devtools_once(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "inspector_css_agent.cc"
            path.write_text(self.INSPECTOR_CSS_AGENT_SOURCE, encoding="utf-8")
            INTEGRATE.patch_blink_inspector_css_agent(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_inspector_css_agent(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))

        for hook in (
            INTEGRATE.BLINK_RECREATION_INSPECTOR_HELPER,
            INTEGRATE.BLINK_RECREATION_INSPECTOR_MATCHED_HOOK,
            INTEGRATE.BLINK_RECREATION_INSPECTOR_INHERITED_HOOK,
            INTEGRATE.BLINK_RECREATION_NO_LAYOUT_OBJECT_HELPER,
            INTEGRATE.BLINK_RECREATION_NO_LAYOUT_OBJECT_MATCHED_HOOK,
        ):
            self.assertEqual(1, first.count(hook))
        # The inferred display is a rule of its own, defined after the
        # recorded style's helper and reported after its rule, so DevTools
        # shows it first; it is not reported for ancestors.
        inferred_helper = first.index(INTEGRATE.BLINK_RECREATION_NO_LAYOUT_OBJECT_HELPER)
        inferred_rule = first.index(INTEGRATE.BLINK_RECREATION_NO_LAYOUT_OBJECT_MATCHED_HOOK)
        self.assertLess(first.index(INTEGRATE.BLINK_RECREATION_INSPECTOR_HELPER), inferred_helper)
        self.assertLess(inferred_helper, first.index("InspectorCSSAgent::getMatchedStylesForNode("))
        self.assertLess(first.index(INTEGRATE.BLINK_RECREATION_INSPECTOR_MATCHED_HOOK), inferred_rule)
        self.assertLess(inferred_rule, first.index("  // Inherited styles."))
        self.assertIn(
            '.setText("No layout object recorded")',
            INTEGRATE.BLINK_RECREATION_NO_LAYOUT_OBJECT_HELPER,
        )
        self.assertIn(
            "element->IsInUserAgentShadowRoot()",
            INTEGRATE.BLINK_RECREATION_NO_LAYOUT_OBJECT_HELPER,
        )
        self.assertNotIn(
            "RecorderNoLayoutObjectMatch(match->element)",
            first,
        )
        for include in INTEGRATE.BLINK_RECREATION_INSPECTOR_INCLUDES:
            self.assertEqual(1, first.count(include + "\n"))
        # The helper is defined before its use; the element's block follows
        # every matched rule; each ancestor's block is added to its entry
        # before the entry is kept.
        helper = first.index(INTEGRATE.BLINK_RECREATION_INSPECTOR_HELPER)
        function = first.index("InspectorCSSAgent::getMatchedStylesForNode(")
        matched = first.index("*matched_css_rules = BuildArrayForMatchedRuleList(")
        own = first.index(INTEGRATE.BLINK_RECREATION_INSPECTOR_MATCHED_HOOK)
        inherited_list = first.index("  // Inherited styles.")
        inherited = first.index(INTEGRATE.BLINK_RECREATION_INSPECTOR_INHERITED_HOOK)
        kept = first.index("(*inherited_entries)->emplace_back(std::move(entry));")
        self.assertLess(helper, function)
        self.assertLess(matched, own)
        self.assertLess(own, inherited_list)
        self.assertLess(inherited_list, inherited)
        self.assertLess(inherited, kept)
        helper_text = INTEGRATE.BLINK_RECREATION_INSPECTOR_HELPER
        self.assertIn("a11y_recorder::IsRecreationMode()", helper_text)
        self.assertIn('.setText("Recorded style")', helper_text)
        self.assertIn("/*important=*/true", helper_text)
        self.assertIn("StyleSheetOriginEnum::Regular", helper_text)
        self.assertNotIn("setStyleSheetId", helper_text)
        self.assertIn(
            "element_pseudo_id == kPseudoIdNone",
            INTEGRATE.BLINK_RECREATION_INSPECTOR_MATCHED_HOOK,
        )
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches("patched", first, signatures),
        )

    BOX_FRAGMENT_BUILDER_SOURCE = (
        '#include "third_party/blink/renderer/core/layout/'
        'box_fragment_builder.h"\n'
        "\n"
        "namespace blink {\n"
        "\n"
        "const LayoutResult* BoxFragmentBuilder::ToBoxFragment(\n"
        "    WritingMode block_or_line_writing_mode) {\n"
        "  Finalize();\n"
        "\n"
        "  if (box_type_ == PhysicalFragment::kNormalBox && node_ &&\n"
        "      node_.IsBlockInInline()) [[unlikely]] {\n"
        "    SetIsBlockInInline();\n"
        "  }\n"
        "}\n"
        "\n"
        "}  // namespace blink\n"
    )

    def test_imposes_recorded_box_fragments_before_finalizing_once(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "box_fragment_builder.cc"
            path.write_text(self.BOX_FRAGMENT_BUILDER_SOURCE, encoding="utf-8")
            INTEGRATE.patch_blink_box_fragment_builder(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_box_fragment_builder(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))

        self.assertEqual(
            1, first.count(INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER)
        )
        self.assertEqual(1, first.count(INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK))
        for include in INTEGRATE.BLINK_RECREATION_FRAGMENT_INCLUDES:
            self.assertEqual(1, first.count(include + "\n"))
        # The helper precedes the function, and the recorded values are set
        # before the builder is finalized.
        helper = first.index(INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER)
        function = first.index("BoxFragmentBuilder::ToBoxFragment(")
        hook = first.index(INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK)
        finalize = first.index("  Finalize();")
        self.assertLess(helper, function)
        self.assertLess(function, hook)
        self.assertLess(hook, finalize)
        hook_text = INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK
        self.assertIn("a11y_recorder::IsRecreationMode()", hook_text)
        self.assertIn("GetWritingDirection().IsHorizontalLtr()", hook_text)
        self.assertIn("GetConstraintSpace().HasBlockFragmentation()", hook_text)
        # Slice 4a: children are matched to the recorded links by node, and
        # lines and anonymous boxes in order.
        self.assertIn("RecorderRecordedNodeId(recorder_child_node)", hook_text)
        self.assertIn("recorder_box_links.find(", hook_text)
        self.assertIn("recorder_lines == recorder_line_links.size()", hook_text)
        self.assertIn(
            "recorder_anonymous == recorder_anonymous_links.size()", hook_text
        )
        # In a BoxFragmentBuilder member, the Node class is hidden by Node().
        self.assertNotIn("const Node*", hook_text)
        self.assertIn("SetChildOffset(recorder_index,", hook_text)
        self.assertIn("RecorderReportNotImposed(", hook_text)
        helper = INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER
        self.assertIn('"data-a11y-recorded-layout"', helper)
        self.assertIn("int RecorderRecordedNodeId(const Node* node)", helper)
        self.assertIn('kRecorderPrefix[] = "{\\"node\\":"', helper)
        self.assertIn("JSONObject::From(ParseJSON(", helper)
        self.assertIn("/*discard_duplicates=*/true", helper)
        self.assertNotIn('"data-a11y-recorded-fragment"', first)
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches("patched", first, signatures),
        )

    def patch_source_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def test_imposes_recorded_items_after_conversion_once(self):
        source = (
            '#include "third_party/blink/renderer/core/layout/inline/'
            'fragment_items_builder.h"\n'
            "\n"
            "namespace blink {\n"
            "\n"
            "void FragmentItemsBuilder::ConvertToPhysical("
            "const PhysicalSize& outer_size) {\n"
            "  if (is_converted_to_physical_)\n"
            "    return;\n"
            "\n"
            "  is_converted_to_physical_ = true;\n"
            "}\n"
            "\n"
            "void FragmentItemsBuilder::MoveChildrenInDirection("
            "LayoutUnit offset,\n"
            "                                                   bool b) {}\n"
            "\n"
            "}  // namespace blink\n"
        )
        patched = self.patch_source_twice(
            "fragment_items_builder.cc",
            source,
            INTEGRATE.patch_blink_fragment_items_builder,
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_RECREATION_ITEMS_HELPER))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_RECREATION_ITEMS_HOOK))
        for include in INTEGRATE.BLINK_RECREATION_ITEMS_INCLUDES:
            self.assertEqual(1, patched.count(include + "\n"))
        # The helper precedes the function; the recorded items are set after
        # every item is converted, before the builder is marked converted.
        self.assertLess(
            patched.index(INTEGRATE.BLINK_RECREATION_ITEMS_HELPER),
            patched.index("void FragmentItemsBuilder::ConvertToPhysical("),
        )
        hook = patched.index(INTEGRATE.BLINK_RECREATION_ITEMS_HOOK)
        self.assertLess(patched.index("    return;\n"), hook)
        self.assertLess(hook, patched.index("  is_converted_to_physical_ = true;"))
        hook_text = INTEGRATE.BLINK_RECREATION_ITEMS_HOOK
        for expected in (
            "a11y_recorder::IsRecreationMode()",
            "GetWritingDirection().IsHorizontalLtr()",
            "RecorderRecordedItemsFragment(",
            "recorder_recorded_items->size() == items_.size()",
            "recorder_text == text_content_",
            "recorder_type == RecorderItemType(recorder_item)",
            "RecorderShapeFromRecordedGlyphs(",
            "recorder_item.RecorderSetTextShapeResult(",
            "RecorderReportNotImposed(",
        ):
            self.assertIn(expected, hook_text)
        helper = INTEGRATE.BLINK_RECREATION_ITEMS_HELPER
        for expected in (
            '"data-a11y-recorded-layout"',
            'recorder_kind != "anonymous"',
            'GetJSONObject("fontFile")',
            'GetString("digest", &recorder_digest)',
            "recorder_typeface.openStream(recorder_index)",
            "String::FromUtf8(recorder_chosen_digest) != recorder_digest",
            "recorder_chosen_index != recorder_file_index",
            "recorder_platform.size() - recorder_size",
            "Base64Decode(recorder_packed, recorder_bytes)",
            "ShapeResult::CreateFromRecordedGlyphs(",
        ):
            self.assertIn(expected, helper)
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches("patched", patched, signatures),
        )

    def test_items_helper_upgrades_to_font_file_matching(self):
        # Sub-step 3: a checkout patched with the stage 3 helper, which
        # compared PostScript names, is upgraded to the helper that compares
        # font files by digest.
        old = INTEGRATE.PRE_FONT_FILE_BLINK_RECREATION_ITEMS_HELPER
        self.assertIn('"postScriptName"', old)
        self.assertNotIn('"postScriptName"', INTEGRATE.BLINK_RECREATION_ITEMS_HELPER)
        upgraded = INTEGRATE.upgrade_legacy_hooks(
            "before\n" + old + "after\n",
            (
                (
                    INTEGRATE.PRE_FONT_FILE_BLINK_RECREATION_ITEMS_HELPER,
                    INTEGRATE.BLINK_RECREATION_ITEMS_HELPER,
                ),
            ),
            Path("fragment_items_builder.cc"),
        )
        self.assertEqual(
            "before\n" + INTEGRATE.BLINK_RECREATION_ITEMS_HELPER + "after\n",
            upgraded,
        )
        # Its glyph check also compares the size before reading the file.
        helper = INTEGRATE.BLINK_RECREATION_ITEMS_HELPER
        self.assertLess(
            helper.index("std::abs(recorder_platform.size() - recorder_size)"),
            helper.index("!RecorderRecreationFontFile(*recorder_typeface"),
        )

    def test_glyph_runs_record_their_font_file(self):
        # Protocol 0.40: each glyph run's typeface gives its font file, read
        # once in the renderer through Skia's openStream, and its variation
        # position.
        helper = INTEGRATE.BLINK_LAYOUT_CHECKPOINT_HELPER
        for expected in (
            "bool RecorderFontFile(const SkTypeface& recorder_typeface,",
            "a11y_recorder::LookUpFontFile(recorder_typeface.uniqueID(),",
            "recorder_typeface.openStream(recorder_index)",
            "a11y_recorder::RecordFontFile(",
            "RecorderReadRunFontFile(*recorder_typeface, recorder_run);",
            "getVariationDesignPosition(recorder_coordinates)",
        ):
            with self.subTest(expected=expected):
                self.assertEqual(1, helper.count(expected))
        self.assertLess(
            helper.index("void RecorderReadRunFontFile("),
            helper.index("void RecorderReadGlyph("),
        )
        self.assertIn(
            '#include "third_party/skia/include/core/SkStream.h"',
            INTEGRATE.BLINK_LAYOUT_CHANGES_INCLUDES,
        )

    def test_font_faces_record_joining_loading_and_leaving_once(self):
        header = self.patch_source_twice(
            "font_custom_platform_data.h",
            "class FontCustomPlatformData {\n public:\n"
            + INTEGRATE.BLINK_FONT_CUSTOM_PLATFORM_DATA_ACCESSOR_ANCHOR
            + "\n private:\n  sk_sp<SkTypeface> base_typeface_;\n};\n",
            INTEGRATE.patch_blink_font_custom_platform_data_header,
        )
        self.assertEqual(
            1, header.count(INTEGRATE.BLINK_FONT_CUSTOM_PLATFORM_DATA_ACCESSOR)
        )
        self.assertLess(
            header.index("RecorderBaseTypeface()"), header.index(" private:")
        )
        face_header = self.patch_source_twice(
            "font_face.h",
            "class FontFace {\n public:\n"
            + INTEGRATE.BLINK_FONT_FACE_PUBLIC_ANCHOR
            + "\n private:\n"
            + INTEGRATE.BLINK_FONT_FACE_PRIVATE_ANCHOR
            + "};\n",
            INTEGRATE.patch_blink_font_face_header,
        )
        self.assertEqual(1, face_header.count(INTEGRATE.BLINK_FONT_FACE_PUBLIC))
        self.assertLess(
            face_header.index("void RecorderNoteRemoved();"),
            face_header.index(" private:"),
        )
        self.assertGreater(
            face_header.index("uint64_t recorder_face_number_ = 0;"),
            face_header.index(" private:"),
        )
        definition = self.patch_source_twice(
            "font_face.cc",
            '#include "third_party/blink/renderer/core/css/font_face.h"\n'
            "\nnamespace blink {\n\n"
            + INTEGRATE.BLINK_FONT_FACE_DEFINITIONS_ANCHOR
            + "  status_ = status;\n"
            + INTEGRATE.BLINK_FONT_FACE_LOADED_ANCHOR
            + "  }\n}\n\n}  // namespace blink\n",
            INTEGRATE.patch_blink_font_face,
        )
        self.assertEqual(1, definition.count(INTEGRATE.BLINK_FONT_FACE_DEFINITIONS))
        self.assertEqual(1, definition.count(INTEGRATE.BLINK_FONT_FACE_LOADED_HOOK))
        self.assertLess(
            definition.index("void FontFace::RecorderNoteLoaded() {"),
            definition.index("void FontFace::SetLoadStatus("),
        )
        for expected in (
            "css_font_face_->FrontSource()",
            "recorder_source->GetCustomPlaftormData()",
            "recorder_data->RecorderBaseTypeface()",
            "DynamicTo<LocalDOMWindow>(recorder_context)",
            "a11y_recorder::RecordBlinkFontFaceLoaded(",
        ):
            with self.subTest(expected=expected):
                self.assertIn(expected, definition)
        cache = self.patch_source_twice(
            "font_face_cache.cc",
            "void FontFaceCache::AddFontFace(FontFace* font_face, bool css_connected) {\n"
            + INTEGRATE.BLINK_FONT_FACE_CACHE_ADD_ANCHOR
            + "}\nbool FontFaceCache::RemoveFontFace(FontFace* font_face, bool c) {\n"
            + INTEGRATE.BLINK_FONT_FACE_CACHE_REMOVE_ANCHOR
            + "  return true;\n}\nvoid FontFaceCache::ClearAll() {\n"
            + INTEGRATE.BLINK_FONT_FACE_CACHE_CLEAR_ANCHOR
            + "}\n",
            INTEGRATE.patch_blink_font_face_cache,
        )
        self.assertEqual(1, cache.count("font_face->RecorderNoteAdded();"))
        self.assertEqual(1, cache.count("font_face->RecorderNoteRemoved();"))
        self.assertEqual(1, cache.count("recorder_face->RecorderNoteRemoved();"))
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "font_face.cc", definition, signatures
            ),
        )

    def test_image_resources_record_their_bytes_before_they_are_cleared(self):
        source = self.patch_source_twice(
            "image_resource.cc",
            '#include "third_party/blink/renderer/core/loader/resource/'
            'image_resource.h"\n\nvoid ImageResource::Finish() {\n'
            "  if (a) {\n"
            + INTEGRATE.BLINK_IMAGE_RESOURCE_ANCHOR
            + "    ClearData();\n  }\n}\n",
            INTEGRATE.patch_blink_image_resource,
        )
        self.assertEqual(1, source.count(INTEGRATE.BLINK_IMAGE_RESOURCE_HOOK))
        self.assertIn(INTEGRATE.BLINK_BRIDGE_INCLUDE, source)
        # Protocol 0.48: the bytes are copied before the image is updated,
        # and recorded after it, with the image's own ID.
        self.assertLess(
            source.index("recorder_image->bytes.append("),
            source.index("UpdateImage(Data()"),
        )
        self.assertLess(
            source.index("UpdateImage(Data()"),
            source.index("a11y_recorder::RecordBlinkImageResource("),
        )
        self.assertLess(
            source.index("GetImage()->paint_image_id()"),
            source.index("a11y_recorder::RecordBlinkImageResource("),
        )
        self.assertEqual(1, source.count('A11Y_RECORDER_HOOK_COST("hook:image-resource");'))
        self.assertEqual(1, source.count("UpdateImage(Data()"))
        self.assertLess(
            source.index("UpdateImage(Data()"), source.index("ClearData();")
        )
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "image_resource.cc", source, signatures
            ),
        )

    def test_an_image_resource_hook_of_protocol_0_40_is_upgraded_in_place(self):
        original = (
            '#include "third_party/blink/renderer/core/loader/resource/'
            'image_resource.h"\n\nvoid ImageResource::Finish() {\n'
            "  if (a) {\n"
            + INTEGRATE.BLINK_IMAGE_RESOURCE_ANCHOR
            + "    ClearData();\n  }\n}\n"
        )
        fresh = self.patch_source_twice(
            "image_resource.cc", original, INTEGRATE.patch_blink_image_resource
        )
        earlier = original.replace(
            INTEGRATE.BLINK_IMAGE_RESOURCE_ANCHOR,
            INTEGRATE.BLINK_IMAGE_RESOURCE_HOOK_0_40,
        )
        upgraded = self.patch_source_twice(
            "image_resource.cc", earlier, INTEGRATE.patch_blink_image_resource
        )
        self.assertEqual(fresh, upgraded)
        self.assertNotIn(INTEGRATE.BLINK_IMAGE_RESOURCE_HOOK_0_40, upgraded)

    def test_an_image_resource_hook_of_part_1c_is_upgraded_in_place(self):
        original = (
            '#include "third_party/blink/renderer/core/loader/resource/'
            'image_resource.h"\n\nvoid ImageResource::Finish() {\n'
            "  if (a) {\n"
            + INTEGRATE.BLINK_IMAGE_RESOURCE_ANCHOR
            + "    ClearData();\n  }\n}\n"
        )
        fresh = self.patch_source_twice(
            "image_resource.cc", original, INTEGRATE.patch_blink_image_resource
        )
        earlier = original.replace(
            INTEGRATE.BLINK_IMAGE_RESOURCE_ANCHOR,
            INTEGRATE.BLINK_IMAGE_RESOURCE_HOOK_1C,
        )
        upgraded = self.patch_source_twice(
            "image_resource.cc", earlier, INTEGRATE.patch_blink_image_resource
        )
        self.assertEqual(fresh, upgraded)
        self.assertEqual(1, upgraded.count("X-A11y-Recorder-Image-Frame"))

    def test_the_recreation_holds_an_image_at_the_frame_its_answer_names(self):
        source = self.patch_source_twice(
            "image_resource.cc",
            '#include "third_party/blink/renderer/core/loader/resource/'
            'image_resource.h"\n\nvoid ImageResource::Finish() {\n'
            "  if (a) {\n"
            + INTEGRATE.BLINK_IMAGE_RESOURCE_ANCHOR
            + "    ClearData();\n  }\n}\n",
            INTEGRATE.patch_blink_image_resource,
        )
        # Held once the image has its bytes, only in the recreation mode, by
        # the image's own paint image ID, before the bytes are cleared.
        self.assertEqual(1, source.count("a11y_recorder::HoldRecreationImageFrame("))
        self.assertLess(
            source.index("UpdateImage(Data()"),
            source.index("a11y_recorder::HoldRecreationImageFrame("),
        )
        self.assertLess(
            source.index("a11y_recorder::HoldRecreationImageFrame("),
            source.index("ClearData();"),
        )
        self.assertIn(
            "if (a11y_recorder::IsRecreationMode() && GetContent()->HasImage()) {",
            source,
        )
        self.assertIn('AtomicString("X-A11y-Recorder-Image-Frame")', source)
        header = self.patch_source_twice(
            "image_animation_controller.h",
            "class ImageAnimationController {\n public:\n"
            + INTEGRATE.CC_IMAGE_ANIMATION_CONTROLLER_ANCHOR
            + "\n private:\n  class AnimationState {\n   public:\n"
            + INTEGRATE.CC_IMAGE_ANIMATION_STATE_ANCHOR
            + "   private:\n    std::vector<FrameMetadata> frames_;\n  };\n"
            "  AnimationStateMap animation_state_map_;\n};\n",
            INTEGRATE.patch_cc_image_animation_controller,
        )
        self.assertEqual(1, header.count(INTEGRATE.CC_IMAGE_ANIMATION_STATE_HOLD))
        self.assertEqual(1, header.count(INTEGRATE.CC_IMAGE_ANIMATION_CONTROLLER_ACCESSOR))
        # The hold is a member of AnimationState, beside its index accessor.
        self.assertLess(
            header.index("class AnimationState"), header.index("void RecorderHoldFrame(")
        )
        self.assertIn("if (index >= frames_.size()) {", header)
        controller = self.patch_source_twice(
            "image_animation_controller.cc",
            INTEGRATE.CC_IMAGE_ANIMATION_CONTROLLER_OWN_INCLUDE
            + "\n\nvoid ImageAnimationController::UpdateAnimatedImage(\n"
            "    const DiscardableImageMap::AnimatedImageMetadata& data) {\n"
            + INTEGRATE.CC_IMAGE_ANIMATION_UPDATE_ANCHOR
            + "}\n\n"
            + INTEGRATE.CC_IMAGE_ANIMATION_SHOULD_ANIMATE_ANCHOR
            + "  return ShouldAnimate(0, 0);\n}\n",
            INTEGRATE.patch_cc_image_animation_controller_source,
        )
        self.assertEqual(1, controller.count(INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n"))
        self.assertEqual(1, controller.count(INTEGRATE.CC_IMAGE_ANIMATION_UPDATE_HOOK))
        self.assertEqual(
            1, controller.count(INTEGRATE.CC_IMAGE_ANIMATION_SHOULD_ANIMATE_HOOK)
        )
        # The hold follows the metadata, which may reset the image's indexes.
        self.assertLess(
            controller.index("animation_state.UpdateMetadata("),
            controller.index("animation_state.RecorderHoldFrame("),
        )
        self.assertLess(
            controller.index("if (a11y_recorder::IsRecreationMode()) {\n    return false;"),
            controller.index("return ShouldAnimate(0, 0);"),
        )
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        for name, text in (
            ("image_resource.cc", source),
            ("image_animation_controller.cc", controller),
        ):
            self.assertEqual(
                [], INTEGRATE.describe_signature_mismatches(name, text, signatures)
            )

    def test_the_held_image_frames_pass_their_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        build = (bridge / "BUILD.gn").read_text(encoding="utf-8")
        self.assertIn('"recreation_image_frames.h",', build)
        source = (bridge / "browser_bridge.cc").read_text(encoding="utf-8")
        self.assertIn("HeldImageFrames<base::Lock, base::AutoLock>", source)
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "recreation_image_frames_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    "-pthread",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "recreation_image_frames_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_the_compositor_values_pass_their_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        build = (bridge / "BUILD.gn").read_text(encoding="utf-8")
        self.assertIn('"recreation_compositor_values.h",', build)
        source = (bridge / "browser_bridge.cc").read_text(encoding="utf-8")
        self.assertIn("RecreationCompositorValues RecreationCompositorValuesOf(", source)
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "recreation_compositor_values_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "recreation_compositor_values_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_the_paint_worklet_values_pass_their_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        build = (bridge / "BUILD.gn").read_text(encoding="utf-8")
        self.assertIn('"recreation_paint_worklet_values.h",', build)
        header = (bridge / "browser_bridge.h").read_text(encoding="utf-8")
        self.assertIn(
            '#include "chromium/recorder_bridge/recreation_paint_worklet_values.h"',
            header,
        )
        source = (bridge / "browser_bridge.cc").read_text(encoding="utf-8")
        self.assertIn(
            "RecreationPaintWorkletValues RecreationPaintWorkletValuesOf(\n", source
        )
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "recreation_paint_worklet_values_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "recreation_paint_worklet_values_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)

    def test_upgrades_the_compositor_opacity_style_hook_to_the_paint_worklet_color(self):
        legacy = INTEGRATE.STAGE_E4C6_BLINK_RECREATION_STYLE_HOOK
        hook = INTEGRATE.BLINK_RECREATION_STYLE_HOOK
        self.assertNotEqual(legacy, hook)
        self.assertNotIn("data-a11y-recorded-paint-worklet", legacy)
        source = self.STYLE_RESOLVER_SOURCE.replace(
            INTEGRATE.BLINK_RECREATION_STYLE_ANCHOR,
            legacy + INTEGRATE.BLINK_RECREATION_STYLE_ANCHOR,
            1,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "style_resolver.cc"
            path.write_text(source, encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            first = path.read_text(encoding="utf-8")
            INTEGRATE.patch_blink_style_resolver(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
        self.assertNotIn(legacy, first)
        self.assertEqual(1, first.count(hook))
        # The paint worklet's color is read from the element's own attribute,
        # never a user agent shadow copy's, and set last, after the opacity.
        self.assertIn('AtomicString("data-a11y-recorded-paint-worklet")', hook)
        self.assertIn(
            "!element.IsInUserAgentShadowRoot()) {\n      recorder_worklet_color =",
            hook,
        )
        self.assertIn(
            "recorder_compositor_opacity || recorder_worklet_color) {", hook
        )
        self.assertLess(
            hook.index('("opacity: " + *recorder_compositor_opacity)'),
            hook.index('"background-color: color(srgb "'),
        )
        self.assertLess(
            hook.index('"background-color: color(srgb "'),
            hook.index("collector.BeginAddingAuthorRulesForTreeScope("),
        )
        self.assertIn('recorder_color[2] + " / " +', hook)
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(1, signatures["RecreationPaintWorkletValuesOf"])
        self.assertEqual(
            [], INTEGRATE.describe_signature_mismatches("patched", first, signatures)
        )

    # Slice 4e (protocol 0.51): page style sheets as they arrive.
    CSS_STYLE_SHEET_HEADER_SOURCE = (
        "class CORE_EXPORT CSSStyleSheet final : public StyleSheet {\n"
        " public:\n"
        "  void Trace(Visitor*) const override;\n"
        "\n"
        " private:\n"
        "  friend class QuietMutationScope;\n"
        "};\n"
    )
    CSS_STYLE_SHEET_SOURCE = (
        '#include "third_party/blink/renderer/core/css/css_style_sheet.h"\n'
        "\n"
        "namespace blink {\n"
        "\n"
        "void CSSStyleSheet::DidMutate(Mutation mutation) {\n"
        "  if (mutation == Mutation::kRules) {\n"
        "  }\n"
        "}\n"
        "\n"
        "bool CSSStyleSheet::CanAccessRules() const {\n"
        "  return enable_rule_access_for_inspector_ || contents_->IsOriginClean();\n"
        "}\n"
        "\n"
        "void CSSStyleSheet::SetText(const String& text, CSSImportRules import_rules) {\n"
        "  DetachCSSOMWrappers();\n"
        "}\n"
        "\n"
        "void CSSStyleSheet::Trace(Visitor* visitor) const {\n"
        "}\n"
        "\n"
        "}  // namespace blink\n"
    )
    STYLE_SHEET_CONTENTS_HEADER_SOURCE = (
        "class CORE_EXPORT StyleSheetContents final {\n"
        " public:\n"
        "  void Trace(Visitor*) const;\n"
        "\n"
        " private:\n"
        "  StyleSheetContents& operator=(const StyleSheetContents&) = delete;\n"
        "};\n"
    )
    STYLE_SHEET_CONTENTS_SOURCE = (
        '#include "third_party/blink/renderer/core/css/style_sheet_contents.h"\n'
        "\n"
        "void StyleSheetContents::ParseAuthorStyleSheet(\n"
        "    const CSSStyleSheetResource* cached_style_sheet) {\n"
        "  const ResourceResponse& response = cached_style_sheet->GetResponse();\n"
        "  String sheet_text =\n"
        "      cached_style_sheet->SheetText(parser_context_, mime_type_check);\n"
        "  CSSParser::ParseSheet(context, this, sheet_text);\n"
        "}\n"
    )
    STYLE_ENGINE_SOURCE = (
        '#include "third_party/blink/renderer/core/css/style_engine.h"\n'
        "\n"
        "namespace blink {\n"
        "\n"
        "void StyleEngine::UpdateActiveStyleSheets() {\n"
        "  probe::ActiveStyleSheetsUpdated(document_);\n"
        "\n"
        "  dirty_tree_scopes_.clear();\n"
        "}\n"
        "\n"
        "}  // namespace blink\n"
    )

    def test_records_style_sheet_changes_once(self):
        header = self.patch_source_twice(
            "css_style_sheet.h",
            self.CSS_STYLE_SHEET_HEADER_SOURCE,
            INTEGRATE.patch_blink_css_style_sheet_header,
        )
        self.assertEqual(1, header.count(INTEGRATE.BLINK_CSS_STYLE_SHEET_HEADER))
        # The members are public, before the private section.
        self.assertLess(
            header.index("uint64_t recorder_sheet_number_ = 0;"),
            header.index(" private:"),
        )
        source = self.patch_source_twice(
            "css_style_sheet.cc",
            self.CSS_STYLE_SHEET_SOURCE,
            INTEGRATE.patch_blink_css_style_sheet,
        )
        self.assertEqual(1, source.count(INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n"))
        for hook in (
            INTEGRATE.BLINK_CSS_STYLE_SHEET_DID_MUTATE_HOOK,
            INTEGRATE.BLINK_CSS_STYLE_SHEET_SET_TEXT_HOOK,
            INTEGRATE.BLINK_CSS_STYLE_SHEET_CAN_ACCESS_HOOK,
            INTEGRATE.BLINK_CSS_STYLE_SHEET_DEFINITIONS,
        ):
            self.assertEqual(1, source.count(hook))
        # A change of the sheet's disabled state alone is not a change of its
        # rules; replace() and replaceSync() are.
        self.assertIn(
            "if (mutation != Mutation::kSheet) {\n    recorder_cssom_changed_ = true;",
            source,
        )
        # Rules are reachable by the builder only in the recreation mode.
        self.assertIn(
            "if (a11y_recorder::IsRecreationMode()) {\n    return true;\n  }",
            source,
        )
        # The CSSOM text is built as DevTools builds it.
        self.assertIn("recorder_builder.Append(ItemInternal(i)->cssText());", source)

    def test_records_the_text_a_style_sheet_arrived_with_once(self):
        header = self.patch_source_twice(
            "style_sheet_contents.h",
            self.STYLE_SHEET_CONTENTS_HEADER_SOURCE,
            INTEGRATE.patch_blink_style_sheet_contents_header,
        )
        self.assertEqual(1, header.count("String recorder_arrived_digest_;"))
        source = self.patch_source_twice(
            "style_sheet_contents.cc",
            self.STYLE_SHEET_CONTENTS_SOURCE,
            INTEGRATE.patch_blink_style_sheet_contents,
        )
        self.assertEqual(1, source.count(INTEGRATE.BLINK_STYLE_SHEET_CONTENTS_PARSE_HOOK))
        # Recorded after the text is decoded and before it is parsed.
        self.assertLess(
            source.index("RecordBlinkStyleSheetResource"),
            source.index("CSSParser::ParseSheet"),
        )
        self.assertIn("recorder_sheet.text = sheet_text.Utf8();", source)

    def test_records_each_active_style_sheet_update_once(self):
        source = self.patch_source_twice(
            "style_engine.cc",
            self.STYLE_ENGINE_SOURCE,
            INTEGRATE.patch_blink_style_engine_style_sheets,
        )
        for include in INTEGRATE.BLINK_STYLE_ENGINE_STYLE_SHEETS_INCLUDES:
            self.assertEqual(1, source.count(include + "\n"))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_STYLE_ENGINE_STYLE_SHEETS_HELPERS))
        self.assertEqual(1, source.count("a11y_recorder::RecordBlinkStyleSheetsUpdated("))
        # The helpers come before the update, and the record before the probe,
        # while the dirty tree scopes are still known.
        self.assertLess(
            source.index("void RecorderAppendStyleSheet("),
            source.index("void StyleEngine::UpdateActiveStyleSheets() {"),
        )
        self.assertLess(
            source.index("RecordBlinkStyleSheetsUpdated("),
            source.index("probe::ActiveStyleSheetsUpdated(document_);"),
        )
        self.assertLess(
            source.index("probe::ActiveStyleSheetsUpdated(document_);"),
            source.index("dirty_tree_scopes_.clear();"),
        )
        helpers = INTEGRATE.BLINK_STYLE_ENGINE_STYLE_SHEETS_HELPERS
        # Only a sheet script changed, or a constructed one, is serialized,
        # and only when its rules changed since it last was.
        self.assertIn(
            "if (sheet.recorder_cssom_changed_ || sheet.recorder_cssom_digest_.empty()) {",
            helpers,
        )
        self.assertIn("sheet.IsConstructed() || (contents && contents->IsMutable())", helpers)
        # Imports follow the sheet that imports them.
        self.assertIn("DynamicTo<CSSImportRule>(sheet.ItemInternal(i))", helpers)
        self.assertIn("for (CSSStyleSheet* sheet : *tree_scope.AdoptedStyleSheets())", helpers)

    def test_upgrades_the_first_style_sheet_hooks(self):
        # blink::String has FromUtf8 of a byte span, not FromUTF8.
        self.assertIn("String::FromUTF8(", INTEGRATE.STAGE_AB95_BLINK_STYLE_SHEET_CONTENTS_PARSE_HOOK)
        self.assertEqual(2, INTEGRATE.STAGE_AB95_BLINK_STYLE_ENGINE_STYLE_SHEETS_HELPERS.count("String::FromUTF8("))
        for current in (
            INTEGRATE.BLINK_STYLE_SHEET_CONTENTS_PARSE_HOOK,
            INTEGRATE.BLINK_STYLE_ENGINE_STYLE_SHEETS_HELPERS,
        ):
            self.assertNotIn("FromUTF8", current)
            self.assertIn("String::FromUtf8(base::as_byte_span(", current)
        legacy_contents = self.STYLE_SHEET_CONTENTS_SOURCE.replace(
            INTEGRATE.BLINK_STYLE_SHEET_CONTENTS_PARSE_ANCHOR,
            INTEGRATE.STAGE_AB95_BLINK_STYLE_SHEET_CONTENTS_PARSE_HOOK,
            1,
        )
        patched = self.patch_source_twice(
            "style_sheet_contents.cc", legacy_contents, INTEGRATE.patch_blink_style_sheet_contents
        )
        self.assertNotIn("FromUTF8", patched)
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_STYLE_SHEET_CONTENTS_PARSE_HOOK))
        legacy_engine = self.STYLE_ENGINE_SOURCE.replace(
            INTEGRATE.BLINK_STYLE_ENGINE_STYLE_SHEETS_HELPERS_ANCHOR,
            INTEGRATE.STAGE_AB95_BLINK_STYLE_ENGINE_STYLE_SHEETS_HELPERS
            + INTEGRATE.BLINK_STYLE_ENGINE_STYLE_SHEETS_HELPERS_ANCHOR,
            1,
        )
        patched = self.patch_source_twice(
            "style_engine.cc", legacy_engine, INTEGRATE.patch_blink_style_engine_style_sheets
        )
        self.assertNotIn("FromUTF8", patched)
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_STYLE_ENGINE_STYLE_SHEETS_HELPERS))

    # Slice 4f (protocol 0.52): who scheduled each timer.
    def test_notes_who_scheduled_a_timer_before_its_record_once(self):
        source = (
            '#include "third_party/blink/renderer/core/scheduler/dom_timer.h"\n'
            "\n"
            "namespace blink {\n"
            "namespace {\n"
            "constexpr int kValue = 1;\n"
            "}  // namespace\n"
            "\n"
            "DOMTimer::DOMTimer(ExecutionContext& context,\n"
            "                   ScheduledAction* action) {\n"
            + INTEGRATE.BLINK_TIMER_SCHEDULED_HOOK
            + "}\n"
            "\n"
            "}  // namespace blink\n"
        )
        patched = self.patch_source_twice(
            "dom_timer.cc", source, INTEGRATE.patch_blink_dom_timer_origin
        )
        for include in INTEGRATE.BLINK_DOM_TIMER_ORIGIN_INCLUDES:
            self.assertEqual(1, patched.count(include + "\n"))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_DOM_TIMER_ORIGIN_HELPERS))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_TIMER_ORIGIN_HOOK))
        # The helpers are in the file's anonymous namespace, and the origin is
        # noted before the scheduled record that takes it.
        self.assertLess(
            patched.index("RecorderTimerOrigin(ExecutionContext& context,"),
            patched.index("}  // namespace\n"),
        )
        self.assertLess(
            patched.index("NoteBlinkTimerOrigin("),
            patched.index("RecordBlinkTimerScheduled("),
        )
        helpers = INTEGRATE.BLINK_DOM_TIMER_ORIGIN_HELPERS
        self.assertIn("DOMWrapperWorld::Current(isolate)", helpers)
        self.assertIn("v8::StackTrace::CurrentStackTrace(", helpers)
        self.assertIn("kMaximumTimerOriginFrames", helpers)
        self.assertIn("action->CallbackFunction()", helpers)
        self.assertIn("origin.string_handler = true;", helpers)
        # V8 gives a function's position zero-based and a stack frame's
        # one-based; both are recorded one-based.
        self.assertIn("line >= 0 ? line + 1 : 0", helpers)

    def test_notes_the_element_of_a_running_script_once(self):
        source = (
            '#include "third_party/blink/renderer/core/script/pending_script.h"\n'
            "\n"
            "namespace blink {\n"
            "\n"
            + INTEGRATE.BLINK_PENDING_SCRIPT_HELPERS_ANCHOR
            + "    Script* script,\n"
            "    ScriptElementBase* element,\n"
            "    bool is_external) {\n"
            "    context_document->PushCurrentScript(current_script);\n"
            + INTEGRATE.BLINK_PENDING_SCRIPT_RUN_ANCHOR
            + "    context_document->PopCurrentScript(current_script);\n"
            "}\n"
            "\n"
            "}  // namespace blink\n"
        )
        patched = self.patch_source_twice(
            "pending_script.cc", source, INTEGRATE.patch_blink_pending_script
        )
        for include in INTEGRATE.BLINK_PENDING_SCRIPT_INCLUDES:
            self.assertEqual(1, patched.count(include + "\n"))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_PENDING_SCRIPT_HELPERS))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_PENDING_SCRIPT_RUN_HOOK))
        self.assertEqual(1, patched.count("script->RunScript("))
        # The element is noted just before the run and forgotten just after.
        self.assertLess(
            patched.index("RecorderNoteScriptElement(\n        script"),
            patched.index("script->RunScript("),
        )
        self.assertLess(
            patched.index("script->RunScript("),
            patched.index("PopBlinkScriptElement("),
        )
        helpers = INTEGRATE.BLINK_PENDING_SCRIPT_HELPERS
        self.assertIn("record->IsSourceTextModule()", helpers)
        self.assertIn("a11y_recorder::RecordBlinkScriptSource(", helpers)
        self.assertIn("a11y_recorder::PushBlinkScriptElement(", helpers)
        # Only an external script's address is recorded.
        self.assertIn("if (is_external) {", helpers)

    def test_records_the_script_id_of_a_classic_script_once(self):
        source = (
            INTEGRATE.BLINK_V8_SCRIPT_RUNNER_OWN_INCLUDE
            + "\n\n"
            "    if (V8ScriptRunner::CompileScript(script_state, *classic_script)\n"
            "            .ToLocal(&script)) {\n"
            + INTEGRATE.BLINK_V8_SCRIPT_RUNNER_COMPILED_ANCHOR
            + "      maybe_result = V8ScriptRunner::RunCompiledScript(isolate, script);\n"
            "    }\n"
        )
        patched = self.patch_source_twice(
            "v8_script_runner.cc", source, INTEGRATE.patch_blink_v8_script_runner
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n"))
        self.assertEqual(1, patched.count("RecordBlinkClassicScriptCompiled("))
        # Recorded after the compile and before the run, which may schedule
        # timers from the script.
        self.assertLess(
            patched.index("RecordBlinkClassicScriptCompiled("),
            patched.index("RunCompiledScript("),
        )

    def test_records_the_element_of_an_attribute_handler_once(self):
        source = (
            INTEGRATE.BLINK_CONTENT_ATTRIBUTE_HANDLER_OWN_INCLUDE
            + "\n\n"
            "  if (!maybe_result.ToLocal(&compiled_function))\n"
            "    return v8::Null(isolate);\n"
            "\n"
            + INTEGRATE.BLINK_CONTENT_ATTRIBUTE_HANDLER_ANCHOR
            + "  compiled_function->SetName(V8String(isolate, function_name_));\n"
        )
        patched = self.patch_source_twice(
            "js_event_handler_for_content_attribute.cc",
            source,
            INTEGRATE.patch_blink_content_attribute_handler,
        )
        for include in INTEGRATE.BLINK_CONTENT_ATTRIBUTE_HANDLER_INCLUDES:
            self.assertEqual(1, patched.count(include + "\n"))
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_CONTENT_ATTRIBUTE_HANDLER_HOOK))
        self.assertEqual(1, patched.count("// Step 12."))
        hook = INTEGRATE.BLINK_CONTENT_ATTRIBUTE_HANDLER_HOOK
        self.assertIn("compiled_function->ScriptId()", hook)
        self.assertIn("kScriptSourceKindEventHandlerAttribute", hook)
        self.assertIn("function_name_.Utf8()", hook)
        self.assertIn("window ? document->body() : nullptr", hook)

    def test_slice_4f_hooks_call_declared_bridge_functions(self):
        header = (Path(__file__).parent / "recorder_bridge" / "browser_bridge.h").read_text(
            encoding="utf-8"
        )
        for name in (
            "NoteBlinkTimerOrigin",
            "RecordBlinkScriptSource",
            "PushBlinkScriptElement",
            "PopBlinkScriptElement",
            "RecordBlinkClassicScriptCompiled",
        ):
            self.assertIn(f"void {name}(", header)

    def test_style_sheet_bridge_calls_match_the_bridge(self):
        bridge = (Path(__file__).parent / "recorder_bridge" / "browser_bridge.h").read_text(encoding="utf-8")
        for name in (
            "RecordBlinkStyleSheetResource",
            "RecordBlinkStyleSheetText",
            "AssignStyleSheetNumber",
            "RecordBlinkStyleSheetsUpdated",
        ):
            self.assertIn(name + "(", bridge)
        signatures = INTEGRATE.parse_bridge_signatures(bridge)
        INTEGRATE.verify_hook_templates(signatures)

    CLIP_PATH_CLIPPER_SOURCE = (
        INTEGRATE.BLINK_CLIP_PATH_CLIPPER_OWN_INCLUDE
        + "\n\nnamespace blink {\n\n"
        + INTEGRATE.BLINK_CLIP_PATH_CLIPPER_HELPERS_ANCHOR
        + INTEGRATE.BLINK_CLIP_PATH_BOUNDING_BOX_ANCHOR
        + "  return std::nullopt;\n}\n\n"
        + INTEGRATE.BLINK_CLIP_PATH_PATH_BASED_ANCHOR
        + "  return std::nullopt;\n}\n\n}  // namespace blink\n"
    )

    def test_upgrades_the_first_paint_worklet_clip_path_helper(self):
        legacy = INTEGRATE.STAGE_5616_BLINK_CLIP_PATH_CLIPPER_HELPERS
        helpers = INTEGRATE.BLINK_CLIP_PATH_CLIPPER_HELPERS
        # DOMNodeIds::IdForNode takes a Node, not a const Element.
        self.assertIn("DOMNodeIds::IdForNode(recorder_element)", legacy)
        self.assertNotIn("DOMNodeIds::IdForNode(recorder_element)", helpers)
        self.assertIn(
            "Node* recorder_node = object.GetNode();\n"
            "    const DOMNodeId recorder_id = DOMNodeIds::IdForNode(recorder_node);",
            helpers,
        )
        source = self.CLIP_PATH_CLIPPER_SOURCE.replace(
            INTEGRATE.BLINK_CLIP_PATH_CLIPPER_HELPERS_ANCHOR,
            legacy + INTEGRATE.BLINK_CLIP_PATH_CLIPPER_HELPERS_ANCHOR,
            1,
        )
        patched = self.patch_source_twice(
            "clip_path_clipper.cc", source, INTEGRATE.patch_blink_clip_path_clipper
        )
        self.assertNotIn(legacy, patched)
        self.assertEqual(1, patched.count(helpers))

    def test_imposes_the_paint_worklet_clip_paths_once(self):
        source = self.patch_source_twice(
            "clip_path_clipper.cc",
            self.CLIP_PATH_CLIPPER_SOURCE,
            INTEGRATE.patch_blink_clip_path_clipper,
        )
        for include in INTEGRATE.BLINK_CLIP_PATH_CLIPPER_INCLUDES:
            self.assertEqual(1, source.count(include + "\n"))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_CLIP_PATH_CLIPPER_HELPERS))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_CLIP_PATH_BOUNDING_BOX_HOOK))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_CLIP_PATH_PATH_BASED_HOOK))
        # The helper comes before both uses.
        self.assertLess(
            source.index("static std::optional<Path> RecorderPaintWorkletClipPath("),
            source.index("std::optional<gfx::RectF> ClipPathClipper::LocalClipPathBoundingBox("),
        )
        helpers = INTEGRATE.BLINK_CLIP_PATH_CLIPPER_HELPERS
        # Only while the recreation holds time, from the element's own
        # attribute, and only for a basic shape the style gives it.
        self.assertIn("if (!a11y_recorder::RecreationHoldsTime() || object.IsAnonymous() ||", helpers)
        self.assertIn("recorder_element->IsInUserAgentShadowRoot()", helpers)
        self.assertIn("!IsA<ShapeClipPathOperation>(*recorder_operation)", helpers)
        self.assertIn('AtomicString("data-a11y-recorded-paint-worklet")', helpers)
        # The recorded points are used unchanged at the recorded origin, and
        # moved, with a console message, elsewhere.
        self.assertIn("recorder_dx != 0 || recorder_dy != 0", helpers)
        self.assertIn("if (recorder_moved && report) {", helpers)
        self.assertIn("/*discard_duplicates=*/true", helpers)
        # The bounding box is without the paint offset and says nothing; the
        # path-based clip is at the paint offset.
        self.assertIn(
            "object, gfx::Vector2dF(), /*report=*/false)",
            INTEGRATE.BLINK_CLIP_PATH_BOUNDING_BOX_HOOK,
        )
        self.assertIn(
            "clip_path_owner, clip_offset, /*report=*/true)",
            INTEGRATE.BLINK_CLIP_PATH_PATH_BASED_HOOK,
        )
        # The path-based clip hook comes before Blink's own clip.
        hook = source.index(INTEGRATE.BLINK_CLIP_PATH_PATH_BASED_HOOK)
        self.assertEqual(
            "  return std::nullopt;\n}",
            source[
                hook + len(INTEGRATE.BLINK_CLIP_PATH_PATH_BASED_HOOK) : hook
                + len(INTEGRATE.BLINK_CLIP_PATH_PATH_BASED_HOOK)
                + len("  return std::nullopt;\n}")
            ],
        )
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [], INTEGRATE.describe_signature_mismatches("patched", source, signatures)
        )

    def test_holds_css_animations_and_transitions_once(self):
        source = self.patch_source_twice(
            "css_animations.cc",
            INTEGRATE.BLINK_CSS_ANIMATIONS_OWN_INCLUDE
            + "\n\nnamespace blink {\n\n"
            + INTEGRATE.BLINK_CSS_ANIMATION_UPDATE_ANCHOR
            + "  update.Clear();\n}\n\n"
            + INTEGRATE.BLINK_CSS_TRANSITION_UPDATE_ANCHOR
            + "  update.Clear();\n}\n\n}  // namespace blink\n",
            INTEGRATE.patch_blink_css_animations,
        )
        self.assertEqual(1, source.count(INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n"))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_CSS_ANIMATION_UPDATE_HOOK))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_CSS_TRANSITION_UPDATE_HOOK))
        # Each returns before it changes the update.
        self.assertEqual(2, source.count("if (a11y_recorder::RecreationHoldsTime()) {\n    return;\n  }\n  update.Clear();"))
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(0, signatures["RecreationHoldsTime"])
        self.assertEqual(
            [], INTEGRATE.describe_signature_mismatches("patched", source, signatures)
        )

    PAINT_PROPERTY_SOURCE = (
        INTEGRATE.BLINK_PAINT_PROPERTY_TREE_BUILDER_OWN_INCLUDE
        + "\n\nnamespace blink {\nnamespace {\n\n"
        + INTEGRATE.BLINK_PAINT_PROPERTY_HELPERS_ANCHOR
        + "  return false;\n}\n\n"
        + "void FragmentPaintPropertyTreeBuilder::UpdateIndividualTransform() {\n"
        + "    if (needs) {\n"
        + INTEGRATE.BLINK_PAINT_PROPERTY_TRANSFORM_ANCHOR
        + INTEGRATE.BLINK_PAINT_PROPERTY_NO_TRANSFORM_ANCHOR
        + "}\n\n"
        + "static void UpdateFilterEffect(const LayoutObject& object) {\n"
        + INTEGRATE.BLINK_PAINT_PROPERTY_FILTER_ANCHOR
        + "}\n\n"
        + "void FragmentPaintPropertyTreeBuilder::UpdateFilter() {\n"
        + "    if (needs) {\n"
        + "      Update();\n"
        + INTEGRATE.BLINK_PAINT_PROPERTY_NO_FILTER_ANCHOR
        + "}\n\n"
        + "void FragmentPaintPropertyTreeBuilder::PopulateBackdropFilterIfNeeded() {\n"
        + INTEGRATE.BLINK_PAINT_PROPERTY_BACKDROP_ANCHOR
        + "}\n\n}  // namespace\n}  // namespace blink\n"
    )

    def test_imposes_the_compositor_values_on_the_paint_properties_once(self):
        source = self.patch_source_twice(
            "paint_property_tree_builder.cc",
            self.PAINT_PROPERTY_SOURCE,
            INTEGRATE.patch_blink_paint_property_tree_builder,
        )
        for include in INTEGRATE.BLINK_PAINT_PROPERTY_TREE_BUILDER_INCLUDES:
            self.assertEqual(1, source.count(include + "\n"))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_PAINT_PROPERTY_HELPERS))
        # The helpers come before their first use.
        self.assertLess(
            source.index("static void RecorderImposeFilters("),
            source.index("static bool NeedsIndividualTransform("),
        )
        for hook in (
            INTEGRATE.BLINK_PAINT_PROPERTY_TRANSFORM_HOOK,
            INTEGRATE.BLINK_PAINT_PROPERTY_NO_TRANSFORM_HOOK,
            INTEGRATE.BLINK_PAINT_PROPERTY_FILTER_HOOK,
            INTEGRATE.BLINK_PAINT_PROPERTY_NO_FILTER_HOOK,
            INTEGRATE.BLINK_PAINT_PROPERTY_BACKDROP_HOOK,
        ):
            self.assertEqual(1, source.count(hook))
        helpers = INTEGRATE.BLINK_PAINT_PROPERTY_HELPERS
        # Values are read only while the recreation holds time, from the
        # element's own attribute, never a user agent shadow copy's.
        self.assertIn("if (!a11y_recorder::RecreationHoldsTime() || object.IsAnonymous()) {", helpers)
        self.assertIn("recorder_element->IsInUserAgentShadowRoot()", helpers)
        self.assertIn('AtomicString("data-a11y-recorded-compositor")', helpers)
        # A filter is replaced only when its operations match, and what the
        # record does not hold is kept from Blink's own operation.
        self.assertIn("recorder_current.size() != recorder_recorded->size()", helpers)
        self.assertIn("recorder_own.blur_tile_mode()", helpers)
        self.assertIn("operations.AppendReferenceFilter(recorder_own.image_filter());", helpers)
        self.assertLess(
            helpers.index("RecorderReportCompositorNotImposed(\n          object, recorder_property,\n          \"an operation"),
            helpers.index("operations.ReleaseCcFilterOperations();"),
        )
        # The transform's matrix is replaced and its origin kept.
        transform = INTEGRATE.BLINK_PAINT_PROPERTY_TRANSFORM_HOOK
        self.assertIn("state.transform_and_origin.matrix = gfx::Transform::RowMajor(", transform)
        self.assertNotIn("transform_and_origin.origin", transform)
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(1, signatures["RecreationCompositorValuesOf"])
        self.assertEqual(
            [], INTEGRATE.describe_signature_mismatches("patched", source, signatures)
        )

    def test_each_paint_image_made_from_an_image_is_recorded_once(self):
        source = self.patch_source_twice(
            "bitmap_image.cc",
            INTEGRATE.BLINK_BITMAP_IMAGE_OWN_INCLUDE
            + "\n\nPaintImage BitmapImage::PaintImageForCurrentFrameWithInfo() {\n"
            + INTEGRATE.BLINK_BITMAP_IMAGE_PAINT_IMAGE_ANCHOR
            + "  new_frame.GetSwSkImage();\n  return new_frame;\n}\n",
            INTEGRATE.patch_blink_bitmap_image,
        )
        self.assertEqual(1, source.count(INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n"))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_BITMAP_IMAGE_PAINT_IMAGE_HOOK))
        # The paint image is recorded once it is made, and only when made.
        self.assertLess(
            source.index("CreatePaintImage(paint_id"),
            source.index("a11y_recorder::RecordBlinkImagePaintImage("),
        )
        self.assertIn("if (new_frame && a11y_recorder::GetProcessRecorderClient())", source)
        self.assertIn("recorder_facts.image_id = paint_image_id();", source)
        self.assertIn("if (id != kNormalCachedFrameId) {", source)
        self.assertIn("PaintImage::AnimationSyncSequence::kOwn", source)
        self.assertEqual(1, source.count('A11Y_RECORDER_HOOK_COST("hook:image-paint-image");'))
        signatures = INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "bitmap_image.cc", source, signatures
            ),
        )

    def test_adds_the_item_setters_and_the_recorded_shaping_result_once(self):
        header = self.patch_source_twice(
            "fragment_item.h",
            "class FragmentItem {\n public:\n"
            + INTEGRATE.BLINK_RECREATION_ITEM_SETTERS_ANCHOR
            + "};\n",
            INTEGRATE.patch_blink_fragment_item_header,
        )
        self.assertEqual(1, header.count(INTEGRATE.BLINK_RECREATION_ITEM_SETTERS))
        self.assertLess(
            header.index(INTEGRATE.BLINK_RECREATION_ITEM_SETTERS_ANCHOR),
            header.index(INTEGRATE.BLINK_RECREATION_ITEM_SETTERS),
        )
        declaration = self.patch_source_twice(
            "shape_result.h",
            "class ShapeResult {\n public:\n"
            + INTEGRATE.BLINK_RECREATION_SHAPE_DECLARATION_ANCHOR
            + "};\n",
            INTEGRATE.patch_blink_shape_result_header,
        )
        self.assertEqual(
            1, declaration.count(INTEGRATE.BLINK_RECREATION_SHAPE_DECLARATION)
        )
        definition = self.patch_source_twice(
            "shape_result.cc",
            "namespace blink {\n"
            + INTEGRATE.BLINK_RECREATION_SHAPE_DEFINITION_ANCHOR
            + "}\n",
            INTEGRATE.patch_blink_shape_result,
        )
        self.assertEqual(
            1, definition.count(INTEGRATE.BLINK_RECREATION_SHAPE_DEFINITION)
        )
        self.assertIn(
            "run_glyphs[i] = {glyphs[i].glyph, glyphs[i].character_index,",
            INTEGRATE.BLINK_RECREATION_SHAPE_DEFINITION,
        )
        self.assertIn(
            "start_index, num_glyphs, num_characters",
            INTEGRATE.BLINK_RECREATION_SHAPE_DEFINITION,
        )

    def test_upgrades_the_feasibility_hooks_to_stage_3(self):
        box = self.patch_source_twice(
            "box_fragment_builder.cc",
            self.BOX_FRAGMENT_BUILDER_SOURCE.replace(
                INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER_ANCHOR,
                INTEGRATE.LEGACY_FEASIBILITY_FRAGMENT_HELPER
                + INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER_ANCHOR,
            ).replace(
                INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
                INTEGRATE.LEGACY_FEASIBILITY_FRAGMENT_HOOK
                + INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
            ),
            INTEGRATE.patch_blink_box_fragment_builder,
        )
        self.assertNotIn(INTEGRATE.LEGACY_FEASIBILITY_FRAGMENT_HELPER, box)
        self.assertNotIn(INTEGRATE.LEGACY_FEASIBILITY_FRAGMENT_HOOK, box)
        self.assertEqual(1, box.count(INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER))
        self.assertEqual(1, box.count(INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK))
        items = self.patch_source_twice(
            "fragment_items_builder.cc",
            '#include "third_party/blink/renderer/core/layout/inline/'
            'fragment_items_builder.h"\n'
            + INTEGRATE.LEGACY_FEASIBILITY_ITEMS_HELPER
            + INTEGRATE.BLINK_RECREATION_ITEMS_HELPER_ANCHOR
            + INTEGRATE.LEGACY_FEASIBILITY_ITEMS_HOOK
            + INTEGRATE.BLINK_RECREATION_ITEMS_ANCHOR
            + "LayoutUnit offset, bool b) {}\n",
            INTEGRATE.patch_blink_fragment_items_builder,
        )
        self.assertNotIn("RecorderRecordedEntries(", items)
        self.assertEqual(1, items.count(INTEGRATE.BLINK_RECREATION_ITEMS_HELPER))
        self.assertEqual(1, items.count(INTEGRATE.BLINK_RECREATION_ITEMS_HOOK))
        declaration = self.patch_source_twice(
            "shape_result.h",
            "class ShapeResult {\n public:\n"
            + INTEGRATE.LEGACY_FEASIBILITY_SHAPE_DECLARATION
            + INTEGRATE.BLINK_RECREATION_SHAPE_DECLARATION_ANCHOR
            + "};\n",
            INTEGRATE.patch_blink_shape_result_header,
        )
        self.assertNotIn("UChar32 code_point;", declaration)
        self.assertEqual(
            1, declaration.count(INTEGRATE.BLINK_RECREATION_SHAPE_DECLARATION)
        )
        definition = self.patch_source_twice(
            "shape_result.cc",
            "namespace blink {\n"
            + INTEGRATE.LEGACY_FEASIBILITY_SHAPE_DEFINITION
            + INTEGRATE.BLINK_RECREATION_SHAPE_DEFINITION_ANCHOR
            + "}\n",
            INTEGRATE.patch_blink_shape_result,
        )
        self.assertNotIn("code_point", definition)
        self.assertEqual(
            1, definition.count(INTEGRATE.BLINK_RECREATION_SHAPE_DEFINITION)
        )

    def test_upgrades_the_box_hook_that_did_not_compile(self):
        self.assertNotEqual(
            INTEGRATE.INTERMEDIATE_BLINK_RECREATION_FRAGMENT_HOOK,
            INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK,
        )
        self.assertIn(
            "const auto* recorder_node = node_.GetDOMNode();",
            INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK,
        )
        box = self.patch_source_twice(
            "box_fragment_builder.cc",
            self.BOX_FRAGMENT_BUILDER_SOURCE.replace(
                INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER_ANCHOR,
                INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER
                + INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER_ANCHOR,
            ).replace(
                INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
                INTEGRATE.INTERMEDIATE_BLINK_RECREATION_FRAGMENT_HOOK
                + INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
            ),
            INTEGRATE.patch_blink_box_fragment_builder,
        )
        self.assertNotIn(
            "    const Node* recorder_node = node_.GetDOMNode();\n", box
        )
        self.assertEqual(1, box.count(INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK))

    def test_upgrades_the_stage_3_box_hook_to_children_matched_by_node(self):
        self.assertNotIn(
            INTEGRATE.STAGE_3_BLINK_RECREATION_FRAGMENT_HELPER,
            INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER,
        )
        self.assertNotIn(
            INTEGRATE.STAGE_3_BLINK_RECREATION_FRAGMENT_HOOK,
            INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK,
        )
        box = self.patch_source_twice(
            "box_fragment_builder.cc",
            self.BOX_FRAGMENT_BUILDER_SOURCE.replace(
                INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER_ANCHOR,
                INTEGRATE.STAGE_3_BLINK_RECREATION_FRAGMENT_HELPER
                + INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER_ANCHOR,
            ).replace(
                INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
                INTEGRATE.STAGE_3_BLINK_RECREATION_FRAGMENT_HOOK
                + INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
            ),
            INTEGRATE.patch_blink_box_fragment_builder,
        )
        self.assertNotIn(
            "children from its recorded fragment, so its children keep", box
        )
        self.assertEqual(1, box.count(INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER))
        self.assertEqual(1, box.count(INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK))

    def test_upgrades_the_node_id_helper_that_did_not_compile(self):
        self.assertNotEqual(
            INTEGRATE.INTERMEDIATE_BLINK_RECREATION_FRAGMENT_HELPER,
            INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER,
        )
        self.assertIn(
            "recorder_text.starts_with(kRecorderPrefix)",
            INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER,
        )
        box = self.patch_source_twice(
            "box_fragment_builder.cc",
            self.BOX_FRAGMENT_BUILDER_SOURCE.replace(
                INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER_ANCHOR,
                INTEGRATE.INTERMEDIATE_BLINK_RECREATION_FRAGMENT_HELPER
                + INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER_ANCHOR,
            ).replace(
                INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
                INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK
                + INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
            ),
            INTEGRATE.patch_blink_box_fragment_builder,
        )
        self.assertNotIn(".GetString().StartsWith(", box)
        self.assertEqual(1, box.count(INTEGRATE.BLINK_RECREATION_FRAGMENT_HELPER))
        self.assertEqual(1, box.count(INTEGRATE.BLINK_RECREATION_FRAGMENT_HOOK))

    def test_refuses_a_feasibility_hook_it_cannot_upgrade(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "box_fragment_builder.cc"
            path.write_text(
                self.BOX_FRAGMENT_BUILDER_SOURCE.replace(
                    INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
                    '  // edited "data-a11y-recorded-fragment"\n'
                    + INTEGRATE.BLINK_RECREATION_FRAGMENT_ANCHOR,
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(RuntimeError, "not upgraded to stage 3"):
                INTEGRATE.patch_blink_box_fragment_builder(path)

    def test_upgrades_the_items_hook_that_did_not_compile(self):
        self.assertNotEqual(
            INTEGRATE.INTERMEDIATE_BLINK_RECREATION_ITEMS_HOOK,
            INTEGRATE.LEGACY_FEASIBILITY_ITEMS_HOOK,
        )
        source = (
            '#include "third_party/blink/renderer/core/layout/inline/'
            'fragment_items_builder.h"\n'
            + INTEGRATE.LEGACY_FEASIBILITY_ITEMS_HELPER
            + INTEGRATE.BLINK_RECREATION_ITEMS_HELPER_ANCHOR
            + INTEGRATE.INTERMEDIATE_BLINK_RECREATION_ITEMS_HOOK
            + INTEGRATE.BLINK_RECREATION_ITEMS_ANCHOR
            + "LayoutUnit offset, bool b) {}\n"
        )
        patched = self.patch_source_twice(
            "fragment_items_builder.cc",
            source,
            INTEGRATE.patch_blink_fragment_items_builder,
        )
        self.assertNotIn(
            INTEGRATE.INTERMEDIATE_BLINK_RECREATION_ITEMS_HOOK, patched
        )
        self.assertEqual(1, patched.count(INTEGRATE.BLINK_RECREATION_ITEMS_HOOK))


class ScriptSourceIntegrationTests(unittest.TestCase):
    """Slice 4h (protocol 0.54): the page's script source."""

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def signatures(self):
        return {
            **INTEGRATE.parse_bridge_signatures(
                (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
                .read_text(encoding="utf-8")
            ),
            **INTEGRATE.V8_SCRIPT_HOOK_ENTRY_POINTS,
        }

    def test_v8_gives_each_instantiated_script_to_the_hook_once(self):
        source = self.patch_twice(
            "debug.cc",
            INTEGRATE.V8_DEBUG_OWN_INCLUDE
            + "\n\nnamespace v8 {\nnamespace internal {\n\n"
            + INTEGRATE.V8_DEBUG_REPORT_ANCHOR
            + "  ProcessCompileEvent(true, script);\n}\n\n"
            + INTEGRATE.V8_DEBUG_AFTER_COMPILE_ANCHOR
            + "\n"
            + INTEGRATE.V8_DEBUG_END_ANCHOR,
            INTEGRATE.patch_v8_debug,
        )
        for include in INTEGRATE.V8_DEBUG_INCLUDES:
            self.assertEqual(1, source.count(include + "\n"))
        self.assertEqual(1, source.count(INTEGRATE.V8_DEBUG_REPORT))
        self.assertEqual(1, source.count(INTEGRATE.V8_DEBUG_AFTER_COMPILE_HOOK))
        # The report is defined before its first use and inside V8's
        # namespaces; the hook storage follows them.
        self.assertLess(
            source.index("void A11yRecorderReportScript("),
            source.index("A11yRecorderReportScript(isolate_, script, false);"),
        )
        self.assertTrue(source.endswith(INTEGRATE.V8_DEBUG_END))
        report = INTEGRATE.V8_DEBUG_REPORT
        # Only normal, non-temporary scripts with string source.
        self.assertIn("script->type() != Script::Type::kNormal", report)
        self.assertIn("Script::kTemporaryScriptId", report)
        self.assertIn("if (!IsString(*source)) {", report)
        self.assertIn("facts.is_module = script->origin_options().IsModule();", report)
        self.assertEqual(
            [], INTEGRATE.describe_signature_mismatches("patched", source, self.signatures())
        )

    def test_a_script_that_fails_to_compile_is_given_to_the_hook(self):
        source = self.patch_twice(
            "pending-compilation-error-handler.cc",
            INTEGRATE.V8_COMPILE_ERROR_OWN_INCLUDE
            + "\n\nnamespace v8 {\nnamespace internal {\n\n"
            + INTEGRATE.V8_COMPILE_ERROR_DECLARATION_ANCHOR
            + "    Isolate* isolate, Handle<Script> script) const {\n"
            + INTEGRATE.V8_COMPILE_ERROR_ANCHOR
            + "}\n\n}  // namespace internal\n}  // namespace v8\n",
            INTEGRATE.patch_v8_compile_error,
        )
        self.assertEqual(1, source.count(INTEGRATE.V8_COMPILE_ERROR_DECLARATION))
        self.assertEqual(1, source.count(INTEGRATE.V8_COMPILE_ERROR_HOOK))
        self.assertIn("A11yRecorderReportScript(isolate, script, true);", source)

    def test_blink_records_main_thread_scripts_outside_devtools_commands(self):
        source = self.patch_twice(
            "v8_initializer.cc",
            INTEGRATE.BLINK_V8_INITIALIZER_OWN_INCLUDE
            + "\n\nnamespace blink {\n\n"
            + INTEGRATE.BLINK_V8_INITIALIZER_HELPERS_ANCHOR
            + INTEGRATE.BLINK_V8_INITIALIZER_INSTALL_ANCHOR
            + "}\n\n}  // namespace blink\n",
            INTEGRATE.patch_blink_v8_initializer,
        )
        for include in INTEGRATE.BLINK_V8_INITIALIZER_INCLUDES:
            self.assertEqual(1, source.count(include + "\n"))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_V8_INITIALIZER_HELPERS))
        self.assertEqual(1, source.count(INTEGRATE.BLINK_V8_INITIALIZER_INSTALL_HOOK))
        helpers = INTEGRATE.BLINK_V8_INITIALIZER_HELPERS
        self.assertIn("a11y_recorder::InDevToolsCommand()", helpers)
        self.assertIn("!IsMainThread()", helpers)
        self.assertIn("WorldType::kInspectorIsolated ||", helpers)
        # Once per script ID, before the text is converted.
        self.assertLess(
            helpers.index("a11y_recorder::ClaimScriptParsed(script.script_id)"),
            helpers.index("RecorderScriptText(isolate, script.source)"),
        )
        self.assertIn("v8::String::WriteFlags::kReplaceInvalidUtf8", helpers)
        self.assertIn('A11Y_RECORDER_HOOK_COST("hook:script-parsed");', helpers)
        self.assertEqual(
            [], INTEGRATE.describe_signature_mismatches("patched", source, self.signatures())
        )

    def test_devtools_commands_are_bracketed_on_the_main_thread(self):
        source = self.patch_twice(
            "devtools_session.cc",
            INTEGRATE.BLINK_DEVTOOLS_SESSION_OWN_INCLUDE
            + "\n\nnamespace blink {\n\nvoid Detach() {\n"
            + "  agent_->client_->DebuggerTaskStarted();\n"
            + "  agent_->client_->DebuggerTaskFinished();\n}\n\n"
            + "void Dispatch() {\n"
            + INTEGRATE.BLINK_DEVTOOLS_SESSION_START_ANCHOR
            + "          method)) {\n  } else {\n"
            + INTEGRATE.BLINK_DEVTOOLS_SESSION_FINISH_ANCHOR
            + "}\n\n}  // namespace blink\n",
            INTEGRATE.patch_blink_devtools_session,
        )
        self.assertEqual(1, source.count("a11y_recorder::EnterDevToolsCommand();"))
        self.assertEqual(1, source.count("a11y_recorder::LeaveDevToolsCommand();"))
        self.assertLess(
            source.index("EnterDevToolsCommand"), source.index("LeaveDevToolsCommand")
        )
        self.assertEqual(
            [], INTEGRATE.describe_signature_mismatches("patched", source, self.signatures())
        )

    def test_the_bridge_records_script_parsed_and_script_text(self):
        bridge = (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.cc").read_text(
            encoding="utf-8"
        )
        build = (MODULE_PATH.parent / "recorder_bridge" / "BUILD.gn").read_text(
            encoding="utf-8"
        )
        self.assertIn('"v8_script_hook.h",', build)
        self.assertIn('constexpr char kScriptChannel[] = "browser.script";', bridge)
        self.assertIn('SendBlinkEvidence(kScriptChannel, "script-parsed",', bridge)
        self.assertIn('"script-text", digest,', bridge)
        self.assertIn("std::move(facts.source), kScriptChannel);", bridge)
        for field in (
            "scriptId", "kind", "url", "sourceUrl", "sourceMapUrl", "line", "column",
            "evalFromScriptId", "compileError", "digest", "size", "textRecorded",
        ):
            with self.subTest(field=field):
                self.assertIn(f'payload.Set("{field}",', bridge)


class FrameOwnerIntegrationTests(unittest.TestCase):
    """Slice 5a (protocol 0.55): which frame each owner element holds."""

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

    def signatures(self):
        return INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def test_a_walk_names_its_frame_and_each_owner_s_frame(self):
        helper = INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER
        self.assertIn(
            "recorder_document_frame->GetDevToolsFrameToken().ToString()", helper
        )
        self.assertIn(
            "recorder_document_frame && recorder_document_frame->IsMainFrame()",
            helper,
        )
        # The owner's record follows its node record, before its children
        # are queued.
        node = helper.index("a11y_recorder::RecordBlinkDomCheckpointNode(")
        owner = helper.index("a11y_recorder::RecordBlinkDomCheckpointFrameOwner(")
        children = helper.index("recorder_pending.push_back(recorder_child);")
        self.assertLess(node, owner)
        self.assertLess(owner, children)
        self.assertIn("recorder_content_frame->IsRemoteFrame()", helper)
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "helper", helper, self.signatures()
            ),
        )

    def test_upgrades_a_dom_helper_that_names_no_frame(self):
        legacy = INTEGRATE.LEGACY_UNFRAMED_BLINK_DOM_CHECKPOINT_HELPER
        self.assertNotIn("GetDevToolsFrameToken", legacy)
        # The superseded helper calls the started record with four
        # arguments, which the bridge no longer declares.
        self.assertNotEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "legacy", legacy, self.signatures()
            ),
        )
        first = self.patch_twice(
            "document.cc",
            self.document_source(legacy),
            INTEGRATE.patch_blink_document,
        )
        self.assertNotIn(legacy, first)
        self.assertEqual(1, first.count(INTEGRATE.BLINK_DOM_CHECKPOINT_HELPER))
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "patched", first, self.signatures()
            ),
        )

    def test_an_owner_records_each_frame_it_is_given_and_its_loss(self):
        source = (
            INTEGRATE.BLINK_FRAME_OWNER_OWN_INCLUDE
            + "\n\nnamespace blink {\n\n"
            "void HTMLFrameOwnerElement::SetContentFrame(Frame& frame) {\n"
            "  content_frame_ = &frame;\n\n"
            + INTEGRATE.BLINK_FRAME_OWNER_SET_ANCHOR
            + "\nvoid HTMLFrameOwnerElement::ClearContentFrame() {\n"
            "  if (!content_frame_)\n    return;\n\n"
            + INTEGRATE.BLINK_FRAME_OWNER_CLEAR_ANCHOR
            + "}\n\n}  // namespace blink\n"
        )
        first = self.patch_twice(
            "html_frame_owner_element.cc", source, INTEGRATE.patch_blink_frame_owner
        )
        self.assertEqual(1, first.count(INTEGRATE.BLINK_BRIDGE_INCLUDE + "\n"))
        self.assertEqual(1, first.count(INTEGRATE.BLINK_FRAME_OWNER_SET_HOOK))
        self.assertEqual(1, first.count(INTEGRATE.BLINK_FRAME_OWNER_CLEAR_HOOK))
        # The set record is written once the owner holds the frame; the
        # cleared record while it still does, so the order is the frame's.
        set_record = first.index("frame.GetDevToolsFrameToken().ToString()")
        self.assertLess(first.index("content_frame_ = &frame;"), set_record)
        clear = first.index("GetDomNodeId(), std::string(), false);")
        self.assertLess(clear, first.index("  content_frame_ = nullptr;"))
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "patched", first, self.signatures()
            ),
        )

    def test_the_bridge_writes_the_frame_records(self):
        bridge = (
            MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.cc"
        ).read_text(encoding="utf-8")
        self.assertIn('"dom-checkpoint-frame-owner"', bridge)
        self.assertIn('"dom-frame-owner-changed"', bridge)
        self.assertIn('payload.Set("frameToken", base::Value());', bridge)
        self.assertIn('payload.Set("mainFrame", main_frame);', bridge)
        self.assertIn('remote ? "remote" : "local"', bridge)
        # The changed record is not a DOM transition.
        start = bridge.index("void RecordBlinkDomFrameOwnerChanged(")
        end = bridge.index("\n}\n", start)
        self.assertNotIn("CreateDomStateChangeBasePayload", bridge[start:end])


class BrowserPreferencesIntegrationTests(unittest.TestCase):
    """Protocol 0.56: the browser records of accessibility preferences."""

    def signatures(self):
        return INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def refuses(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            with self.assertRaises(RuntimeError):
                patch(path)

    def render_view_host_source(self):
        return (
            INTEGRATE.CONTENT_RENDER_VIEW_HOST_OWN_INCLUDE
            + "\n\nnamespace content {\n\n"
            + INTEGRATE.CONTENT_RENDER_VIEW_HOST_HELPER_ANCHOR
            + "    int proxy_route_id) {\n"
            "  params->web_preferences = delegate_->GetOrCreateWebPreferences(this);\n\n"
            + INTEGRATE.CONTENT_RENDER_VIEW_HOST_CREATED_ANCHOR
            + "  return true;\n}\n\n"
            "void RenderViewHostImpl::SendWebPreferencesToRenderer() {\n"
            "  if (auto& broadcast = GetAssociatedPageBroadcast()) {\n"
            + INTEGRATE.CONTENT_RENDER_VIEW_HOST_WEB_ANCHOR
            + "}\n\n"
            "void RenderViewHostImpl::SendRendererPreferencesToRenderer(\n"
            "    const blink::RendererPreferences& preferences) {\n"
            "  if (auto& broadcast = GetAssociatedPageBroadcast()) {\n"
            + INTEGRATE.CONTENT_RENDER_VIEW_HOST_RENDERER_ANCHOR
            + "}\n\n}  // namespace content\n"
        )

    def test_each_send_of_preferences_to_a_view_is_recorded(self):
        first = self.patch_twice(
            "render_view_host_impl.cc",
            self.render_view_host_source(),
            INTEGRATE.patch_content_render_view_host,
        )
        self.assertEqual(1, first.count(INTEGRATE.BRIDGE_INCLUDE + "\n"))
        self.assertEqual(1, first.count(INTEGRATE.CONTENT_RENDER_VIEW_HOST_HELPER))
        for hook in (
            INTEGRATE.CONTENT_RENDER_VIEW_HOST_CREATED_HOOK,
            INTEGRATE.CONTENT_RENDER_VIEW_HOST_WEB_HOOK,
            INTEGRATE.CONTENT_RENDER_VIEW_HOST_RENDERER_HOOK,
        ):
            self.assertEqual(1, first.count(hook))
        # The helper is defined before its first use, and the view is
        # recorded before its parameters are moved away.
        self.assertLess(
            first.index("void RecorderRecordPreferencesSent("),
            first.index("bool RenderViewHostImpl::CreateRenderView("),
        )
        self.assertLess(
            first.index('"view-created"'),
            first.index("CreateView(std::move(params));"),
        )
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "patched", first, self.signatures()
            ),
        )

    def test_records_every_listed_field_of_the_preferences(self):
        helper = INTEGRATE.CONTENT_RENDER_VIEW_HOST_HELPER
        for field in (
            "standardFontFamily", "fixedFontFamily", "serifFontFamily",
            "sansSerifFontFamily", "cursiveFontFamily", "fantasyFontFamily",
            "mathFontFamily", "defaultFontSize", "defaultFixedFontSize",
            "minimumFontSize", "minimumLogicalFontSize",
            "prefersReducedMotion", "prefersReducedTransparency",
            "invertedColors", "textTrackTextSize", "textTrackFontFamily",
            "inForcedColors", "isForcedColorsDisabled",
            "preferredRootScrollbarColorScheme", "preferredColorScheme",
            "preferredContrast", "focusRingColor",
            "hasCaretBlinkInterval", "caretBlinkIntervalMilliseconds",
            "caretBrowsingEnabled",
            "useOverlayScrollbar", "captionFontFamily", "captionFontHeight",
            "smallCaptionFontFamily", "smallCaptionFontHeight",
            "menuFontFamily", "menuFontHeight", "statusFontFamily",
            "statusFontHeight", "messageFontFamily", "messageFontHeight",
        ):
            with self.subTest(field=field):
                self.assertIn(f'"{field}"', helper)
        # Nothing is read for a browser started without the recorder.
        self.assertLess(
            helper.index("if (!a11y_recorder::IsRecorderActive())"),
            helper.index("base::DictValue fields;"),
        )

    def test_refuses_a_render_view_host_it_does_not_recognise(self):
        source = self.render_view_host_source().replace(
            "broadcast->UpdateRendererPreferences(preferences);",
            "broadcast->UpdateRendererPreferences(std::move(preferences));",
        )
        self.refuses(
            "render_view_host_impl.cc",
            source,
            INTEGRATE.patch_content_render_view_host,
        )

    def host_zoom_map_source(self):
        return (
            INTEGRATE.CONTENT_HOST_ZOOM_MAP_OWN_INCLUDE
            + "\n\nnamespace content {\n\n"
            "void HostZoomMapImpl::SetZoomLevelForHostInternal() {\n"
            + INTEGRATE.CONTENT_HOST_ZOOM_MAP_HOST_ANCHOR
            + "}\n\nvoid HostZoomMapImpl::SetZoomLevelForHostAndScheme() {\n"
            + INTEGRATE.CONTENT_HOST_ZOOM_MAP_SCHEME_ANCHOR
            + "}\n\nvoid HostZoomMapImpl::SetDefaultZoomLevelInternal() {\n"
            "  if (uses_default_zoom) {\n"
            + INTEGRATE.CONTENT_HOST_ZOOM_MAP_FOLLOWS_ANCHOR
            + "  }\n}\n\nvoid HostZoomMapImpl::SetDefaultZoomLevel(double level) {\n"
            + INTEGRATE.CONTENT_HOST_ZOOM_MAP_DEFAULT_ANCHOR
            + "}\n\nvoid HostZoomMapImpl::SetTemporaryZoomLevel() {\n"
            + INTEGRATE.CONTENT_HOST_ZOOM_MAP_TEMPORARY_ANCHOR
            + "}\n\n}  // namespace content\n"
        )

    def test_each_zoom_level_change_is_recorded_before_its_callbacks(self):
        first = self.patch_twice(
            "host_zoom_map_impl.cc",
            self.host_zoom_map_source(),
            INTEGRATE.patch_content_host_zoom_map,
        )
        self.assertEqual(1, first.count(INTEGRATE.BRIDGE_INCLUDE + "\n"))
        self.assertEqual(
            5, first.count("a11y_recorder::RecordBrowserZoomLevelChanged(")
        )
        for mode in ('"host", false', '"scheme-and-host", false',
                     '"host", true', '"temporary", false', '"default", false'):
            with self.subTest(mode=mode):
                self.assertIn(mode, first)
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "patched", first, self.signatures()
            ),
        )

    def test_refuses_a_zoom_map_it_does_not_recognise(self):
        source = self.host_zoom_map_source().replace(
            "  change.host = GetHostFromProcessFrame(rfh);\n", ""
        )
        self.refuses(
            "host_zoom_map_impl.cc", source, INTEGRATE.patch_content_host_zoom_map
        )

    def profile_source(self):
        return (
            INTEGRATE.CHROME_PROFILE_IMPL_OWN_INCLUDE
            + "\n\nnamespace {\n\nint unrelated = 0;\n\n"
            + INTEGRATE.CHROME_PROFILE_IMPL_HELPER_ANCHOR
            + "const base::FilePath& path) {\n}\n\n"
            "void ProfileImpl::DoFinalInit(CreateMode create_mode) {\n"
            "  PrefService* prefs = GetPrefs();\n"
            "  pref_change_registrar_.Init(prefs);\n"
            "  pref_change_registrar_.Add(\n"
            "      subscription_eligibility::prefs::kAiSubscriptionTier,\n"
            + INTEGRATE.CHROME_PROFILE_IMPL_WATCH_ANCHOR
            + "}\n"
        )

    def test_a_profile_records_its_listed_preferences_and_their_changes(self):
        first = self.patch_twice(
            "profile_impl.cc", self.profile_source(),
            INTEGRATE.patch_chrome_profile_impl,
        )
        self.assertEqual(1, first.count(INTEGRATE.BRIDGE_INCLUDE + "\n"))
        self.assertEqual(1, first.count(INTEGRATE.CHROME_PROFILE_IMPL_HELPER))
        self.assertEqual(1, first.count(INTEGRATE.CHROME_PROFILE_IMPL_WATCH_HOOK))
        # The watch is added once the profile's registrar is initialised.
        self.assertLess(
            first.index("pref_change_registrar_.Init(prefs);"),
            first.index("RecorderWatchBrowserPreferences(prefs,"),
        )
        for preference in (
            "webkit.webprefs.fonts.standard.Zyyy",
            "webkit.webprefs.default_font_size",
            "webkit.webprefs.minimum_font_size",
            "browser.theme.color_scheme2",
            "settings.a11y.focus_highlight",
            "settings.a11y.requested_page_colors",
            "settings.a11y.apply_page_colors_only_on_increased_contrast",
            "settings.a11y.page_colors_block_list",
            "settings.a11y.caretbrowsing.enabled",
        ):
            with self.subTest(preference=preference):
                self.assertIn(f'"{preference}"', first)
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "patched", first, self.signatures()
            ),
        )

    def test_refuses_a_profile_it_does_not_recognise(self):
        source = self.profile_source().replace(
            "  base::FilePath base_cache_path;\n", ""
        )
        self.refuses("profile_impl.cc", source, INTEGRATE.patch_chrome_profile_impl)

    def test_the_profiles_misc_target_depends_on_the_bridge(self):
        source = (
            'source_set("profiles") {\n  deps = [\n    "//base",\n  ]\n}\n\n'
            'source_set("misc") {\n  sources = [\n    "profile_impl.cc",\n'
            '    "profile_impl.h",\n  ]\n\n  deps = [\n    ":profile",\n'
            '  ]\n}\n'
        )
        first = self.patch_twice(
            "BUILD.gn", source, INTEGRATE.patch_chrome_profiles_build
        )
        self.assertEqual(1, first.count(INTEGRATE.BRIDGE_DEP))
        self.assertGreater(
            first.index(INTEGRATE.BRIDGE_DEP), first.index('source_set("misc")')
        )
        self.refuses(
            "BUILD.gn",
            source.replace('"profile_impl.cc"', '"other.cc"'),
            INTEGRATE.patch_chrome_profiles_build,
        )

    def test_the_bridge_records_only_what_changed_for_a_view(self):
        bridge = (
            MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.cc"
        ).read_text(encoding="utf-8")
        start = bridge.index("void RecordBrowserWebPreferencesSent(")
        end = bridge.index("\n}\n", start)
        body = bridge[start:end]
        self.assertIn('const bool created = point == "view-created";', body)
        self.assertIn("if (!first && changed.empty()) {", body)
        self.assertIn('"browser.preferences", "web-preferences-sent"', body)


class RecreationPreferencesIntegrationTests(unittest.TestCase):
    """Protocol 0.57 and stage 3: the color maps recorded, and the recorded
    preferences given to a recreated page."""

    def signatures(self):
        return INTEGRATE.parse_bridge_signatures(
            (MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.h")
            .read_text(encoding="utf-8")
        )

    def patch_twice(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            patch(path)
            first = path.read_text(encoding="utf-8")
            patch(path)
            self.assertEqual(first, path.read_text(encoding="utf-8"))
            return first

    def refuses(self, name, source, patch):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / name
            path.write_text(source, encoding="utf-8")
            with self.assertRaises(RuntimeError):
                patch(path)

    def no_mismatches(self, text):
        self.assertEqual(
            [],
            INTEGRATE.describe_signature_mismatches(
                "patched", text, self.signatures()
            ),
        )

    def render_view_host_source(self):
        return (
            INTEGRATE.CONTENT_RENDER_VIEW_HOST_OWN_INCLUDE
            + "\n\nnamespace content {\n\n"
            + INTEGRATE.CONTENT_RENDER_VIEW_HOST_HELPER_ANCHOR
            + "    int proxy_route_id) {\n"
            + INTEGRATE.CONTENT_RENDER_VIEW_HOST_COLOR_CREATED_ANCHOR
            + INTEGRATE.CONTENT_RENDER_VIEW_HOST_CREATED_ANCHOR
            + "  return true;\n}\n\n}  // namespace content\n"
        )

    def test_the_color_maps_a_view_is_created_with_are_recorded(self):
        first = self.patch_twice(
            "render_view_host_impl.cc",
            self.render_view_host_source(),
            INTEGRATE.patch_content_render_view_host_color_maps,
        )
        self.assertEqual(
            1, first.count(INTEGRATE.CONTENT_RENDER_VIEW_HOST_COLOR_HELPER)
        )
        self.assertEqual(
            1, first.count(INTEGRATE.CONTENT_RENDER_VIEW_HOST_COLOR_CREATED_HOOK)
        )
        for include in INTEGRATE.CONTENT_RENDER_VIEW_HOST_COLOR_INCLUDES:
            self.assertEqual(1, first.count(include + "\n"))
        # The helper is defined before its use, and the maps are recorded
        # once they are set on the parameters, before these are moved away.
        self.assertLess(
            first.index("void RecorderRecordColorMapsSent(RenderViewHostImpl* view,"),
            first.index("bool RenderViewHostImpl::CreateRenderView("),
        )
        self.assertLess(
            first.index('RecorderRecordColorMapsSent(this, "view-created",'),
            first.index("CreateView(std::move(params));"),
        )
        # Nothing is read for a browser started without the recorder.
        helper = INTEGRATE.CONTENT_RENDER_VIEW_HOST_COLOR_HELPER
        self.assertLess(
            helper.index("if (!a11y_recorder::IsRecorderActive() || !view)"),
            helper.index("base::DictValue value;"),
        )
        for name in ("kLightColorMap", "kDarkColorMap", "kForcedColorsColorMap"):
            self.assertIn(f"a11y_recorder::{name}", helper)
        self.no_mismatches(first)

    def test_refuses_a_render_view_host_without_the_color_maps_line(self):
        source = self.render_view_host_source().replace(
            "delegate_->GetColorProviderColorMaps();",
            "GetColorProviderColorMaps();",
        )
        self.refuses(
            "render_view_host_impl.cc",
            source,
            INTEGRATE.patch_content_render_view_host_color_maps,
        )

    def web_contents_source(self):
        return (
            "namespace content {\n\n"
            + INTEGRATE.CONTENT_WEB_CONTENTS_COLOR_DECLARATION_ANCHOR
            + "  if (blink::ColorProviderColorMaps color_maps = GetColorProviderColorMaps();\n"
            "      color_maps_ != color_maps) {\n"
            "    color_maps_.swap(color_maps);\n"
            "    ExecutePageBroadcastMethodForAllPages([this](RenderViewHostImpl* rvh) {\n"
            + INTEGRATE.CONTENT_WEB_CONTENTS_COLOR_ANCHOR
            + "    });\n  }\n}\n\n}  // namespace content\n"
        )

    def test_each_change_of_the_color_maps_sent_is_recorded(self):
        first = self.patch_twice(
            "web_contents_impl.cc",
            self.web_contents_source(),
            INTEGRATE.patch_content_web_contents_color_maps,
        )
        self.assertEqual(
            1, first.count(INTEGRATE.CONTENT_WEB_CONTENTS_COLOR_DECLARATION)
        )
        self.assertEqual(1, first.count(INTEGRATE.CONTENT_WEB_CONTENTS_COLOR_HOOK))
        # Declared before use, and recorded after each page is sent them.
        self.assertLess(
            first.index("void RecorderRecordColorMapsSent("),
            first.index("void WebContentsImpl::HandleColorRelatedStateChanges()"),
        )
        self.assertLess(
            first.index("broadcast->UpdateColorProviders(color_maps_);"),
            first.index('RecorderRecordColorMapsSent(rvh, "color-providers"'),
        )
        # The declaration matches the definition.
        self.assertIn(
            INTEGRATE.CONTENT_WEB_CONTENTS_COLOR_DECLARATION.split("\n", 1)[1]
            .strip(),
            INTEGRATE.CONTENT_RENDER_VIEW_HOST_COLOR_HELPER,
        )
        self.no_mismatches(first)

    def test_refuses_web_contents_it_does_not_recognise(self):
        source = self.web_contents_source().replace(
            "broadcast->UpdateColorProviders(color_maps_);",
            "broadcast->UpdateColorProviders(std::move(color_maps));",
        )
        self.refuses(
            "web_contents_impl.cc",
            source,
            INTEGRATE.patch_content_web_contents_color_maps,
        )

    def web_view_source(self):
        return (
            INTEGRATE.BLINK_WEB_VIEW_OWN_INCLUDE
            + "\n\nnamespace blink {\n\n"
            + INTEGRATE.BLINK_WEB_VIEW_PREFERENCES_HELPER_ANCHOR
            + "    const RendererPreferences& preferences) {\n"
            "  std::string old_accept_languages = renderer_preferences_.accept_languages;\n"
            + INTEGRATE.BLINK_WEB_VIEW_RENDERER_PREFERENCES_ANCHOR
            + "}\n\n"
            + INTEGRATE.BLINK_WEB_VIEW_WEB_PREFERENCES_ANCHOR
            + "  ApplyWebPreferences(web_preferences_, this);\n}\n\n"
            "}  // namespace blink\n"
        )

    def test_a_recreated_page_takes_the_recorded_preferences(self):
        first = self.patch_twice(
            "web_view_impl.cc",
            self.web_view_source(),
            INTEGRATE.patch_blink_web_view_preferences,
        )
        self.assertEqual(
            1, first.count(INTEGRATE.BLINK_WEB_VIEW_PREFERENCES_HELPER)
        )
        for hook in (
            INTEGRATE.BLINK_WEB_VIEW_WEB_PREFERENCES_HOOK,
            INTEGRATE.BLINK_WEB_VIEW_RENDERER_PREFERENCES_HOOK,
        ):
            self.assertEqual(1, first.count(hook))
        for include in INTEGRATE.BLINK_WEB_VIEW_PREFERENCES_INCLUDES:
            self.assertEqual(1, first.count(include + "\n"))
        # The page is given the preferences with the recorded values.
        self.assertIn(
            "GetPage()->SetRendererPreferences(renderer_preferences_);", first
        )
        self.assertNotIn("GetPage()->SetRendererPreferences(preferences);", first)
        self.assertLess(
            first.index("RecorderOverrideWebPreferences(MainFrameImpl(), web_preferences_);"),
            first.index("ApplyWebPreferences(web_preferences_, this);"),
        )
        helper = INTEGRATE.BLINK_WEB_VIEW_PREFERENCES_HELPER
        # Only in the recreation mode, and only for the outermost main frame.
        self.assertIn("!a11y_recorder::IsRecreationMode() || !frame ||", helper)
        self.assertIn("!frame->IsOutermostMainFrame()", helper)
        self.assertIn('"data-a11y-recorded-preferences"', helper)
        for field in (
            "standardFontFamily", "fixedFontFamily", "serifFontFamily",
            "sansSerifFontFamily", "cursiveFontFamily", "fantasyFontFamily",
            "mathFontFamily", "defaultFontSize", "defaultFixedFontSize",
            "minimumFontSize", "minimumLogicalFontSize",
            "prefersReducedMotion", "prefersReducedTransparency",
            "invertedColors", "textTrackTextSize", "textTrackFontFamily",
            "inForcedColors", "isForcedColorsDisabled",
            "preferredRootScrollbarColorScheme", "preferredColorScheme",
            "preferredContrast", "focusRingColor",
            "hasCaretBlinkInterval", "caretBlinkIntervalMilliseconds",
            "caretBrowsingEnabled",
            "useOverlayScrollbar", "captionFontFamily", "captionFontHeight",
            "smallCaptionFontFamily", "smallCaptionFontHeight",
            "menuFontFamily", "menuFontHeight", "statusFontFamily",
            "statusFontHeight", "messageFontFamily", "messageFontHeight",
        ):
            with self.subTest(field=field):
                self.assertIn(f'"{field}"', helper)
                # Every field the recording names is one the recorder sends.
                self.assertIn(f'"{field}"', INTEGRATE.CONTENT_RENDER_VIEW_HOST_HELPER)
        self.no_mismatches(first)

    def test_refuses_a_web_view_it_does_not_recognise(self):
        source = self.web_view_source().replace(
            "GetPage()->SetRendererPreferences(preferences);",
            "GetPage()->SetRendererPreferences(std::move(preferences));",
        )
        self.refuses(
            "web_view_impl.cc", source, INTEGRATE.patch_blink_web_view_preferences
        )

    def test_a_recreated_page_takes_the_recorded_color_maps(self):
        source = (
            "namespace blink {\n\n"
            + INTEGRATE.BLINK_PAGE_COLOR_MAPS_ANCHOR
            + "  CHECK(!color_provider_colors.IsEmpty());\n"
            "  SetColorProviderColorMaps(color_provider_colors);\n"
            "  return true;\n}\n\n}  // namespace blink\n"
        )
        first = self.patch_twice("page.cc", source, INTEGRATE.patch_blink_page_color_maps)
        self.assertEqual(1, first.count(INTEGRATE.BLINK_PAGE_COLOR_MAPS_HOOK))
        # The rest of the function reads the recorded maps by the old name.
        self.assertLess(
            first.index("const ColorProviderColorMaps color_provider_colors ="),
            first.index("CHECK(!color_provider_colors.IsEmpty());"),
        )
        self.refuses(
            "page.cc",
            source.replace("bool Page::UpdateColorProviders(", "void Page::UpdateColorProviders("),
            INTEGRATE.patch_blink_page_color_maps,
        )

    def test_a_recreated_page_takes_the_recorded_zoom_level(self):
        source = (
            "namespace blink {\n\n"
            + INTEGRATE.BLINK_FRAME_WIDGET_ZOOM_DECLARATION_ANCHOR
            + "  SetZoomInternal(zoom_level, css_zoom_factor_);\n}\n\n"
            "void WebFrameWidgetImpl::SetZoomInternal(double zoom_level,\n"
            "                                         double css_zoom_factor) {\n"
            + INTEGRATE.BLINK_FRAME_WIDGET_ZOOM_ANCHOR
            + "  bool zoom_changed = zoom_level != zoom_level_;\n}\n\n"
            "}  // namespace blink\n"
        )
        first = self.patch_twice(
            "web_frame_widget_impl.cc", source, INTEGRATE.patch_blink_frame_widget_zoom
        )
        self.assertEqual(1, first.count(INTEGRATE.BLINK_FRAME_WIDGET_ZOOM_HOOK))
        self.assertEqual(1, first.count(INTEGRATE.BLINK_FRAME_WIDGET_ZOOM_DECLARATION))
        # The recorded level wins over the one sent and the testing one.
        self.assertLess(
            first.index("zoom_level = zoom_level_for_testing_;"),
            first.index("zoom_level = *recorder_zoom;"),
        )
        self.assertLess(
            first.index("zoom_level = *recorder_zoom;"),
            first.index("bool zoom_changed"),
        )
        self.refuses(
            "web_frame_widget_impl.cc",
            source.replace("zoom_level_for_testing_ != -INFINITY", "HasZoomLevelForTesting()"),
            INTEGRATE.patch_blink_frame_widget_zoom,
        )

    def test_the_recorded_preferences_are_applied_as_the_root_is_parsed(self):
        source = (
            "namespace blink {\n\n"
            + INTEGRATE.BLINK_HTML_ELEMENT_PREFERENCES_ANCHOR
            + "\n  GetDocument().Parser()->DocumentElementAvailable();\n}\n\n"
            "}  // namespace blink\n"
        )
        first = self.patch_twice(
            "html_html_element.cc", source, INTEGRATE.patch_blink_html_element_preferences
        )
        self.assertEqual(1, first.count(INTEGRATE.BLINK_HTML_ELEMENT_PREFERENCES_HOOK))
        self.assertLess(
            first.index("RecorderApplyRecordedPreferences(GetDocument());"),
            first.index("DocumentElementAvailable();"),
        )
        self.refuses(
            "html_html_element.cc",
            source.replace("if (!GetDocument().Parser())\n    return;", "if (!GetDocument().Parser()) {\n    return;\n  }"),
            INTEGRATE.patch_blink_html_element_preferences,
        )

    def test_the_blink_declarations_match_their_definitions(self):
        helper = INTEGRATE.BLINK_WEB_VIEW_PREFERENCES_HELPER
        self.assertIn(
            "std::optional<double> RecorderRecordedZoomLevel(WebLocalFrameImpl* frame);",
            INTEGRATE.BLINK_FRAME_WIDGET_ZOOM_DECLARATION,
        )
        self.assertIn(
            "std::optional<double> RecorderRecordedZoomLevel(WebLocalFrameImpl* frame) {",
            helper,
        )
        self.assertIn("void RecorderApplyRecordedPreferences(Document& document);", helper)
        self.assertIn(
            "void RecorderApplyRecordedPreferences(Document& document);",
            INTEGRATE.BLINK_HTML_ELEMENT_PREFERENCES_HOOK,
        )
        declaration = (
            "ColorProviderColorMaps RecorderRecordedColorMaps(\n"
            "    LocalFrame* main_frame,\n"
            "    const ColorProviderColorMaps& sent);"
        )
        self.assertIn(declaration, helper)
        self.assertIn(declaration, INTEGRATE.BLINK_PAGE_COLOR_MAPS_HOOK)

    def test_main_applies_the_stage_3_hooks(self):
        source = Path(INTEGRATE.__file__).read_text(encoding="utf-8")
        main = source[source.index("def main() -> int:"):]
        for patch in (
            "patch_content_render_view_host_color_maps(",
            "patch_content_web_contents_color_maps(",
            "patch_blink_web_view_preferences(",
            "patch_blink_page_color_maps(",
            "patch_blink_frame_widget_zoom(",
            "patch_blink_html_element_preferences(",
        ):
            with self.subTest(patch=patch):
                self.assertEqual(1, main.count(patch))
        # The color maps of a view are recorded after its 0.56 hooks apply.
        self.assertLess(
            main.index("patch_content_render_view_host("),
            main.index("patch_content_render_view_host_color_maps("),
        )

    def test_the_bridge_records_only_the_maps_that_changed_for_a_view(self):
        bridge = (
            MODULE_PATH.parent / "recorder_bridge" / "browser_bridge.cc"
        ).read_text(encoding="utf-8")
        start = bridge.index("void RecordBrowserColorMapsSent(")
        end = bridge.index("\n}\n", start)
        body = bridge[start:end]
        self.assertIn('const bool created = point == "view-created";', body)
        self.assertIn('{"view-created", "color-providers"}', body)
        self.assertIn("if (!first && changed.empty()) {", body)
        self.assertIn('"browser.preferences", "color-maps-sent"', body)
        self.assertIn('payload.Set("maps", std::move(changed));', body)
        self.assertIn("RecreationPreferences RecreationPreferencesOf(", bridge)

    def test_the_recreation_preferences_pass_their_native_tests(self):
        import shutil
        import subprocess

        compiler = shutil.which("g++") or shutil.which("clang++")
        if compiler is None:
            self.skipTest("no C++ compiler is available")
        bridge = MODULE_PATH.parent / "recorder_bridge"
        build = (bridge / "BUILD.gn").read_text(encoding="utf-8")
        self.assertIn('"recreation_preferences.h",', build)
        header = (bridge / "browser_bridge.h").read_text(encoding="utf-8")
        self.assertIn(
            '#include "chromium/recorder_bridge/recreation_preferences.h"', header
        )
        with tempfile.TemporaryDirectory() as directory:
            binary = Path(directory) / "recreation_preferences_test"
            subprocess.run(
                [
                    compiler,
                    "-std=c++20",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    f"-I{MODULE_PATH.parent.parent}",
                    str(bridge / "recreation_preferences_test.cc"),
                    "-o",
                    str(binary),
                ],
                check=True,
            )
            subprocess.run([str(binary)], check=True)
