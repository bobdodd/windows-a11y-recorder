#!/usr/bin/env python3
"""Installs the recorder bridge and startup hook into a Chromium checkout."""

from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path


BRIDGE_DEP = '"//chromium/recorder_bridge",'
BRIDGE_INCLUDE = '#include "chromium/recorder_bridge/browser_bridge.h"'
BLINK_BRIDGE_INCLUDE = (
    '#include "chromium/recorder_bridge/browser_bridge.h"'
)
BLINK_DOCUMENT_INCLUDE = (
    '#include "third_party/blink/renderer/core/dom/document.h"'
)
BLINK_PAGE_INCLUDE = '#include "third_party/blink/renderer/core/page/page.h"'
BLINK_CORE_DEP = '    "//chromium/recorder_bridge",'
BLINK_SCHEDULER_DEP = '    "//chromium/recorder_bridge",'
CHILD_LAUNCHER_INCLUDE = (
    '#include "chromium/recorder_bridge/browser_bridge.h"'
)
CHILD_LAUNCHER_INCLUDE_BLOCK = f"""\
#if BUILDFLAG(IS_WIN)
{CHILD_LAUNCHER_INCLUDE}
#endif
"""
BRIDGE_INCLUDE_BLOCK = f"""\
#if BUILDFLAG(IS_WIN)
{BRIDGE_INCLUDE}
#endif
"""
HOOK = """\
#if BUILDFLAG(IS_WIN)
  std::string recorder_bridge_error;
  if (!a11y_recorder::InitializeProcessBridge(
          &recorder_bridge_error)) {
    a11y_recorder::WriteRecorderBridgeDiagnostic(
        "Recorder process bridge initialization failed: " +
        recorder_bridge_error);
    LOG(ERROR) << "Windows A11y Recorder bridge failed: "
               << recorder_bridge_error;
    return content::RESULT_CODE_NORMAL_EXIT;
  }
#endif
"""
ORIGINAL_CHILD_LAUNCHER_HOOK = """\
  std::string recorder_bridge_error;
  if (!a11y_recorder::AppendRecorderBootstrapToChildProcess(
          command_line(), options, child_process_id().value(),
          &recorder_bridge_error)) {
    LOG(ERROR) << "Windows A11y Recorder child bootstrap failed: "
               << recorder_bridge_error;
    return false;
  }
"""
TRACED_CHILD_LAUNCHER_HOOK = """\
  std::string recorder_bridge_error;
  if (!a11y_recorder::AppendRecorderBootstrapToChildProcess(
          command_line(), options, child_process_id().value(),
          &recorder_bridge_error)) {
    a11y_recorder::WriteRecorderBridgeDiagnostic(
        "Recorder child bootstrap attachment failed: " +
        recorder_bridge_error);
    LOG(ERROR) << "Windows A11y Recorder child bootstrap failed: "
               << recorder_bridge_error;
    return false;
  }
"""
SHARED_CHILD_LAUNCHER_HOOK = """\
  std::string recorder_bridge_error;
  if (!a11y_recorder::AppendRecorderBootstrapToChildProcess(
          command_line(), options_ptr, child_process_id().value(),
          &recorder_bridge_error)) {
    a11y_recorder::WriteRecorderBridgeDiagnostic(
        "Recorder child bootstrap attachment failed: " +
        recorder_bridge_error);
    LOG(ERROR) << "Windows A11y Recorder child bootstrap failed: "
               << recorder_bridge_error;
    return;
  }
"""
LEGACY_SHARED_CHILD_LAUNCHER_HOOK = f"""\
#if BUILDFLAG(IS_WIN)
{TRACED_CHILD_LAUNCHER_HOOK}#endif
"""
CHILD_LAUNCHER_HOOK = f"""\
#if BUILDFLAG(IS_WIN)
{SHARED_CHILD_LAUNCHER_HOOK}#endif
"""
BLINK_LISTENER_HOOK = """\
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
LEGACY_BLINK_LISTENER_CALL = """\
      a11y_recorder::RecordBlinkListenerRegistered(
          recorder_target->GetDocument().GetDomNodeId(),
"""
CURRENT_BLINK_LISTENER_CALL = """\
      a11y_recorder::RecordBlinkListenerRegistered(
          reinterpret_cast<uintptr_t>(registered_listener),
          recorder_target->GetDocument().GetDomNodeId(),
"""
BLINK_LISTENER_REMOVED_HOOK = """\
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
LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK = """\
    a11y_recorder::BeginBlinkListenerInvocation(
        reinterpret_cast<uintptr_t>(&event),
        reinterpret_cast<uintptr_t>(registered_listener.Get()));
"""
BLINK_LISTENER_INVOCATION_STARTED_HOOK = """\
    if (Node* recorder_current_target = ToNode()) {
      Element* recorder_current_element =
          DynamicTo<Element>(recorder_current_target);
      a11y_recorder::BeginBlinkListenerInvocation(
          reinterpret_cast<uintptr_t>(&event),
          reinterpret_cast<uintptr_t>(registered_listener.Get()),
          recorder_current_target->GetDocument().GetDomNodeId(),
          recorder_current_target->GetDomNodeId(),
          recorder_current_target->nodeName().Utf8().c_str(),
          recorder_current_element
              ? recorder_current_element->GetIdAttribute().Utf8().c_str()
              : "");
    }
"""
BLINK_LISTENER_INVOKED_HOOK = """\
    a11y_recorder::RecordBlinkListenerInvoked(
        reinterpret_cast<uintptr_t>(&event),
        event.eventPhase() == Event::PhaseType::kCapturingPhase
            ? 1
            : event.eventPhase() == Event::PhaseType::kAtTarget
                  ? 2
                  : event.eventPhase() == Event::PhaseType::kBubblingPhase ? 3
                                                                           : 0,
        event.defaultPrevented(),
        event.PropagationStopped(),
        event.ImmediatePropagationStopped());
"""
LEGACY_BLINK_DISPATCH_HOOK_WITH_IDENTITY = """\
  Element* recorder_element = DynamicTo<Element>(*node_);
  a11y_recorder::RecordBlinkDispatchStarted(
      reinterpret_cast<uintptr_t>(event_),
      node_->GetDocument().GetDomNodeId(),
      node_->GetDomNodeId(),
      event_->type().Utf8().c_str(),
      node_->nodeName().Utf8().c_str(),
      recorder_element
          ? recorder_element->GetIdAttribute().Utf8().c_str()
          : "",
      event_->isTrusted());
"""
BLINK_DISPATCH_HOOK = """\
  Element* recorder_element = DynamicTo<Element>(*node_);
  a11y_recorder::RecordBlinkDispatchStarted(
      reinterpret_cast<uintptr_t>(event_),
      node_->GetDocument().GetDomNodeId(),
      node_->GetDomNodeId(),
      event_->type().Utf8().c_str(),
      node_->nodeName().Utf8().c_str(),
      recorder_element
          ? recorder_element->GetIdAttribute().Utf8().c_str()
          : "",
      event_->isTrusted());
  for (const NodeEventContext& recorder_context :
       event_->GetEventPath().NodeEventContexts()) {
    Node& recorder_path_node = recorder_context.GetNode();
    Element* recorder_path_element =
        DynamicTo<Element>(recorder_path_node);
    a11y_recorder::RecordBlinkDispatchPathNode(
        reinterpret_cast<uintptr_t>(event_),
        recorder_path_node.GetDocument().GetDomNodeId(),
        recorder_path_node.GetDomNodeId(),
        recorder_path_node.nodeName().Utf8().c_str(),
        recorder_path_element
            ? recorder_path_element->GetIdAttribute().Utf8().c_str()
            : "");
  }
  a11y_recorder::CompleteBlinkDispatchStart(
      reinterpret_cast<uintptr_t>(event_));
"""
LEGACY_BLINK_DISPATCH_CALL = """\
  a11y_recorder::RecordBlinkDispatchStarted(
      node_->GetDocument().GetDomNodeId(),
"""
CURRENT_BLINK_DISPATCH_CALL = """\
  a11y_recorder::RecordBlinkDispatchStarted(
      reinterpret_cast<uintptr_t>(event_),
      node_->GetDocument().GetDomNodeId(),
"""
BLINK_DISPATCH_COMPLETED_HOOK = """\
  a11y_recorder::RecordBlinkDispatchCompleted(
      reinterpret_cast<uintptr_t>(event_),
      result == DispatchEventResult::kCanceledByEventHandler
          ? 1
          : result == DispatchEventResult::kCanceledByDefaultEventHandler
                ? 2
                : result == DispatchEventResult::kCanceledBeforeDispatch ? 3
                                                                         : 0,
      event_->defaultPrevented(),
      event_->PropagationStopped(),
      event_->ImmediatePropagationStopped());
"""
BLINK_DEFAULT_ACTION_TARGET_HOOK = """\
    Element* recorder_default_target_element = DynamicTo<Element>(*node_);
    a11y_recorder::RecordBlinkDefaultAction(
        reinterpret_cast<uintptr_t>(event_),
        node_->GetDocument().GetDomNodeId(),
        node_->GetDomNodeId(),
        node_->nodeName().Utf8().c_str(),
        recorder_default_target_element
            ? recorder_default_target_element->GetIdAttribute().Utf8().c_str()
            : "",
        0,
        event_->defaultPrevented(),
        event_->PropagationStopped(),
        event_->ImmediatePropagationStopped());
"""
BLINK_DEFAULT_ACTION_ANCESTOR_HOOK = """\
        Node& recorder_default_ancestor =
            event_->GetEventPath()[i].GetNode();
        Element* recorder_default_ancestor_element =
            DynamicTo<Element>(recorder_default_ancestor);
        a11y_recorder::RecordBlinkDefaultAction(
            reinterpret_cast<uintptr_t>(event_),
            recorder_default_ancestor.GetDocument().GetDomNodeId(),
            recorder_default_ancestor.GetDomNodeId(),
            recorder_default_ancestor.nodeName().Utf8().c_str(),
            recorder_default_ancestor_element
                ? recorder_default_ancestor_element->GetIdAttribute()
                      .Utf8()
                      .c_str()
                : "",
            0,
            event_->defaultPrevented(),
            event_->PropagationStopped(),
            event_->ImmediatePropagationStopped());
"""
BLINK_DEFAULT_ACTION_SUPPRESSED_HOOK = """\
    Element* recorder_default_suppressed_element = DynamicTo<Element>(*node_);
    a11y_recorder::RecordBlinkDefaultAction(
        reinterpret_cast<uintptr_t>(event_),
        node_->GetDocument().GetDomNodeId(),
        node_->GetDomNodeId(),
        node_->nodeName().Utf8().c_str(),
        recorder_default_suppressed_element
            ? recorder_default_suppressed_element->GetIdAttribute()
                  .Utf8()
                  .c_str()
            : "",
        event_->defaultPrevented() ? 1 : event_->DefaultHandled() ? 2 : 3,
        event_->defaultPrevented(),
        event_->PropagationStopped(),
        event_->ImmediatePropagationStopped());
"""
BLINK_TIMER_REQUESTED_DELAY_HOOK = """\
  const base::TimeDelta recorder_requested_timeout =
      std::max(timeout, base::TimeDelta());
"""
LEGACY_BLINK_TIMER_SCHEDULED_HOOK = """\
  if (auto* recorder_window = DynamicTo<LocalDOMWindow>(context)) {
    if (Document* recorder_document = recorder_window->document()) {
      a11y_recorder::RecordBlinkTimerScheduled(
          reinterpret_cast<uintptr_t>(this),
          recorder_document->GetDomNodeId(),
          timeout_id_,
          !single_shot,
          recorder_requested_timeout.InMillisecondsF(),
          timeout.InMillisecondsF(),
          nesting_level_);
    }
  }
"""
BLINK_TIMER_SCHEDULED_HOOK = """\
  if (auto* recorder_window = DynamicTo<LocalDOMWindow>(context)) {
    if (Document* recorder_document = recorder_window->document()) {
      Page* recorder_page = recorder_document->GetPage();
      const int recorder_page_lifecycle_state =
          !recorder_page ? 0
                         : recorder_page->Frozen()
                               ? 3
                               : recorder_page->IsPageVisible() ? 1 : 2;
      a11y_recorder::RecordBlinkTimerScheduled(
          reinterpret_cast<uintptr_t>(this),
          recorder_document->GetDomNodeId(),
          timeout_id_,
          !single_shot,
          recorder_requested_timeout.InMillisecondsF(),
          timeout.InMillisecondsF(),
          nesting_level_,
          recorder_page_lifecycle_state);
    }
  }
"""
LEGACY_BLINK_TIMER_CANCELLED_HOOK = """\
    a11y_recorder::RecordBlinkTimerCancelled(
        reinterpret_cast<uintptr_t>(timer));
"""
BLINK_TIMER_CANCELLED_HOOK = """\
    if (auto* recorder_window = DynamicTo<LocalDOMWindow>(context)) {
      if (Document* recorder_document = recorder_window->document()) {
        Page* recorder_page = recorder_document->GetPage();
        const int recorder_page_lifecycle_state =
            !recorder_page ? 0
                           : recorder_page->Frozen()
                                 ? 3
                                 : recorder_page->IsPageVisible() ? 1 : 2;
        a11y_recorder::RecordBlinkTimerCancelled(
            reinterpret_cast<uintptr_t>(timer),
            recorder_page_lifecycle_state);
      }
    }
"""
LEGACY_BLINK_TIMER_FIRED_HOOK = """\
  a11y_recorder::RecordBlinkTimerFired(
      reinterpret_cast<uintptr_t>(this), is_interval);
"""
BLINK_TIMER_FIRED_HOOK = """\
  if (auto* recorder_window =
          DynamicTo<LocalDOMWindow>(GetExecutionContext())) {
    if (Document* recorder_document = recorder_window->document()) {
      Page* recorder_page = recorder_document->GetPage();
      const int recorder_page_lifecycle_state =
          !recorder_page ? 0
                         : recorder_page->Frozen()
                               ? 3
                               : recorder_page->IsPageVisible() ? 1 : 2;
      a11y_recorder::RecordBlinkTimerFired(
          reinterpret_cast<uintptr_t>(this), is_interval,
          recorder_page_lifecycle_state);
    }
  }
"""
LEGACY_BLINK_ANIMATION_FRAME_SCHEDULED_HOOK = """\
  if (type == FrameCallbackType::kWebExposed) {
    if (auto* recorder_window =
            DynamicTo<LocalDOMWindow>(context_.Get())) {
      if (Document* recorder_document = recorder_window->document()) {
        a11y_recorder::RecordBlinkAnimationFrameScheduled(
            reinterpret_cast<uintptr_t>(callback),
            recorder_document->GetDomNodeId(), id);
      }
    }
  }
"""
BLINK_ANIMATION_FRAME_SCHEDULED_HOOK = """\
  if (type == FrameCallbackType::kWebExposed) {
    if (auto* recorder_window =
            DynamicTo<LocalDOMWindow>(context_.Get())) {
      if (Document* recorder_document = recorder_window->document()) {
        Page* recorder_page = recorder_document->GetPage();
        const int recorder_page_lifecycle_state =
            !recorder_page ? 0
                           : recorder_page->Frozen()
                                 ? 3
                                 : recorder_page->IsPageVisible() ? 1 : 2;
        a11y_recorder::RecordBlinkAnimationFrameScheduled(
            reinterpret_cast<uintptr_t>(callback),
            recorder_document->GetDomNodeId(), id,
            recorder_page_lifecycle_state);
      }
    }
  }
"""
LEGACY_BLINK_ANIMATION_FRAME_CANCELLED_INDEX_HOOK = """\
      a11y_recorder::RecordBlinkAnimationFrameCancelled(
          reinterpret_cast<uintptr_t>(callbacks[i].Get()));
"""
BLINK_ANIMATION_FRAME_CANCELLED_INDEX_HOOK = """\
      if (auto* recorder_window =
              DynamicTo<LocalDOMWindow>(context_.Get())) {
        if (Document* recorder_document = recorder_window->document()) {
          Page* recorder_page = recorder_document->GetPage();
          const int recorder_page_lifecycle_state =
              !recorder_page ? 0
                             : recorder_page->Frozen()
                                   ? 3
                                   : recorder_page->IsPageVisible() ? 1 : 2;
          a11y_recorder::RecordBlinkAnimationFrameCancelled(
              reinterpret_cast<uintptr_t>(callbacks[i].Get()),
              recorder_page_lifecycle_state);
        }
      }
"""
LEGACY_BLINK_ANIMATION_FRAME_CANCELLED_CALLBACK_HOOK = """\
      a11y_recorder::RecordBlinkAnimationFrameCancelled(
          reinterpret_cast<uintptr_t>(callback.Get()));
"""
BLINK_ANIMATION_FRAME_CANCELLED_CALLBACK_HOOK = """\
      if (auto* recorder_window =
              DynamicTo<LocalDOMWindow>(context_.Get())) {
        if (Document* recorder_document = recorder_window->document()) {
          Page* recorder_page = recorder_document->GetPage();
          const int recorder_page_lifecycle_state =
              !recorder_page ? 0
                             : recorder_page->Frozen()
                                   ? 3
                                   : recorder_page->IsPageVisible() ? 1 : 2;
          a11y_recorder::RecordBlinkAnimationFrameCancelled(
              reinterpret_cast<uintptr_t>(callback.Get()),
              recorder_page_lifecycle_state);
        }
      }
"""
LEGACY_BLINK_ANIMATION_FRAME_FIRED_HOOK = """\
    a11y_recorder::RecordBlinkAnimationFrameFired(
        reinterpret_cast<uintptr_t>(callback.Get()));
"""
BLINK_ANIMATION_FRAME_FIRED_HOOK = """\
    if (auto* recorder_window =
            DynamicTo<LocalDOMWindow>(context_.Get())) {
      if (Document* recorder_document = recorder_window->document()) {
        Page* recorder_page = recorder_document->GetPage();
        const int recorder_page_lifecycle_state =
            !recorder_page ? 0
                           : recorder_page->Frozen()
                                 ? 3
                                 : recorder_page->IsPageVisible() ? 1 : 2;
        a11y_recorder::RecordBlinkAnimationFrameFired(
            reinterpret_cast<uintptr_t>(callback.Get()),
            recorder_page_lifecycle_state);
      }
    }
"""
LEGACY_BLINK_IDLE_CALLBACK_SCHEDULED_HOOK = """\
  if (auto* recorder_window =
          DynamicTo<LocalDOMWindow>(GetExecutionContext())) {
    if (Document* recorder_document = recorder_window->document()) {
      a11y_recorder::RecordBlinkIdleCallbackScheduled(
          reinterpret_cast<uintptr_t>(idle_task),
          recorder_document->GetDomNodeId(), id, options->hasTimeout(),
          timeout_millis);
    }
  }
"""
BLINK_IDLE_CALLBACK_SCHEDULED_HOOK = """\
  if (auto* recorder_window =
          DynamicTo<LocalDOMWindow>(GetExecutionContext())) {
    if (Document* recorder_document = recorder_window->document()) {
      Page* recorder_page = recorder_document->GetPage();
      const int recorder_page_lifecycle_state =
          !recorder_page ? 0
                         : recorder_page->Frozen()
                               ? 3
                               : recorder_page->IsPageVisible() ? 1 : 2;
      a11y_recorder::RecordBlinkIdleCallbackScheduled(
          reinterpret_cast<uintptr_t>(idle_task),
          recorder_document->GetDomNodeId(), id, options->hasTimeout(),
          timeout_millis, recorder_page_lifecycle_state);
    }
  }
"""
LEGACY_BLINK_IDLE_CALLBACK_CANCELLED_HOOK = """\
  auto recorder_idle_task = idle_tasks_.find(id);
  if (recorder_idle_task != idle_tasks_.end()) {
    a11y_recorder::RecordBlinkIdleCallbackCancelled(
        reinterpret_cast<uintptr_t>(recorder_idle_task->value.Get()));
  }
"""
BLINK_IDLE_CALLBACK_CANCELLED_HOOK = """\
  auto recorder_idle_task = idle_tasks_.find(id);
  if (recorder_idle_task != idle_tasks_.end()) {
    if (auto* recorder_window =
            DynamicTo<LocalDOMWindow>(GetExecutionContext())) {
      if (Document* recorder_document = recorder_window->document()) {
        Page* recorder_page = recorder_document->GetPage();
        const int recorder_page_lifecycle_state =
            !recorder_page ? 0
                           : recorder_page->Frozen()
                                 ? 3
                                 : recorder_page->IsPageVisible() ? 1 : 2;
        a11y_recorder::RecordBlinkIdleCallbackCancelled(
            reinterpret_cast<uintptr_t>(recorder_idle_task->value.Get()),
            recorder_page_lifecycle_state);
      }
    }
  }
"""
LEGACY_BLINK_IDLE_CALLBACK_FIRED_HOOK = """\
  a11y_recorder::RecordBlinkIdleCallbackFired(
      reinterpret_cast<uintptr_t>(idle_task),
      callback_type == IdleDeadline::CallbackType::kCalledByTimeout);
"""
BLINK_IDLE_CALLBACK_FIRED_HOOK = """\
  if (auto* recorder_window =
          DynamicTo<LocalDOMWindow>(GetExecutionContext())) {
    if (Document* recorder_document = recorder_window->document()) {
      Page* recorder_page = recorder_document->GetPage();
      const int recorder_page_lifecycle_state =
          !recorder_page ? 0
                         : recorder_page->Frozen()
                               ? 3
                               : recorder_page->IsPageVisible() ? 1 : 2;
      a11y_recorder::RecordBlinkIdleCallbackFired(
          reinterpret_cast<uintptr_t>(idle_task),
          callback_type == IdleDeadline::CallbackType::kCalledByTimeout,
          recorder_page_lifecycle_state);
    }
  }
"""
BLINK_THROTTLER_OWNER_DECLARATION = """\
class MainThreadTaskQueue;
"""
BLINK_THROTTLER_CONSTRUCTOR_DECLARATION = """\
  TaskQueueThrottler(MainThreadTaskQueue* owner,
                     base::sequence_manager::TaskQueue* task_queue,
                     const base::TickClock* tick_clock);
"""
BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION = """\
TaskQueueThrottler::TaskQueueThrottler(
    MainThreadTaskQueue* owner,
    base::sequence_manager::TaskQueue* task_queue,
    const base::TickClock* tick_clock)
    : owner_(owner->AsWeakPtr()),
      task_queue_(task_queue),
      tick_clock_(tick_clock) {}
"""
LEGACY_BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION = """\
TaskQueueThrottler::TaskQueueThrottler(
    MainThreadTaskQueue* owner,
    base::sequence_manager::TaskQueue* task_queue,
    const base::TickClock* tick_clock)
    : owner_(owner), task_queue_(task_queue), tick_clock_(tick_clock) {}
"""
BLINK_THROTTLER_OWNER_MEMBER = """\
  base::WeakPtr<MainThreadTaskQueue> owner_;
"""
LEGACY_BLINK_THROTTLER_OWNER_MEMBER = """\
  const raw_ptr<MainThreadTaskQueue> owner_;
"""
BLINK_MAIN_THREAD_QUEUE_CONSTRUCTION = """\
      throttler_.emplace(this, task_queue_.get(),
                         main_thread_scheduler_->GetTickClock());
"""
BLINK_FRAME_THROTTLING_ACCESSOR = """\
  int RecorderThrottlingType() const {
    return static_cast<int>(throttling_type_);
  }
"""
BLINK_SCHEDULER_DECISION_HOOK = """\
  std::optional<base::sequence_manager::WakeUp> allowed_wake_up =
      GetNextAllowedWakeUpImpl(lazy_now, next_desired_wake_up,
                               has_ready_task);
  base::TimeTicks desired_wake_up;
  if (has_ready_task) {
    desired_wake_up = lazy_now->Now();
  } else if (next_desired_wake_up.has_value()) {
    desired_wake_up =
        std::max(next_desired_wake_up->time, lazy_now->Now());
  }
  if (owner_ && !desired_wake_up.is_null() &&
      allowed_wake_up.has_value() &&
      allowed_wake_up->time > desired_wake_up) {
    FrameSchedulerImpl* frame_scheduler = owner_->GetFrameScheduler();
    std::optional<QueueBlockType> block_type =
        GetBlockType(desired_wake_up);
    if (frame_scheduler && block_type.has_value()) {
      a11y_recorder::RecordBlinkSchedulerWakeUpDeferred(
          static_cast<int>(owner_->queue_type()),
          frame_scheduler->RecorderThrottlingType(),
          desired_wake_up.since_origin().InMicroseconds(),
          allowed_wake_up->time.since_origin().InMicroseconds(),
          has_ready_task, static_cast<int>(*block_type));
    }
  }
  return allowed_wake_up;
"""


def replace_once(text: str, old: str, new: str, path: Path) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(
            f"{path}: expected exactly one integration anchor, found {count}"
        )
    return text.replace(old, new, 1)


def upgrade_legacy_hooks(
    text: str, replacements: tuple[tuple[str, str], ...], path: Path
) -> str:
    for legacy_hook, current_hook in replacements:
        count = text.count(legacy_hook)
        if count > 1:
            raise RuntimeError(
                f"{path}: expected at most one legacy integration hook, "
                f"found {count}"
            )
        if count == 1:
            text = text.replace(legacy_hook, current_hook, 1)
    return text


def patch_main_delegate(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    text = text.replace(
        "InitializeBrowserProcessBridge", "InitializeProcessBridge"
    )
    if BRIDGE_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "chrome/app/chrome_main_delegate.h"\n',
            '#include "chrome/app/chrome_main_delegate.h"\n'
            f"{BRIDGE_INCLUDE_BLOCK}",
            path,
        )

    if "InitializeProcessBridge" not in text.replace(
        BRIDGE_INCLUDE, ""
    ):
        function = (
            "std::optional<int> ChromeMainDelegate::BasicStartupComplete() {"
        )
        text = replace_once(text, function, f"{function}\n{HOOK}", path)
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_chrome_build(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    if BRIDGE_DEP in text:
        return

    target = '  shared_library("chrome_dll") {'
    target_index = text.find(target)
    if target_index < 0:
        raise RuntimeError(f"{path}: Windows chrome_dll target not found")

    source = '      "app/chrome_main_delegate.cc",'
    source_index = text.find(source, target_index)
    if source_index < 0:
        raise RuntimeError(
            f"{path}: chrome_main_delegate source not found in chrome_dll"
        )

    target_end = text.find("\n  }\n", source_index)
    deps = text.find("    deps = [\n", source_index)
    if deps < 0 or (target_end >= 0 and deps > target_end):
        raise RuntimeError(f"{path}: chrome_dll deps list not found")

    opening = "    deps = [\n"
    text = text[:deps] + text[deps:].replace(
        opening, opening + f"      {BRIDGE_DEP}\n", 1
    )
    path.write_text(text, encoding="utf-8", newline="\n")


def remove_legacy_child_launcher_hook(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    text = text.replace(f"{CHILD_LAUNCHER_INCLUDE}\n", "")
    text = text.replace(TRACED_CHILD_LAUNCHER_HOOK, "")
    text = text.replace(ORIGINAL_CHILD_LAUNCHER_HOOK, "")
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_child_launcher(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    text = text.replace(
        LEGACY_SHARED_CHILD_LAUNCHER_HOOK,
        CHILD_LAUNCHER_HOOK,
    )
    if CHILD_LAUNCHER_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "content/browser/child_process_launcher_helper.h"\n',
            '#include "content/browser/child_process_launcher_helper.h"\n'
            f"{CHILD_LAUNCHER_INCLUDE_BLOCK}",
            path,
        )

    if "AppendRecorderBootstrapToChildProcess" not in text:
        function = "void ChildProcessLauncherHelper::LaunchOnLauncherThread()"
        function_index = text.find(function)
        if function_index < 0:
            raise RuntimeError(
                f"{path}: shared child launch function not found"
            )
        anchor = (
            "  if (BeforeLaunchOnLauncherThread("
            "*files_to_register, options_ptr)) {"
        )
        anchor_index = text.find(anchor, function_index)
        function_end = text.find("\n}\n", function_index)
        if anchor_index < 0 or (
            function_end >= 0 and anchor_index > function_end
        ):
            raise RuntimeError(
                f"{path}: shared child launch anchor not found"
            )
        text = (
            text[:anchor_index]
            + CHILD_LAUNCHER_HOOK
            + text[anchor_index:]
        )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_content_browser_build(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    if BRIDGE_DEP in text:
        return

    source = '      "child_process_launcher_helper_win.cc",'
    source_index = text.find(source)
    if source_index < 0:
        raise RuntimeError(f"{path}: Windows child launcher source not found")
    windows_block = text.rfind("  if (is_win) {", 0, source_index)
    deps = text.find("    deps += [\n", source_index)
    block_end = text.find("\n  }\n", source_index)
    if windows_block < 0 or deps < 0 or (
        block_end >= 0 and deps > block_end
    ):
        raise RuntimeError(f"{path}: Windows browser deps list not found")

    opening = "    deps += [\n"
    text = text[:deps] + text[deps:].replace(
        opening, opening + f"      {BRIDGE_DEP}\n", 1
    )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_event_target(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    if BLINK_BRIDGE_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "base/time/time.h"\n',
            '#include "base/time/time.h"\n'
            f"{BLINK_BRIDGE_INCLUDE}\n"
            '#include "third_party/blink/renderer/core/dom/document.h"\n'
            '#include "third_party/blink/renderer/core/dom/element.h"\n'
            '#include "third_party/blink/renderer/core/dom/node.h"\n',
            path,
        )

    if LEGACY_BLINK_LISTENER_CALL in text:
        text = replace_once(
            text,
            LEGACY_BLINK_LISTENER_CALL,
            CURRENT_BLINK_LISTENER_CALL,
            path,
        )
    if LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK in text:
        text = replace_once(
            text,
            LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK,
            BLINK_LISTENER_INVOCATION_STARTED_HOOK,
            path,
        )
    if "RecordBlinkListenerRegistered" not in text:
        anchor = "  if (added) {\n    CHECK(registered_listener);\n"
        text = replace_once(
            text,
            anchor,
            f"{anchor}{BLINK_LISTENER_HOOK}",
            path,
        )
    if "RecordBlinkListenerRemoved" not in text:
        anchor = (
            "  CHECK(registered_listener);\n"
            "  RemovedEventListener(event_type, *registered_listener);\n"
        )
        text = replace_once(
            text,
            anchor,
            (
                "  CHECK(registered_listener);\n"
                f"{BLINK_LISTENER_REMOVED_HOOK}"
                "  RemovedEventListener(event_type, *registered_listener);\n"
            ),
            path,
        )
    if "BeginBlinkListenerInvocation" not in text:
        anchor = (
            "    EventListener* listener = registered_listener->Callback();\n"
            "    // The listener will be retained by Member<EventListener> in the\n"
        )
        text = replace_once(
            text,
            anchor,
            (
                "    EventListener* listener = registered_listener->Callback();\n"
                f"{BLINK_LISTENER_INVOCATION_STARTED_HOOK}"
                "    // The listener will be retained by Member<EventListener> in the\n"
            ),
            path,
        )
    if "RecordBlinkListenerInvoked" not in text:
        anchor = (
            "    // To match Mozilla, the AT_TARGET phase fires both capturing and "
            "bubbling\n"
            "    // event listeners, even though that violates some versions of the "
            "DOM spec.\n"
            "    listener->Invoke(context, &event);\n"
        )
        text = replace_once(
            text,
            anchor,
            (
                "    // To match Mozilla, the AT_TARGET phase fires both capturing and "
                "bubbling\n"
                "    // event listeners, even though that violates some versions of the "
                "DOM spec.\n"
                "    listener->Invoke(context, &event);\n"
                f"{BLINK_LISTENER_INVOKED_HOOK}"
            ),
            path,
        )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_event_dispatcher(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    text = text.replace(
        "reinterpret_cast<uintptr_t>(event_.Get())",
        "reinterpret_cast<uintptr_t>(event_)",
    )
    if BLINK_BRIDGE_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "build/build_config.h"\n',
            '#include "build/build_config.h"\n'
            f"{BLINK_BRIDGE_INCLUDE}\n",
            path,
        )

    if LEGACY_BLINK_DISPATCH_CALL in text:
        text = replace_once(
            text,
            LEGACY_BLINK_DISPATCH_CALL,
            CURRENT_BLINK_DISPATCH_CALL,
            path,
        )
    if (
        "CompleteBlinkDispatchStart" not in text
        and LEGACY_BLINK_DISPATCH_HOOK_WITH_IDENTITY in text
    ):
        text = replace_once(
            text,
            LEGACY_BLINK_DISPATCH_HOOK_WITH_IDENTITY,
            BLINK_DISPATCH_HOOK,
            path,
        )
    if "RecordBlinkDispatchStarted" not in text:
        anchor = (
            "  event_->SetTarget("
            "&EventPath::EventTargetRespectingTargetRules(*node_));\n"
            "#if DCHECK_IS_ON()\n"
        )
        text = replace_once(
            text,
            anchor,
            (
                "  event_->SetTarget("
                "&EventPath::EventTargetRespectingTargetRules(*node_));\n"
                f"{BLINK_DISPATCH_HOOK}"
                "#if DCHECK_IS_ON()\n"
            ),
            path,
        )
    if "RecordBlinkDispatchCompleted" not in text:
        anchor = (
            "  auto result = EventTarget::GetDispatchEventResult(*event_);\n"
            "\n"
            "  return result;\n"
        )
        text = replace_once(
            text,
            anchor,
            (
                "  auto result = EventTarget::GetDispatchEventResult(*event_);\n"
                f"{BLINK_DISPATCH_COMPLETED_HOOK}"
                "\n"
                "  return result;\n"
            ),
            path,
        )
    if "recorder_default_target_element" not in text:
        anchor = "    node_->DefaultEventHandler(*event_);\n"
        text = replace_once(
            text,
            anchor,
            f"{BLINK_DEFAULT_ACTION_TARGET_HOOK}{anchor}",
            path,
        )
    if "recorder_default_ancestor_element" not in text:
        anchor = (
            "        event_->GetEventPath()[i].GetNode()."
            "DefaultEventHandler(*event_);\n"
        )
        text = replace_once(
            text,
            anchor,
            f"{BLINK_DEFAULT_ACTION_ANCESTOR_HOOK}{anchor}",
            path,
        )
    if "recorder_default_suppressed_element" not in text:
        anchor = "  } else {\n#if BUILDFLAG(IS_MAC)\n"
        text = replace_once(
            text,
            anchor,
            (
                "  } else {\n"
                f"{BLINK_DEFAULT_ACTION_SUPPRESSED_HOOK}"
                "#if BUILDFLAG(IS_MAC)\n"
            ),
            path,
        )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_dom_timer(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    text = upgrade_legacy_hooks(
        text,
        (
            (LEGACY_BLINK_TIMER_SCHEDULED_HOOK, BLINK_TIMER_SCHEDULED_HOOK),
            (LEGACY_BLINK_TIMER_CANCELLED_HOOK, BLINK_TIMER_CANCELLED_HOOK),
            (LEGACY_BLINK_TIMER_FIRED_HOOK, BLINK_TIMER_FIRED_HOOK),
        ),
        path,
    )
    if BLINK_BRIDGE_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "base/check_deref.h"\n',
            '#include "base/check_deref.h"\n'
            f"{BLINK_BRIDGE_INCLUDE}\n"
            '#include "third_party/blink/renderer/core/dom/document.h"\n'
            '#include "third_party/blink/renderer/core/frame/'
            'local_dom_window.h"\n'
            '#include "third_party/blink/renderer/core/page/page.h"\n',
            path,
        )
    elif BLINK_PAGE_INCLUDE not in text:
        text = replace_once(
            text,
            f"{BLINK_DOCUMENT_INCLUDE}\n",
            f"{BLINK_DOCUMENT_INCLUDE}\n{BLINK_PAGE_INCLUDE}\n",
            path,
        )
    if "recorder_requested_timeout" not in text:
        anchor = "  DCHECK_GT(timeout_id_, 0);\n\n"
        text = replace_once(
            text,
            anchor,
            f"{anchor}{BLINK_TIMER_REQUESTED_DELAY_HOOK}\n",
            path,
        )
    if "RecordBlinkTimerScheduled" not in text:
        anchor = (
            "  DEVTOOLS_TIMELINE_TRACE_EVENT_INSTANT(\n"
            '      "TimerInstall", inspector_timer_install_event::Data, &context,\n'
        )
        text = replace_once(
            text,
            anchor,
            f"{BLINK_TIMER_SCHEDULED_HOOK}\n{anchor}",
            path,
        )
    if "RecordBlinkTimerCancelled" not in text:
        anchor = (
            "  if (DOMTimer* timer =\n"
            "          DOMTimerCoordinator::From(context)."
            "RemoveTimeoutByID(timeout_id)) {\n"
        )
        text = replace_once(
            text,
            anchor,
            f"{anchor}{BLINK_TIMER_CANCELLED_HOOK}",
            path,
        )
    if "RecordBlinkTimerFired" not in text:
        function = "void DOMTimer::Fired() {"
        function_index = text.find(function)
        if function_index < 0:
            raise RuntimeError(
                f"{path}: DOMTimer::Fired function not found"
            )
        anchor = (
            "  const bool is_interval = RepeatInterval().has_value();\n\n"
        )
        anchor_index = text.find(anchor, function_index)
        if anchor_index < 0:
            raise RuntimeError(
                f"{path}: DOMTimer::Fired timer-kind anchor not found"
            )
        insertion_index = anchor_index + len(anchor)
        text = (
            text[:insertion_index]
            + f"{BLINK_TIMER_FIRED_HOOK}\n"
            + text[insertion_index:]
        )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_animation_frame_callbacks(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    text = upgrade_legacy_hooks(
        text,
        (
            (
                LEGACY_BLINK_ANIMATION_FRAME_SCHEDULED_HOOK,
                BLINK_ANIMATION_FRAME_SCHEDULED_HOOK,
            ),
            (
                LEGACY_BLINK_ANIMATION_FRAME_CANCELLED_INDEX_HOOK,
                BLINK_ANIMATION_FRAME_CANCELLED_INDEX_HOOK,
            ),
            (
                LEGACY_BLINK_ANIMATION_FRAME_CANCELLED_CALLBACK_HOOK,
                BLINK_ANIMATION_FRAME_CANCELLED_CALLBACK_HOOK,
            ),
            (
                LEGACY_BLINK_ANIMATION_FRAME_FIRED_HOOK,
                BLINK_ANIMATION_FRAME_FIRED_HOOK,
            ),
        ),
        path,
    )
    if BLINK_BRIDGE_INCLUDE not in text:
        header = (
            '#include "third_party/blink/renderer/core/dom/'
            'frame_request_callback_collection.h"\n'
        )
        text = replace_once(
            text,
            header,
            header
            + f"{BLINK_BRIDGE_INCLUDE}\n"
            + '#include "third_party/blink/renderer/core/dom/document.h"\n'
            + '#include "third_party/blink/renderer/core/frame/'
            'local_dom_window.h"\n'
            + '#include "third_party/blink/renderer/core/page/page.h"\n',
            path,
        )
    elif BLINK_PAGE_INCLUDE not in text:
        text = replace_once(
            text,
            f"{BLINK_DOCUMENT_INCLUDE}\n",
            f"{BLINK_DOCUMENT_INCLUDE}\n{BLINK_PAGE_INCLUDE}\n",
            path,
        )
    if "RecordBlinkAnimationFrameScheduled" not in text:
        function = (
            "FrameRequestCallbackCollection::RegisterFrameCallback("
        )
        function_index = text.find(function)
        if function_index < 0:
            raise RuntimeError(
                f"{path}: RegisterFrameCallback function not found"
            )
        anchor = (
            '  DEVTOOLS_TIMELINE_TRACE_EVENT_INSTANT("RequestAnimationFrame",\n'
        )
        anchor_index = text.find(anchor, function_index)
        if anchor_index < 0:
            raise RuntimeError(
                f"{path}: RegisterFrameCallback trace anchor not found"
            )
        text = (
            text[:anchor_index]
            + f"{BLINK_ANIMATION_FRAME_SCHEDULED_HOOK}\n"
            + text[anchor_index:]
        )
    if "RecordBlinkAnimationFrameCancelled" not in text:
        function = "void FrameRequestCallbackCollection::CancelFrameCallback("
        function_index = text.find(function)
        if function_index < 0:
            raise RuntimeError(
                f"{path}: CancelFrameCallback function not found"
            )
        next_function_index = text.find(
            "\nvoid FrameRequestCallbackCollection::", function_index + 1
        )
        function_text = text[
            function_index:
            next_function_index if next_function_index >= 0 else len(text)
        ]
        first_anchor = (
            "      callbacks[i]->async_task_context()->Cancel();\n"
        )
        second_anchor = (
            "      callback->async_task_context()->Cancel();\n"
        )
        if function_text.count(first_anchor) != 1:
            raise RuntimeError(
                f"{path}: pending animation-frame cancellation anchor "
                f"count was {function_text.count(first_anchor)}"
            )
        if function_text.count(second_anchor) != 1:
            raise RuntimeError(
                f"{path}: invoking animation-frame cancellation anchor "
                f"count was {function_text.count(second_anchor)}"
            )
        function_text = function_text.replace(
            first_anchor,
            first_anchor + BLINK_ANIMATION_FRAME_CANCELLED_INDEX_HOOK,
            1,
        )
        function_text = function_text.replace(
            second_anchor,
            second_anchor + BLINK_ANIMATION_FRAME_CANCELLED_CALLBACK_HOOK,
            1,
        )
        text = (
            text[:function_index]
            + function_text
            + text[
                next_function_index
                if next_function_index >= 0
                else len(text):
            ]
        )
    if "RecordBlinkAnimationFrameFired" not in text:
        function = (
            "void FrameRequestCallbackCollection::"
            "ExecuteFrameCallbacksImpl("
        )
        function_index = text.find(function)
        if function_index < 0:
            raise RuntimeError(
                f"{path}: ExecuteFrameCallbacksImpl function not found"
            )
        next_function_index = text.find(
            "\nvoid FrameRequestCallbackCollection::", function_index + 1
        )
        function_end = (
            next_function_index
            if next_function_index >= 0
            else len(text)
        )
        function_text = text[function_index:function_end]
        anchor = "    if (callback->GetUseLegacyTimeBase()) {\n"
        anchor_offset = function_text.find(anchor)
        if anchor_offset < 0:
            legacy_anchor = (
                "    callback->Invoke(high_res_now_ms);\n"
            )
            if function_text.count(legacy_anchor) != 1:
                raise RuntimeError(
                    f"{path}: ExecuteFrameCallbacksImpl invocation anchor "
                    "not found"
                )
            anchor = legacy_anchor
            anchor_offset = function_text.find(anchor)
        anchor_index = function_index + anchor_offset
        text = (
            text[:anchor_index]
            + f"{BLINK_ANIMATION_FRAME_FIRED_HOOK}\n"
            + text[anchor_index:]
        )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_idle_callbacks(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    text = upgrade_legacy_hooks(
        text,
        (
            (
                LEGACY_BLINK_IDLE_CALLBACK_SCHEDULED_HOOK,
                BLINK_IDLE_CALLBACK_SCHEDULED_HOOK,
            ),
            (
                LEGACY_BLINK_IDLE_CALLBACK_CANCELLED_HOOK,
                BLINK_IDLE_CALLBACK_CANCELLED_HOOK,
            ),
            (
                LEGACY_BLINK_IDLE_CALLBACK_FIRED_HOOK,
                BLINK_IDLE_CALLBACK_FIRED_HOOK,
            ),
        ),
        path,
    )
    if BLINK_BRIDGE_INCLUDE not in text:
        header = (
            '#include "third_party/blink/renderer/core/scheduler/'
            'scripted_idle_task_controller.h"\n'
        )
        text = replace_once(
            text,
            header,
            header
            + f"{BLINK_BRIDGE_INCLUDE}\n"
            + '#include "third_party/blink/renderer/core/dom/document.h"\n'
            + '#include "third_party/blink/renderer/core/frame/'
            'local_dom_window.h"\n'
            + '#include "third_party/blink/renderer/core/page/page.h"\n',
            path,
        )
    elif BLINK_PAGE_INCLUDE not in text:
        text = replace_once(
            text,
            f"{BLINK_DOCUMENT_INCLUDE}\n",
            f"{BLINK_DOCUMENT_INCLUDE}\n{BLINK_PAGE_INCLUDE}\n",
            path,
        )
    if "RecordBlinkIdleCallbackScheduled" not in text:
        function = "ScriptedIdleTaskController::RegisterCallback("
        function_index = text.find(function)
        if function_index < 0:
            raise RuntimeError(f"{path}: RegisterCallback function not found")
        anchor = "  PostSchedulerIdleAndTimeoutTasks(id, timeout_millis);\n"
        anchor_index = text.find(anchor, function_index)
        if anchor_index < 0:
            raise RuntimeError(
                f"{path}: idle callback scheduling anchor not found"
            )
        insertion_index = anchor_index + len(anchor)
        text = (
            text[:insertion_index]
            + f"{BLINK_IDLE_CALLBACK_SCHEDULED_HOOK}\n"
            + text[insertion_index:]
        )
    if "RecordBlinkIdleCallbackCancelled" not in text:
        function = "void ScriptedIdleTaskController::CancelCallback("
        function_index = text.find(function)
        if function_index < 0:
            raise RuntimeError(f"{path}: CancelCallback function not found")
        anchor = "  CHECK(IsValidCallbackId(id));\n\n"
        anchor_index = text.find(anchor, function_index)
        if anchor_index < 0:
            raise RuntimeError(
                f"{path}: idle callback cancellation anchor not found"
            )
        insertion_index = anchor_index + len(anchor)
        text = (
            text[:insertion_index]
            + f"{BLINK_IDLE_CALLBACK_CANCELLED_HOOK}\n"
            + text[insertion_index:]
        )
    if "RecordBlinkIdleCallbackFired" not in text:
        function = "void ScriptedIdleTaskController::RunIdleTask("
        function_index = text.find(function)
        if function_index < 0:
            raise RuntimeError(f"{path}: RunIdleTask function not found")
        anchor = (
            "  idle_task->invoke(MakeGarbageCollected<IdleDeadline>(\n"
            "      deadline, cross_origin_isolated_capability, callback_type));\n"
        )
        anchor_index = text.find(anchor, function_index)
        if anchor_index < 0:
            raise RuntimeError(
                f"{path}: idle callback invocation anchor not found"
            )
        text = (
            text[:anchor_index]
            + f"{BLINK_IDLE_CALLBACK_FIRED_HOOK}\n"
            + text[anchor_index:]
        )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_core_build(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    if BLINK_CORE_DEP in text:
        return

    target = 'component("core") {'
    target_index = text.find(target)
    if target_index < 0:
        raise RuntimeError(f"{path}: Blink core component target not found")
    target_end = text.find("\n}", target_index)
    deps = text.find("  deps = [\n", target_index)
    if deps < 0 or (target_end >= 0 and deps > target_end):
        raise RuntimeError(f"{path}: Blink core deps list not found")

    opening = "  deps = [\n"
    text = text[:deps] + text[deps:].replace(
        opening, opening + f"{BLINK_CORE_DEP}\n", 1
    )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_task_queue_throttler_header(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    weak_ptr_include = '#include "base/memory/weak_ptr.h"\n'
    if weak_ptr_include not in text:
        text = replace_once(
            text,
            '#include "base/memory/raw_ptr.h"\n',
            '#include "base/memory/raw_ptr.h"\n' + weak_ptr_include,
            path,
        )
    if BLINK_THROTTLER_OWNER_DECLARATION not in text:
        text = replace_once(
            text,
            "namespace scheduler {\n\nclass BudgetPool;\n",
            "namespace scheduler {\n\nclass BudgetPool;\n"
            f"{BLINK_THROTTLER_OWNER_DECLARATION}",
            path,
        )
    old_constructor = """\
  TaskQueueThrottler(base::sequence_manager::TaskQueue* task_queue,
                     const base::TickClock* tick_clock);
"""
    if BLINK_THROTTLER_CONSTRUCTOR_DECLARATION not in text:
        text = replace_once(
            text,
            old_constructor,
            BLINK_THROTTLER_CONSTRUCTOR_DECLARATION,
            path,
        )
    if BLINK_THROTTLER_OWNER_MEMBER not in text:
        if LEGACY_BLINK_THROTTLER_OWNER_MEMBER in text:
            text = replace_once(
                text,
                LEGACY_BLINK_THROTTLER_OWNER_MEMBER,
                BLINK_THROTTLER_OWNER_MEMBER,
                path,
            )
        else:
            text = replace_once(
                text,
                "  const raw_ptr<base::sequence_manager::TaskQueue> task_queue_;\n",
                f"{BLINK_THROTTLER_OWNER_MEMBER}"
                "  const raw_ptr<base::sequence_manager::TaskQueue> task_queue_;\n",
                path,
            )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_task_queue_throttler(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    includes = (
        f"{BLINK_BRIDGE_INCLUDE}\n"
        '#include "third_party/blink/renderer/platform/scheduler/main_thread/'
        'frame_scheduler_impl.h"\n'
        '#include "third_party/blink/renderer/platform/scheduler/main_thread/'
        'main_thread_task_queue.h"\n'
    )
    if BLINK_BRIDGE_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "base/check_op.h"\n',
            '#include "base/check_op.h"\n' + includes,
            path,
        )
    old_constructor = """\
TaskQueueThrottler::TaskQueueThrottler(
    base::sequence_manager::TaskQueue* task_queue,
    const base::TickClock* tick_clock)
    : task_queue_(task_queue), tick_clock_(tick_clock) {}
"""
    if BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION not in text:
        if LEGACY_BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION in text:
            text = replace_once(
                text,
                LEGACY_BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION,
                BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION,
                path,
            )
        else:
            text = replace_once(
                text,
                old_constructor,
                BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION,
                path,
            )
    old_return = """\
  return GetNextAllowedWakeUpImpl(lazy_now, next_desired_wake_up,
                                  has_ready_task);
"""
    if "RecordBlinkSchedulerWakeUpDeferred" not in text.replace(
        BLINK_BRIDGE_INCLUDE, ""
    ):
        text = replace_once(
            text,
            old_return,
            BLINK_SCHEDULER_DECISION_HOOK,
            path,
        )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_main_thread_task_queue(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    old_construction = """\
      throttler_.emplace(task_queue_.get(),
                         main_thread_scheduler_->GetTickClock());
"""
    if BLINK_MAIN_THREAD_QUEUE_CONSTRUCTION not in text:
        text = replace_once(
            text,
            old_construction,
            BLINK_MAIN_THREAD_QUEUE_CONSTRUCTION,
            path,
        )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_frame_scheduler_header(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    if BLINK_FRAME_THROTTLING_ACCESSOR in text:
        return
    anchor = "  void UpdatePolicy();\n"
    text = replace_once(
        text,
        anchor,
        anchor + "\n" + BLINK_FRAME_THROTTLING_ACCESSOR,
        path,
    )
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_blink_scheduler_build(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    if BLINK_SCHEDULER_DEP in text:
        return
    target = 'blink_platform_sources("scheduler") {'
    target_index = text.find(target)
    if target_index < 0:
        raise RuntimeError(f"{path}: Blink scheduler target not found")
    target_end = text.find("\n}", target_index)
    deps = text.find("  deps = [\n", target_index)
    if deps < 0 or (target_end >= 0 and deps > target_end):
        raise RuntimeError(f"{path}: Blink scheduler deps list not found")
    opening = "  deps = [\n"
    text = text[:deps] + text[deps:].replace(
        opening, opening + f"{BLINK_SCHEDULER_DEP}\n", 1
    )
    path.write_text(text, encoding="utf-8", newline="\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "chromium_src",
        type=Path,
        help="Path to the Chromium src checkout",
    )
    args = parser.parse_args()
    source = args.chromium_src.resolve()
    if not (source / ".git").exists():
        parser.error(f"{source} is not a Chromium Git checkout")

    bridge_source = Path(__file__).resolve().parent / "recorder_bridge"
    bridge_destination = source / "chromium" / "recorder_bridge"
    if bridge_destination.exists():
        shutil.rmtree(bridge_destination)
    shutil.copytree(bridge_source, bridge_destination)

    patch_main_delegate(source / "chrome" / "app" / "chrome_main_delegate.cc")
    patch_chrome_build(source / "chrome" / "BUILD.gn")
    remove_legacy_child_launcher_hook(
        source / "content" / "browser" / "child_process_launcher_helper_win.cc"
    )
    patch_child_launcher(
        source / "content" / "browser" / "child_process_launcher_helper.cc"
    )
    patch_content_browser_build(source / "content" / "browser" / "BUILD.gn")
    patch_blink_event_target(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "dom"
        / "events"
        / "event_target.cc"
    )
    patch_blink_event_dispatcher(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "dom"
        / "events"
        / "event_dispatcher.cc"
    )
    patch_blink_dom_timer(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "scheduler"
        / "dom_timer.cc"
    )
    patch_blink_animation_frame_callbacks(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "dom"
        / "frame_request_callback_collection.cc"
    )
    patch_blink_idle_callbacks(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "scheduler"
        / "scripted_idle_task_controller.cc"
    )
    patch_blink_core_build(
        source / "third_party" / "blink" / "renderer" / "core" / "BUILD.gn"
    )
    scheduler = (
        source / "third_party" / "blink" / "renderer" / "platform"
        / "scheduler"
    )
    patch_blink_task_queue_throttler_header(
        scheduler / "common" / "throttling" / "task_queue_throttler.h"
    )
    patch_blink_task_queue_throttler(
        scheduler / "common" / "throttling" / "task_queue_throttler.cc"
    )
    patch_blink_main_thread_task_queue(
        scheduler / "main_thread" / "main_thread_task_queue.cc"
    )
    patch_blink_frame_scheduler_header(
        scheduler / "main_thread" / "frame_scheduler_impl.h"
    )
    patch_blink_scheduler_build(scheduler / "BUILD.gn")
    print(f"Recorder bridge installed in {source}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError) as error:
        print(f"Integration failed: {error}", file=sys.stderr)
        raise SystemExit(1)
