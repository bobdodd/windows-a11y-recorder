import importlib.util
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
            self.assertIn(
                "NodeTraversal::InclusiveDescendantsOf(*this)",
                first,
            )
            self.assertIn("recorder_node.parentNode()", first)
            self.assertIn("Token().ToString()", first)
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
            self.assertEqual(2, first.count('"post-mutation"'))
            self.assertEqual(1, first.count("BeginBlinkDomCheckpoint"))
            self.assertEqual(1, first.count("RecordBlinkDomCheckpointNode("))
            self.assertEqual(1, first.count("CompleteBlinkDomCheckpoint"))
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
            self.assertIn("recorder_document->Token().ToString()", first)
            self.assertIn(
                "NodeTraversal::InclusiveDescendantsOf(*recorder_document)",
                first,
            )
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
            self.assertEqual(1, mutation_first.count("BeginBlinkDomCheckpoint"))
            self.assertEqual(
                1,
                mutation_first.count("RecordBlinkDomCheckpointNode("),
            )
            self.assertEqual(
                1,
                mutation_first.count("CompleteBlinkDomCheckpoint"),
            )
            self.assertIn(
                "recorder_document->Token().ToString()",
                mutation_first,
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
            self.assertEqual(1, mutation_first.count("BeginBlinkDomCheckpoint"))
            self.assertEqual(
                1,
                mutation_first.count("RecordBlinkDomCheckpointNode("),
            )
            self.assertEqual(
                1,
                mutation_first.count("CompleteBlinkDomCheckpoint"),
            )
            self.assertIn(
                "recorder_document->Token().ToString()",
                mutation_first,
            )

            for patched in (document_first, mutation_first):
                self.assertEqual(
                    1,
                    patched.count("RecordBlinkDomCheckpointNodeAttribute("),
                )
                self.assertIn(
                    "kRecorderMaximumDomAttributesPerNode = 64", patched
                )
                self.assertIn("kRecorderMaximumDomValueLength = 4096", patched)
                self.assertIn("recorder_attributes_truncated", patched)
                self.assertIn(
                    '#include "third_party/blink/renderer/core/dom/attribute.h"',
                    patched,
                )
                self.assertIn(
                    '#include "third_party/blink/renderer/core/dom/element.h"',
                    patched,
                )


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
        self.assertIn("mutation_observer.cc:10", message)
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


if __name__ == "__main__":
    unittest.main()
