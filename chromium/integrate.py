#!/usr/bin/env python3
"""Installs the recorder bridge and startup hook into a Chromium checkout."""

from __future__ import annotations

import argparse
import re
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
CONTENT_NAVIGATION_INCLUDE = (
    '#include "chromium/recorder_bridge/browser_bridge.h"'
)
CONTENT_RENDERER_ACCESSIBILITY_INCLUDE = (
    '#include "chromium/recorder_bridge/browser_bridge.h"'
)
CONTENT_RENDERER_DEP = '    "//chromium/recorder_bridge",'
CONTENT_RENDERER_AX_ENUM_INCLUDE = (
    '#include "ui/accessibility/ax_enum_util.h"'
)
LEGACY_CONTENT_RENDERER_FOCUSED_EXPRESSION = (
    "recorder_node.HasState(ax::mojom::State::kFocused)"
)
LEGACY_CONTENT_RENDERER_ROLE_EXPRESSION = (
    "            static_cast<int>(recorder_node.role),\n"
    "            recorder_node.GetStringAttribute(\n"
)
CONTENT_RENDERER_ROLE_EXPRESSION = (
    "            static_cast<int>(recorder_node.role),\n"
    "            ui::ToString(recorder_node.role),\n"
    "            recorder_node.GetStringAttribute(\n"
)
CONTENT_RENDERER_FOCUSED_EXPRESSION = (
    "recorder_update.has_tree_data &&\n"
    "                recorder_node.id == recorder_update.tree_data.focus_id"
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
BRIDGE_FAILURE_RETURN = (
    "    return a11y_recorder::kBridgeInitializationFailureExitCode;"
)
PROTOCOL_VERSION_QUERY_RETURN = (
    "    return a11y_recorder::kProtocolVersionQueryExitCode;"
)
# The query is answered after the error declaration so that every hook body,
# current or historical, still opens with BRIDGE_HOOK_ANCHOR. Region detection
# depends on that text matching both what earlier revisions wrote and what this
# revision writes, so the anchor must not move.
HOOK = f"""\
#if BUILDFLAG(IS_WIN)
  std::string recorder_bridge_error;
  if (a11y_recorder::WriteProtocolVersionIfRequested()) {{
{PROTOCOL_VERSION_QUERY_RETURN}
  }}
  if (!a11y_recorder::InitializeProcessBridge(
          &recorder_bridge_error)) {{
    a11y_recorder::WriteRecorderBridgeDiagnostic(
        "Recorder process bridge initialization failed: " +
        recorder_bridge_error);
    LOG(ERROR) << "Windows A11y Recorder bridge failed: "
               << recorder_bridge_error;
{BRIDGE_FAILURE_RETURN}
  }}
#endif
"""
# Every bridge hook body, current or historical, opens with this text. The hook
# is migrated by rewriting the whole region it introduces, so a checkout patched
# by any earlier revision converges on the current body without this script
# having to carry a verbatim copy of each shape it ever wrote.
BRIDGE_HOOK_ANCHOR = (
    "#if BUILDFLAG(IS_WIN)\n  std::string recorder_bridge_error;\n"
)
BRIDGE_HOOK_TERMINATOR = "#endif\n"
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
LEGACY_CONTENT_NAVIGATION_STARTED_HOOK = """\
  if (navigation_handle->IsInPrimaryMainFrame()) {
    a11y_recorder::RecordBrowserNavigationStarted(
        navigation_handle->GetNavigationId(),
        navigation_handle->GetFrameTreeNodeId().GetUnsafeValue(),
        navigation_handle->GetURL().spec(),
        navigation_handle->IsRendererInitiated(),
        navigation_handle->IsSameDocument());
  }
"""
LEGACY_CONTENT_NAVIGATION_COMPLETED_HOOK = """\
  if (navigation_handle->IsInPrimaryMainFrame()) {
    int64_t recorder_document_navigation_id = 0;
    if (navigation_handle->HasCommitted()) {
      if (RenderFrameHost* recorder_frame =
              navigation_handle->GetRenderFrameHost()) {
        recorder_document_navigation_id = recorder_frame->GetNavigationId();
      }
    }
    a11y_recorder::RecordBrowserNavigationCompleted(
        navigation_handle->GetNavigationId(),
        navigation_handle->GetFrameTreeNodeId().GetUnsafeValue(),
        recorder_document_navigation_id,
        navigation_handle->GetURL().spec(),
        navigation_handle->IsRendererInitiated(),
        navigation_handle->IsSameDocument(),
        navigation_handle->HasCommitted(),
        navigation_handle->HasCommitted() &&
            navigation_handle->IsErrorPage(),
        navigation_handle->GetNetErrorCode());
  }
"""
CONTENT_NAVIGATION_STARTED_HOOK = """\
  int recorder_page_frame_tree_node_id =
      navigation_handle->GetFrameTreeNodeId().GetUnsafeValue();
  int recorder_parent_frame_tree_node_id = -1;
  int recorder_parent_or_outer_document_frame_tree_node_id = -1;
  const char* recorder_frame_type = "subframe";
  switch (navigation_handle->GetNavigatingFrameType()) {
    case FrameType::kSubframe:
      recorder_frame_type = "subframe";
      break;
    case FrameType::kPrimaryMainFrame:
      recorder_frame_type = "primary-main-frame";
      break;
    case FrameType::kPrerenderMainFrame:
      recorder_frame_type = "prerender-main-frame";
      break;
    case FrameType::kFencedFrameRoot:
      recorder_frame_type = "fenced-frame-root";
      break;
    case FrameType::kGuestMainFrame:
      recorder_frame_type = "guest-main-frame";
      break;
  }
  bool recorder_primary_page = navigation_handle->IsInPrimaryMainFrame();
  if (RenderFrameHost* recorder_parent =
          navigation_handle->GetParentFrame()) {
    recorder_parent_frame_tree_node_id =
        recorder_parent->GetFrameTreeNodeId().GetUnsafeValue();
    if (RenderFrameHost* recorder_main_frame =
            recorder_parent->GetMainFrame()) {
      recorder_page_frame_tree_node_id =
          recorder_main_frame->GetFrameTreeNodeId().GetUnsafeValue();
      recorder_primary_page = recorder_main_frame->IsInPrimaryMainFrame();
    }
  }
  if (RenderFrameHost* recorder_owner =
          navigation_handle->GetParentFrameOrOuterDocument()) {
    recorder_parent_or_outer_document_frame_tree_node_id =
        recorder_owner->GetFrameTreeNodeId().GetUnsafeValue();
  }
  a11y_recorder::RecordBrowserNavigationStarted(
      navigation_handle->GetNavigationId(),
      recorder_page_frame_tree_node_id,
      navigation_handle->GetFrameTreeNodeId().GetUnsafeValue(),
      recorder_parent_frame_tree_node_id,
      recorder_parent_or_outer_document_frame_tree_node_id,
      recorder_frame_type,
      recorder_primary_page,
      navigation_handle->GetURL().spec(),
      navigation_handle->IsRendererInitiated(),
      navigation_handle->IsSameDocument());
"""
INTERMEDIATE_CONTENT_NAVIGATION_COMPLETED_HOOK = """\
  int recorder_page_frame_tree_node_id =
      navigation_handle->GetFrameTreeNodeId().GetUnsafeValue();
  int recorder_parent_frame_tree_node_id = -1;
  int recorder_parent_or_outer_document_frame_tree_node_id = -1;
  const char* recorder_frame_type = "subframe";
  switch (navigation_handle->GetNavigatingFrameType()) {
    case FrameType::kSubframe:
      recorder_frame_type = "subframe";
      break;
    case FrameType::kPrimaryMainFrame:
      recorder_frame_type = "primary-main-frame";
      break;
    case FrameType::kPrerenderMainFrame:
      recorder_frame_type = "prerender-main-frame";
      break;
    case FrameType::kFencedFrameRoot:
      recorder_frame_type = "fenced-frame-root";
      break;
    case FrameType::kGuestMainFrame:
      recorder_frame_type = "guest-main-frame";
      break;
  }
  bool recorder_primary_page = navigation_handle->IsInPrimaryMainFrame();
  if (RenderFrameHost* recorder_parent =
          navigation_handle->GetParentFrame()) {
    recorder_parent_frame_tree_node_id =
        recorder_parent->GetFrameTreeNodeId().GetUnsafeValue();
    if (RenderFrameHost* recorder_main_frame =
            recorder_parent->GetMainFrame()) {
      recorder_page_frame_tree_node_id =
          recorder_main_frame->GetFrameTreeNodeId().GetUnsafeValue();
      recorder_primary_page = recorder_main_frame->IsInPrimaryMainFrame();
    }
  }
  if (RenderFrameHost* recorder_owner =
          navigation_handle->GetParentFrameOrOuterDocument()) {
    recorder_parent_or_outer_document_frame_tree_node_id =
        recorder_owner->GetFrameTreeNodeId().GetUnsafeValue();
  }
  int64_t recorder_document_navigation_id = 0;
  if (navigation_handle->HasCommitted()) {
    if (RenderFrameHost* recorder_frame =
            navigation_handle->GetRenderFrameHost()) {
      recorder_document_navigation_id = recorder_frame->GetNavigationId();
    }
  }
  a11y_recorder::RecordBrowserNavigationCompleted(
      navigation_handle->GetNavigationId(),
      recorder_page_frame_tree_node_id,
      navigation_handle->GetFrameTreeNodeId().GetUnsafeValue(),
      recorder_parent_frame_tree_node_id,
      recorder_parent_or_outer_document_frame_tree_node_id,
      recorder_frame_type,
      recorder_primary_page,
      recorder_document_navigation_id,
      navigation_handle->GetURL().spec(),
      navigation_handle->IsRendererInitiated(),
      navigation_handle->IsSameDocument(),
      navigation_handle->HasCommitted(),
      navigation_handle->HasCommitted() &&
          navigation_handle->IsErrorPage(),
      navigation_handle->GetNetErrorCode());
"""
CONTENT_NAVIGATION_COMPLETED_HOOK = """\
  int recorder_page_frame_tree_node_id =
      navigation_handle->GetFrameTreeNodeId().GetUnsafeValue();
  int recorder_parent_frame_tree_node_id = -1;
  int recorder_parent_or_outer_document_frame_tree_node_id = -1;
  const char* recorder_frame_type = "subframe";
  switch (navigation_handle->GetNavigatingFrameType()) {
    case FrameType::kSubframe:
      recorder_frame_type = "subframe";
      break;
    case FrameType::kPrimaryMainFrame:
      recorder_frame_type = "primary-main-frame";
      break;
    case FrameType::kPrerenderMainFrame:
      recorder_frame_type = "prerender-main-frame";
      break;
    case FrameType::kFencedFrameRoot:
      recorder_frame_type = "fenced-frame-root";
      break;
    case FrameType::kGuestMainFrame:
      recorder_frame_type = "guest-main-frame";
      break;
  }
  bool recorder_primary_page = navigation_handle->IsInPrimaryMainFrame();
  if (RenderFrameHost* recorder_parent =
          navigation_handle->GetParentFrame()) {
    recorder_parent_frame_tree_node_id =
        recorder_parent->GetFrameTreeNodeId().GetUnsafeValue();
    if (RenderFrameHost* recorder_main_frame =
            recorder_parent->GetMainFrame()) {
      recorder_page_frame_tree_node_id =
          recorder_main_frame->GetFrameTreeNodeId().GetUnsafeValue();
      recorder_primary_page = recorder_main_frame->IsInPrimaryMainFrame();
    }
  }
  if (RenderFrameHost* recorder_owner =
          navigation_handle->GetParentFrameOrOuterDocument()) {
    recorder_parent_or_outer_document_frame_tree_node_id =
        recorder_owner->GetFrameTreeNodeId().GetUnsafeValue();
  }
  int64_t recorder_document_navigation_id = 0;
  std::string recorder_document_token;
  int recorder_renderer_process_id = 0;
  if (navigation_handle->HasCommitted()) {
    if (RenderFrameHost* recorder_frame =
            navigation_handle->GetRenderFrameHost()) {
      recorder_document_navigation_id = recorder_frame->GetNavigationId();
      recorder_document_token =
          static_cast<RenderFrameHostImpl*>(recorder_frame)
              ->GetDocumentToken()
              .ToString();
      recorder_renderer_process_id = static_cast<int>(
          recorder_frame->GetProcess()->GetProcess().Pid());
    }
  }
  a11y_recorder::RecordBrowserNavigationCompleted(
      navigation_handle->GetNavigationId(),
      recorder_page_frame_tree_node_id,
      navigation_handle->GetFrameTreeNodeId().GetUnsafeValue(),
      recorder_parent_frame_tree_node_id,
      recorder_parent_or_outer_document_frame_tree_node_id,
      recorder_frame_type,
      recorder_primary_page,
      recorder_document_navigation_id,
      recorder_document_token,
      recorder_renderer_process_id,
      navigation_handle->GetURL().spec(),
      navigation_handle->IsRendererInitiated(),
      navigation_handle->IsSameDocument(),
      navigation_handle->HasCommitted(),
      navigation_handle->HasCommitted() &&
          navigation_handle->IsErrorPage(),
      navigation_handle->GetNetErrorCode());
"""
CONTENT_RENDERER_ACCESSIBILITY_HOOK = """\
  constexpr int kRecorderMaximumAccessibilityCheckpointNodes = 100000;
  const std::string recorder_document_token =
      document.Token().ToString();
  const int recorder_update_count =
      static_cast<int>(updates_and_events.updates.size());
  const int recorder_event_count =
      static_cast<int>(updates_and_events.events.size());
  const uint64_t recorder_checkpoint_sequence =
      a11y_recorder::BeginRendererAccessibilityCheckpoint(
          recorder_document_token, "renderer-serialization",
          kRecorderMaximumAccessibilityCheckpointNodes,
          recorder_update_count, recorder_event_count);
  if (recorder_checkpoint_sequence != 0) {
    std::unordered_map<int32_t, int32_t> recorder_parent_ids;
    for (const ui::AXTreeUpdate& recorder_update :
         updates_and_events.updates) {
      for (const ui::AXNodeData& recorder_node : recorder_update.nodes) {
        for (int32_t recorder_child_id : recorder_node.child_ids) {
          recorder_parent_ids.insert_or_assign(
              recorder_child_id, recorder_node.id);
        }
      }
    }
    int recorder_node_count = 0;
    bool recorder_truncated = false;
    for (const ui::AXTreeUpdate& recorder_update :
         updates_and_events.updates) {
      for (const ui::AXNodeData& recorder_node : recorder_update.nodes) {
        if (recorder_node_count >=
            kRecorderMaximumAccessibilityCheckpointNodes) {
          recorder_truncated = true;
          break;
        }
        const auto recorder_parent =
            recorder_parent_ids.find(recorder_node.id);
        a11y_recorder::RecordRendererAccessibilityCheckpointNode(
            recorder_checkpoint_sequence, recorder_document_token,
            recorder_node_count, recorder_node.id,
            recorder_parent == recorder_parent_ids.end()
                ? 0
                : recorder_parent->second,
            recorder_node.GetDOMNodeId(),
            static_cast<int>(recorder_node.role),
            ui::ToString(recorder_node.role),
            recorder_node.GetStringAttribute(
                ax::mojom::StringAttribute::kName),
            recorder_node.GetStringAttribute(
                ax::mojom::StringAttribute::kDescription),
            recorder_node.ToString(false),
            recorder_update.has_tree_data &&
                recorder_node.id == recorder_update.tree_data.focus_id);
        ++recorder_node_count;
      }
      if (recorder_truncated)
        break;
    }
    a11y_recorder::CompleteRendererAccessibilityCheckpoint(
        recorder_checkpoint_sequence, recorder_document_token,
        "renderer-serialization", recorder_node_count,
        recorder_truncated,
        kRecorderMaximumAccessibilityCheckpointNodes,
        recorder_update_count, recorder_event_count);
  }
"""
ORIGINAL_BLINK_DOM_CHECKPOINT_HOOK = """\
  constexpr int kRecorderMaximumDomCheckpointNodes = 512;
  const int recorder_document_node_id = GetDomNodeId();
  const uint64_t recorder_checkpoint_sequence =
      a11y_recorder::BeginBlinkDomCheckpoint(
          recorder_document_node_id, "finished-parsing",
          kRecorderMaximumDomCheckpointNodes);
  if (recorder_checkpoint_sequence != 0) {
    int recorder_node_count = 0;
    bool recorder_truncated = false;
    for (Node& recorder_node :
         NodeTraversal::InclusiveDescendantsOf(*this)) {
      if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
        recorder_truncated = true;
        break;
      }
      ContainerNode* recorder_parent = recorder_node.parentNode();
      a11y_recorder::RecordBlinkDomCheckpointNode(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_node_count, recorder_node.GetDomNodeId(),
          recorder_parent ? recorder_parent->GetDomNodeId() : 0,
          static_cast<int>(recorder_node.getNodeType()),
          recorder_node.nodeName().Utf8().c_str());
      ++recorder_node_count;
    }
    a11y_recorder::CompleteBlinkDomCheckpoint(
        recorder_checkpoint_sequence, recorder_document_node_id,
        "finished-parsing", recorder_node_count, recorder_truncated,
        kRecorderMaximumDomCheckpointNodes);
  }
"""
LEGACY_BLINK_DOM_CHECKPOINT_HOOK = """\
  constexpr int kRecorderMaximumDomCheckpointNodes = 512;
  const int recorder_document_node_id = GetDomNodeId();
  const std::string recorder_document_token = Token().ToString();
  const uint64_t recorder_checkpoint_sequence =
      a11y_recorder::BeginBlinkDomCheckpoint(
          recorder_document_node_id, recorder_document_token,
          "finished-parsing",
          kRecorderMaximumDomCheckpointNodes);
  if (recorder_checkpoint_sequence != 0) {
    int recorder_node_count = 0;
    bool recorder_truncated = false;
    for (Node& recorder_node :
         NodeTraversal::InclusiveDescendantsOf(*this)) {
      if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
        recorder_truncated = true;
        break;
      }
      ContainerNode* recorder_parent = recorder_node.parentNode();
      a11y_recorder::RecordBlinkDomCheckpointNode(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token,
          recorder_node_count, recorder_node.GetDomNodeId(),
          recorder_parent ? recorder_parent->GetDomNodeId() : 0,
          static_cast<int>(recorder_node.getNodeType()),
          recorder_node.nodeName().Utf8().c_str());
      ++recorder_node_count;
    }
    a11y_recorder::CompleteBlinkDomCheckpoint(
        recorder_checkpoint_sequence, recorder_document_node_id,
        recorder_document_token,
        "finished-parsing", recorder_node_count, recorder_truncated,
        kRecorderMaximumDomCheckpointNodes);
  }
"""
INTERMEDIATE_BLINK_DOM_CHECKPOINT_HOOK = """\
  constexpr int kRecorderMaximumDomCheckpointNodes = 512;
  constexpr int kRecorderMaximumDomAttributesPerNode = 64;
  constexpr int kRecorderMaximumDomValueLength = 4096;
  const int recorder_document_node_id = GetDomNodeId();
  const std::string recorder_document_token = Token().ToString();
  const uint64_t recorder_checkpoint_sequence =
      a11y_recorder::BeginBlinkDomCheckpoint(
          recorder_document_node_id, recorder_document_token,
          "finished-parsing",
          kRecorderMaximumDomCheckpointNodes);
  if (recorder_checkpoint_sequence != 0) {
    int recorder_node_count = 0;
    bool recorder_truncated = false;
    int recorder_attribute_count = 0;
    bool recorder_attributes_truncated = false;
    for (Node& recorder_node :
         NodeTraversal::InclusiveDescendantsOf(*this)) {
      if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
        recorder_truncated = true;
        break;
      }
      ContainerNode* recorder_parent = recorder_node.parentNode();
      a11y_recorder::RecordBlinkDomCheckpointNode(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token,
          recorder_node_count, recorder_node.GetDomNodeId(),
          recorder_parent ? recorder_parent->GetDomNodeId() : 0,
          static_cast<int>(recorder_node.getNodeType()),
          recorder_node.nodeName().Utf8().c_str());
      ++recorder_node_count;
      Element* recorder_element = DynamicTo<Element>(recorder_node);
      if (!recorder_element)
        continue;
      int recorder_node_attribute_index = 0;
      for (const Attribute& recorder_attribute :
           recorder_element->Attributes()) {
        if (recorder_node_attribute_index >=
            kRecorderMaximumDomAttributesPerNode) {
          recorder_attributes_truncated = true;
          break;
        }
        const String recorder_attribute_value = recorder_attribute.Value();
        const int recorder_attribute_value_length =
            static_cast<int>(recorder_attribute_value.length());
        const bool recorder_attribute_value_truncated =
            recorder_attribute_value_length >
            kRecorderMaximumDomValueLength;
        const String recorder_recorded_attribute_value =
            recorder_attribute_value_truncated
                ? recorder_attribute_value.Left(
                      kRecorderMaximumDomValueLength)
                : recorder_attribute_value;
        a11y_recorder::RecordBlinkDomCheckpointNodeAttribute(
            recorder_checkpoint_sequence, recorder_document_node_id,
            recorder_document_token, recorder_node.GetDomNodeId(),
            recorder_node_attribute_index,
            recorder_attribute.NamespaceURI().Utf8().c_str(),
            recorder_attribute.LocalName().Utf8().c_str(),
            recorder_recorded_attribute_value.Utf8().c_str(),
            recorder_attribute_value_length,
            recorder_attribute_value_truncated,
            kRecorderMaximumDomValueLength);
        ++recorder_node_attribute_index;
        ++recorder_attribute_count;
      }
    }
    a11y_recorder::CompleteBlinkDomCheckpoint(
        recorder_checkpoint_sequence, recorder_document_node_id,
        recorder_document_token,
        "finished-parsing", recorder_node_count, recorder_truncated,
        kRecorderMaximumDomCheckpointNodes,
        recorder_attribute_count,
        recorder_attributes_truncated,
        kRecorderMaximumDomAttributesPerNode,
        kRecorderMaximumDomValueLength);
  }
"""

LEGACY_LIGHT_TREE_BLINK_DOM_CHECKPOINT_HOOK = """\
  constexpr int kRecorderMaximumDomCheckpointNodes = 512;
  constexpr int kRecorderMaximumDomAttributesPerNode = 64;
  constexpr int kRecorderMaximumDomValueLength = 4096;
  const int recorder_document_node_id = GetDomNodeId();
  const std::string recorder_document_token = Token().ToString();
  const uint64_t recorder_checkpoint_sequence =
      a11y_recorder::BeginBlinkDomCheckpoint(
          recorder_document_node_id, recorder_document_token,
          "finished-parsing",
          kRecorderMaximumDomCheckpointNodes);
  if (recorder_checkpoint_sequence != 0) {
    int recorder_node_count = 0;
    bool recorder_truncated = false;
    int recorder_attribute_count = 0;
    bool recorder_attributes_truncated = false;
    for (Node& recorder_node :
         NodeTraversal::InclusiveDescendantsOf(*this)) {
      if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
        recorder_truncated = true;
        break;
      }
      ContainerNode* recorder_parent = recorder_node.parentNode();
      a11y_recorder::RecordBlinkDomCheckpointNode(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token,
          recorder_node_count, recorder_node.GetDomNodeId(),
          recorder_parent ? recorder_parent->GetDomNodeId() : 0,
          static_cast<int>(recorder_node.getNodeType()),
          recorder_node.nodeName().Utf8().c_str());
      ++recorder_node_count;
      Element* recorder_element = DynamicTo<Element>(recorder_node);
      if (!recorder_element)
        continue;
      int recorder_node_attribute_index = 0;
      for (const Attribute& recorder_attribute :
           recorder_element->Attributes()) {
        if (recorder_node_attribute_index >=
            kRecorderMaximumDomAttributesPerNode) {
          recorder_attributes_truncated = true;
          break;
        }
        const String recorder_attribute_value = recorder_attribute.Value();
        const int recorder_attribute_value_length =
            static_cast<int>(recorder_attribute_value.length());
        const bool recorder_attribute_value_truncated =
            recorder_attribute_value_length >
            kRecorderMaximumDomValueLength;
        const String recorder_recorded_attribute_value =
            recorder_attribute_value_truncated
                ? recorder_attribute_value.substr(
                      0, kRecorderMaximumDomValueLength)
                : recorder_attribute_value;
        a11y_recorder::RecordBlinkDomCheckpointNodeAttribute(
            recorder_checkpoint_sequence, recorder_document_node_id,
            recorder_document_token, recorder_node.GetDomNodeId(),
            recorder_node_attribute_index,
            recorder_attribute.NamespaceURI().Utf8().c_str(),
            recorder_attribute.LocalName().Utf8().c_str(),
            recorder_recorded_attribute_value.Utf8().c_str(),
            recorder_attribute_value_length,
            recorder_attribute_value_truncated,
            kRecorderMaximumDomValueLength);
        ++recorder_node_attribute_index;
        ++recorder_attribute_count;
      }
    }
    a11y_recorder::CompleteBlinkDomCheckpoint(
        recorder_checkpoint_sequence, recorder_document_node_id,
        recorder_document_token,
        "finished-parsing", recorder_node_count, recorder_truncated,
        kRecorderMaximumDomCheckpointNodes,
        recorder_attribute_count,
        recorder_attributes_truncated,
        kRecorderMaximumDomAttributesPerNode,
        kRecorderMaximumDomValueLength);
  }
"""
BLINK_DOM_CHECKPOINT_HELPER_MARKER = (
    "void RecorderRecordDomCheckpoint(Document& recorder_document,"
)
LEGACY_SHADOW_TREE_BLINK_DOM_CHECKPOINT_HELPER = """\
// Declared before its definition so the definition has a prior declaration.
// The mutation observer declares it as well, so the finished-parsing and
// post-mutation checkpoints share one traversal.
void RecorderRecordDomCheckpoint(Document& recorder_document,
                                 const char* recorder_reason);

// Records one bounded structural checkpoint of the composed tree. Each node is
// followed by the shadow root it hosts, of any mode, and that shadow tree, and
// then by its own children, so a shadow root is recorded with its host as its
// parent. Slot assignments are read as Blink currently holds them; the
// traversal never requests an assignment recalculation, so recording does not
// change the state it records.
void RecorderRecordDomCheckpoint(Document& recorder_document,
                                 const char* recorder_reason) {
  constexpr int kRecorderMaximumDomCheckpointNodes = 512;
  constexpr int kRecorderMaximumDomAttributesPerNode = 64;
  constexpr int kRecorderMaximumDomValueLength = 4096;
  const int recorder_document_node_id = recorder_document.GetDomNodeId();
  const std::string recorder_document_token =
      recorder_document.Token().ToString();
  const uint64_t recorder_checkpoint_sequence =
      a11y_recorder::BeginBlinkDomCheckpoint(
          recorder_document_node_id, recorder_document_token,
          recorder_reason, kRecorderMaximumDomCheckpointNodes);
  if (recorder_checkpoint_sequence == 0) {
    return;
  }
  int recorder_node_count = 0;
  bool recorder_truncated = false;
  int recorder_attribute_count = 0;
  bool recorder_attributes_truncated = false;
  int recorder_shadow_root_count = 0;
  int recorder_slot_count = 0;
  HeapVector<Member<Node>> recorder_pending;
  recorder_pending.push_back(&recorder_document);
  while (!recorder_pending.empty()) {
    Node& recorder_node = *recorder_pending.back();
    recorder_pending.pop_back();
    if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
      recorder_truncated = true;
      break;
    }
    ContainerNode* recorder_parent = recorder_node.ParentOrShadowHostNode();
    a11y_recorder::RecordBlinkDomCheckpointNode(
        recorder_checkpoint_sequence, recorder_document_node_id,
        recorder_document_token,
        recorder_node_count, recorder_node.GetDomNodeId(),
        recorder_parent ? recorder_parent->GetDomNodeId() : 0,
        static_cast<int>(recorder_node.getNodeType()),
        recorder_node.nodeName().Utf8().c_str());
    ++recorder_node_count;
    for (Node* recorder_child = recorder_node.lastChild(); recorder_child;
         recorder_child = recorder_child->previousSibling()) {
      recorder_pending.push_back(recorder_child);
    }
    if (auto* recorder_shadow_root = DynamicTo<ShadowRoot>(recorder_node)) {
      ++recorder_shadow_root_count;
      const ShadowRootMode recorder_mode = recorder_shadow_root->GetMode();
      const AtomicString& recorder_reference_target =
          recorder_shadow_root->referenceTarget();
      a11y_recorder::RecordBlinkDomCheckpointShadowRoot(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token, recorder_shadow_root->GetDomNodeId(),
          recorder_shadow_root->host().GetDomNodeId(),
          recorder_mode == ShadowRootMode::kOpen     ? "open"
          : recorder_mode == ShadowRootMode::kClosed ? "closed"
                                                     : "user-agent",
          recorder_shadow_root->delegatesFocus(),
          recorder_shadow_root->IsManualSlotting() ? "manual" : "named",
          recorder_shadow_root->clonable(),
          recorder_shadow_root->serializable(),
          recorder_shadow_root->IsDeclarativeShadowRoot(),
          recorder_shadow_root->IsAvailableToElementInternals(),
          !recorder_reference_target.IsNull(),
          recorder_reference_target.IsNull()
              ? std::string()
              : recorder_reference_target.Utf8());
    }
    Element* recorder_element = DynamicTo<Element>(recorder_node);
    if (!recorder_element) {
      continue;
    }
    if (ShadowRoot* recorder_hosted_root = recorder_element->GetShadowRoot()) {
      recorder_pending.push_back(recorder_hosted_root);
    }
    int recorder_node_attribute_index = 0;
    for (const Attribute& recorder_attribute :
         recorder_element->Attributes()) {
      if (recorder_node_attribute_index >=
          kRecorderMaximumDomAttributesPerNode) {
        recorder_attributes_truncated = true;
        break;
      }
      const String recorder_attribute_value = recorder_attribute.Value();
      const int recorder_attribute_value_length =
          static_cast<int>(recorder_attribute_value.length());
      const bool recorder_attribute_value_truncated =
          recorder_attribute_value_length > kRecorderMaximumDomValueLength;
      const String recorder_recorded_attribute_value =
          recorder_attribute_value_truncated
              ? recorder_attribute_value.substr(
                    0, kRecorderMaximumDomValueLength)
              : recorder_attribute_value;
      a11y_recorder::RecordBlinkDomCheckpointNodeAttribute(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token, recorder_node.GetDomNodeId(),
          recorder_node_attribute_index,
          recorder_attribute.NamespaceURI().Utf8().c_str(),
          recorder_attribute.LocalName().Utf8().c_str(),
          recorder_recorded_attribute_value.Utf8().c_str(),
          recorder_attribute_value_length,
          recorder_attribute_value_truncated,
          kRecorderMaximumDomValueLength);
      ++recorder_node_attribute_index;
      ++recorder_attribute_count;
    }
    auto* recorder_slot = DynamicTo<HTMLSlotElement>(recorder_element);
    ShadowRoot* recorder_slot_root =
        recorder_slot ? recorder_slot->ContainingShadowRoot() : nullptr;
    if (recorder_slot_root && recorder_slot->SupportsAssignment()) {
      ++recorder_slot_count;
      const HeapVector<Member<Node>>& recorder_assigned =
          recorder_slot->AssignedNodesNoRecalc();
      std::vector<int> recorder_assigned_ids;
      for (const Member<Node>& recorder_assigned_node : recorder_assigned) {
        if (static_cast<int>(recorder_assigned_ids.size()) >=
            kRecorderMaximumDomCheckpointNodes) {
          break;
        }
        recorder_assigned_ids.push_back(
            recorder_assigned_node->GetDomNodeId());
      }
      a11y_recorder::RecordBlinkDomCheckpointSlotAssignment(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token, recorder_slot->GetDomNodeId(),
          std::move(recorder_assigned_ids),
          static_cast<int>(recorder_assigned.size()),
          kRecorderMaximumDomCheckpointNodes,
          !recorder_slot_root->GetSlotAssignment().NeedsAssignmentRecalc());
    }
  }
  a11y_recorder::CompleteBlinkDomCheckpoint(
      recorder_checkpoint_sequence, recorder_document_node_id,
      recorder_document_token, recorder_reason, recorder_node_count,
      recorder_truncated, kRecorderMaximumDomCheckpointNodes,
      recorder_attribute_count, recorder_attributes_truncated,
      kRecorderMaximumDomAttributesPerNode, kRecorderMaximumDomValueLength,
      recorder_shadow_root_count, recorder_slot_count);
}

"""
BLINK_DOM_CHECKPOINT_HELPER = """\
// Declared before its definition so the definition has a prior declaration.
// The mutation observer declares it as well, so the finished-parsing and
// post-mutation checkpoints share one traversal.
void RecorderRecordDomCheckpoint(Document& recorder_document,
                                 const char* recorder_reason);

// Defined further down this file; records the interaction state immediately
// after the DOM checkpoint it follows.
void RecorderRecordInteractionCheckpoint(Document& recorder_document,
                                         uint64_t recorder_source_sequence,
                                         const char* recorder_source_channel,
                                         const char* recorder_reason);

// Records one bounded structural checkpoint of the composed tree. Each node is
// followed by the shadow root it hosts, of any mode, and that shadow tree, and
// then by its own children, so a shadow root is recorded with its host as its
// parent. Slot assignments are read as Blink currently holds them; the
// traversal never requests an assignment recalculation, so recording does not
// change the state it records.
void RecorderRecordDomCheckpoint(Document& recorder_document,
                                 const char* recorder_reason) {
  constexpr int kRecorderMaximumDomCheckpointNodes = 512;
  constexpr int kRecorderMaximumDomAttributesPerNode = 64;
  constexpr int kRecorderMaximumDomValueLength = 4096;
  const int recorder_document_node_id = recorder_document.GetDomNodeId();
  const std::string recorder_document_token =
      recorder_document.Token().ToString();
  const uint64_t recorder_checkpoint_sequence =
      a11y_recorder::BeginBlinkDomCheckpoint(
          recorder_document_node_id, recorder_document_token,
          recorder_reason, kRecorderMaximumDomCheckpointNodes);
  if (recorder_checkpoint_sequence == 0) {
    return;
  }
  int recorder_node_count = 0;
  bool recorder_truncated = false;
  int recorder_attribute_count = 0;
  bool recorder_attributes_truncated = false;
  int recorder_shadow_root_count = 0;
  int recorder_slot_count = 0;
  HeapVector<Member<Node>> recorder_pending;
  recorder_pending.push_back(&recorder_document);
  while (!recorder_pending.empty()) {
    Node& recorder_node = *recorder_pending.back();
    recorder_pending.pop_back();
    if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
      recorder_truncated = true;
      break;
    }
    ContainerNode* recorder_parent = recorder_node.ParentOrShadowHostNode();
    a11y_recorder::RecordBlinkDomCheckpointNode(
        recorder_checkpoint_sequence, recorder_document_node_id,
        recorder_document_token,
        recorder_node_count, recorder_node.GetDomNodeId(),
        recorder_parent ? recorder_parent->GetDomNodeId() : 0,
        static_cast<int>(recorder_node.getNodeType()),
        recorder_node.nodeName().Utf8().c_str());
    ++recorder_node_count;
    for (Node* recorder_child = recorder_node.lastChild(); recorder_child;
         recorder_child = recorder_child->previousSibling()) {
      recorder_pending.push_back(recorder_child);
    }
    if (auto* recorder_shadow_root = DynamicTo<ShadowRoot>(recorder_node)) {
      ++recorder_shadow_root_count;
      const ShadowRootMode recorder_mode = recorder_shadow_root->GetMode();
      const AtomicString& recorder_reference_target =
          recorder_shadow_root->referenceTarget();
      a11y_recorder::RecordBlinkDomCheckpointShadowRoot(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token, recorder_shadow_root->GetDomNodeId(),
          recorder_shadow_root->host().GetDomNodeId(),
          recorder_mode == ShadowRootMode::kOpen     ? "open"
          : recorder_mode == ShadowRootMode::kClosed ? "closed"
                                                     : "user-agent",
          recorder_shadow_root->delegatesFocus(),
          recorder_shadow_root->IsManualSlotting() ? "manual" : "named",
          recorder_shadow_root->clonable(),
          recorder_shadow_root->serializable(),
          recorder_shadow_root->IsDeclarativeShadowRoot(),
          recorder_shadow_root->IsAvailableToElementInternals(),
          !recorder_reference_target.IsNull(),
          recorder_reference_target.IsNull()
              ? std::string()
              : recorder_reference_target.Utf8());
    }
    Element* recorder_element = DynamicTo<Element>(recorder_node);
    if (!recorder_element) {
      continue;
    }
    if (ShadowRoot* recorder_hosted_root = recorder_element->GetShadowRoot()) {
      recorder_pending.push_back(recorder_hosted_root);
    }
    int recorder_node_attribute_index = 0;
    for (const Attribute& recorder_attribute :
         recorder_element->Attributes()) {
      if (recorder_node_attribute_index >=
          kRecorderMaximumDomAttributesPerNode) {
        recorder_attributes_truncated = true;
        break;
      }
      const String recorder_attribute_value = recorder_attribute.Value();
      const int recorder_attribute_value_length =
          static_cast<int>(recorder_attribute_value.length());
      const bool recorder_attribute_value_truncated =
          recorder_attribute_value_length > kRecorderMaximumDomValueLength;
      const String recorder_recorded_attribute_value =
          recorder_attribute_value_truncated
              ? recorder_attribute_value.substr(
                    0, kRecorderMaximumDomValueLength)
              : recorder_attribute_value;
      a11y_recorder::RecordBlinkDomCheckpointNodeAttribute(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token, recorder_node.GetDomNodeId(),
          recorder_node_attribute_index,
          recorder_attribute.NamespaceURI().Utf8().c_str(),
          recorder_attribute.LocalName().Utf8().c_str(),
          recorder_recorded_attribute_value.Utf8().c_str(),
          recorder_attribute_value_length,
          recorder_attribute_value_truncated,
          kRecorderMaximumDomValueLength);
      ++recorder_node_attribute_index;
      ++recorder_attribute_count;
    }
    auto* recorder_slot = DynamicTo<HTMLSlotElement>(recorder_element);
    ShadowRoot* recorder_slot_root =
        recorder_slot ? recorder_slot->ContainingShadowRoot() : nullptr;
    if (recorder_slot_root && recorder_slot->SupportsAssignment()) {
      ++recorder_slot_count;
      const HeapVector<Member<Node>>& recorder_assigned =
          recorder_slot->AssignedNodesNoRecalc();
      std::vector<int> recorder_assigned_ids;
      for (const Member<Node>& recorder_assigned_node : recorder_assigned) {
        if (static_cast<int>(recorder_assigned_ids.size()) >=
            kRecorderMaximumDomCheckpointNodes) {
          break;
        }
        recorder_assigned_ids.push_back(
            recorder_assigned_node->GetDomNodeId());
      }
      a11y_recorder::RecordBlinkDomCheckpointSlotAssignment(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token, recorder_slot->GetDomNodeId(),
          std::move(recorder_assigned_ids),
          static_cast<int>(recorder_assigned.size()),
          kRecorderMaximumDomCheckpointNodes,
          !recorder_slot_root->GetSlotAssignment().NeedsAssignmentRecalc());
    }
  }
  a11y_recorder::CompleteBlinkDomCheckpoint(
      recorder_checkpoint_sequence, recorder_document_node_id,
      recorder_document_token, recorder_reason, recorder_node_count,
      recorder_truncated, kRecorderMaximumDomCheckpointNodes,
      recorder_attribute_count, recorder_attributes_truncated,
      kRecorderMaximumDomAttributesPerNode, kRecorderMaximumDomValueLength,
      recorder_shadow_root_count, recorder_slot_count);
  RecorderRecordInteractionCheckpoint(recorder_document,
                                      recorder_checkpoint_sequence,
                                      "browser.dom", recorder_reason);
}

"""
BLINK_INTERACTION_CHECKPOINT_HELPER_MARKER = (
    "void RecorderRecordInteractionCheckpoint(Document& recorder_document,\n"
    "                                         uint64_t recorder_source_sequence,"
    "\n"
    "                                         const char* recorder_source_channel,"
    "\n"
    "                                         const char* recorder_reason) {\n"
)
BLINK_INTERACTION_CHECKPOINT_HELPER = """\
// Declared before its definition so the definition has a prior declaration.
// The layout checkpoint in local_frame_view.cc declares it as well.
void RecorderRecordInteractionCheckpoint(Document& recorder_document,
                                         uint64_t recorder_source_sequence,
                                         const char* recorder_source_channel,
                                         const char* recorder_reason);

// Records the interaction state the document holds immediately after a DOM or
// layout checkpoint completed: focus, the frame selection, and every text
// control's value and selection. Everything is read from state Blink already
// holds; nothing here requests a style recalculation, a layout, or a
// selection update, so recording does not change the state it records. Text
// controls are visited in composed-tree order, including every shadow tree,
// and their values are recorded verbatim up to the value bound, as the value
// change records are.
void RecorderRecordInteractionCheckpoint(Document& recorder_document,
                                         uint64_t recorder_source_sequence,
                                         const char* recorder_source_channel,
                                         const char* recorder_reason) {
  constexpr int kRecorderMaximumInteractionTextControls = 512;
  constexpr int kRecorderMaximumInteractionValueLength = 4096;
  const int recorder_document_node_id = recorder_document.GetDomNodeId();
  if (recorder_document_node_id <= 0 || !recorder_document.IsActive()) {
    return;
  }
  const std::string recorder_document_token =
      recorder_document.Token().ToString();
  a11y_recorder::InteractionCheckpointState recorder_state;
  recorder_state.document_has_focus = recorder_document.hasFocus();
  Element* recorder_focused = recorder_document.FocusedElement();
  if (recorder_focused) {
    recorder_state.focused_node_id = recorder_focused->GetDomNodeId();
    recorder_state.focus_visible =
        SelectorChecker::MatchesFocusVisiblePseudoClass(*recorder_focused);
    Element* recorder_active_descendant =
        recorder_focused->GetElementAttribute(
            html_names::kAriaActivedescendantAttr);
    recorder_state.active_descendant_node_id =
        recorder_active_descendant ? recorder_active_descendant->GetDomNodeId()
                                   : 0;
  }
  switch (recorder_document.LastFocusType()) {
    case mojom::blink::FocusType::kNone:
      recorder_state.last_focus_type = "none";
      break;
    case mojom::blink::FocusType::kScript:
      recorder_state.last_focus_type = "script";
      break;
    case mojom::blink::FocusType::kForward:
      recorder_state.last_focus_type = "forward";
      break;
    case mojom::blink::FocusType::kBackward:
      recorder_state.last_focus_type = "backward";
      break;
    case mojom::blink::FocusType::kSpatialNavigation:
      recorder_state.last_focus_type = "spatial-navigation";
      break;
    case mojom::blink::FocusType::kMouse:
      recorder_state.last_focus_type = "mouse";
      break;
    case mojom::blink::FocusType::kAccessKey:
      recorder_state.last_focus_type = "access-key";
      break;
    case mojom::blink::FocusType::kPage:
      recorder_state.last_focus_type = "page";
      break;
  }
  recorder_state.selection_type = "none";
  LocalFrame* recorder_frame = recorder_document.GetFrame();
  if (recorder_frame && recorder_frame->Selection().IsAvailable()) {
    const FrameSelection& recorder_frame_selection =
        recorder_frame->Selection();
    const SelectionInDomTree& recorder_selection =
        recorder_frame_selection.GetSelectionInDomTree();
    Node* recorder_anchor_node =
        recorder_selection.Anchor().ComputeContainerNode();
    Node* recorder_focus_node =
        recorder_selection.Focus().ComputeContainerNode();
    if (!recorder_selection.IsNone() && recorder_anchor_node &&
        recorder_focus_node) {
      recorder_state.selection_type =
          recorder_selection.IsCaret() ? "caret" : "range";
      recorder_state.anchor_node_id = recorder_anchor_node->GetDomNodeId();
      recorder_state.anchor_offset = static_cast<int>(
          recorder_selection.Anchor().ComputeOffsetInContainerNode());
      recorder_state.focus_node_id = recorder_focus_node->GetDomNodeId();
      recorder_state.focus_offset = static_cast<int>(
          recorder_selection.Focus().ComputeOffsetInContainerNode());
    }
    recorder_state.directional = recorder_frame_selection.IsDirectional();
  }
  const uint64_t recorder_checkpoint_sequence =
      a11y_recorder::BeginBlinkInteractionCheckpoint(
          recorder_document_node_id, recorder_document_token,
          recorder_source_channel, recorder_source_sequence, recorder_reason,
          std::move(recorder_state), kRecorderMaximumInteractionTextControls,
          kRecorderMaximumInteractionValueLength);
  if (recorder_checkpoint_sequence == 0) {
    return;
  }
  int recorder_text_control_count = 0;
  bool recorder_truncated = false;
  HeapVector<Member<Node>> recorder_pending;
  recorder_pending.push_back(&recorder_document);
  while (!recorder_pending.empty()) {
    Node& recorder_node = *recorder_pending.back();
    recorder_pending.pop_back();
    for (Node* recorder_child = recorder_node.lastChild(); recorder_child;
         recorder_child = recorder_child->previousSibling()) {
      recorder_pending.push_back(recorder_child);
    }
    Element* recorder_element = DynamicTo<Element>(recorder_node);
    if (!recorder_element) {
      continue;
    }
    if (ShadowRoot* recorder_hosted_root = recorder_element->GetShadowRoot()) {
      recorder_pending.push_back(recorder_hosted_root);
    }
    if (!recorder_element->IsTextControl()) {
      continue;
    }
    if (recorder_text_control_count >=
        kRecorderMaximumInteractionTextControls) {
      recorder_truncated = true;
      break;
    }
    auto& recorder_control = To<TextControlElement>(*recorder_element);
    const String recorder_value = recorder_control.Value();
    const int recorder_value_length =
        static_cast<int>(recorder_value.length());
    const bool recorder_value_truncated =
        recorder_value_length > kRecorderMaximumInteractionValueLength;
    const String recorder_recorded_value =
        recorder_value_truncated
            ? recorder_value.substr(0, kRecorderMaximumInteractionValueLength)
            : recorder_value;
    a11y_recorder::RecordBlinkInteractionCheckpointTextControl(
        recorder_checkpoint_sequence, recorder_document_node_id,
        recorder_document_token, recorder_text_control_count,
        recorder_control.GetDomNodeId(),
        recorder_control.FormControlTypeAsString().Utf8(),
        recorder_recorded_value.Utf8(), recorder_value_length,
        recorder_value_truncated,
        static_cast<int>(recorder_control.selectionStart()),
        static_cast<int>(recorder_control.selectionEnd()),
        recorder_control.selectionDirection().Utf8());
    ++recorder_text_control_count;
  }
  a11y_recorder::CompleteBlinkInteractionCheckpoint(
      recorder_checkpoint_sequence, recorder_document_node_id,
      recorder_document_token, recorder_text_control_count,
      recorder_truncated, kRecorderMaximumInteractionTextControls);
}

"""
BLINK_INTERACTION_CHECKPOINT_INCLUDES = (
    '#include "third_party/blink/renderer/core/css/selector_checker.h"',
    '#include "third_party/blink/renderer/core/editing/frame_selection.h"',
    '#include "third_party/blink/renderer/core/editing/position.h"',
    '#include "third_party/blink/renderer/core/html/forms/'
    'text_control_element.h"',
)
# The interaction checkpoint follows each layout checkpoint as well. The
# layout helper opens an unnamed namespace, so the declaration of the blink
# scope helper is placed ahead of it.
BLINK_LAYOUT_INTERACTION_CHECKPOINT_DECLARATION = """\
// Defined in document.cc; records the interaction state immediately after the
// layout checkpoint it follows.
void RecorderRecordInteractionCheckpoint(Document& recorder_document,
                                         uint64_t recorder_source_sequence,
                                         const char* recorder_source_channel,
                                         const char* recorder_reason);

"""
BLINK_DOM_CHECKPOINT_DECLARATION = """\
// Defined in document.cc, which records every DOM checkpoint.
void RecorderRecordDomCheckpoint(Document& recorder_document,
                                 const char* recorder_reason);

"""
BLINK_DOM_CHECKPOINT_HOOK = """\
  RecorderRecordDomCheckpoint(*this, "finished-parsing");
"""
BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK = """\
    for (const auto& recorder_document : recorder_mutated_documents) {
      if (!recorder_document->HasFinishedParsing() ||
          !recorder_document->IsActive())
        continue;
      RecorderRecordDomCheckpoint(*recorder_document, "post-mutation");
    }
"""
BLINK_DOM_CHECKPOINT_INCLUDES = (
    '#include "third_party/blink/renderer/core/dom/shadow_root.h"',
    '#include "third_party/blink/renderer/core/dom/slot_assignment.h"',
    '#include "third_party/blink/renderer/core/html/html_slot_element.h"',
)
BLINK_POST_MUTATION_DOM_CHECKPOINT_HOOK = """\
    HeapHashSet<Member<Document>> recorder_mutated_documents;
    recorder_mutated_documents.swap(recorder_mutated_documents_);
"""
ORIGINAL_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK = """\
    constexpr int kRecorderMaximumDomCheckpointNodes = 512;
    for (const auto& recorder_document : recorder_mutated_documents) {
      if (!recorder_document->HasFinishedParsing() ||
          !recorder_document->IsActive())
        continue;
      const int recorder_document_node_id =
          recorder_document->GetDomNodeId();
      const uint64_t recorder_checkpoint_sequence =
          a11y_recorder::BeginBlinkDomCheckpoint(
              recorder_document_node_id, "post-mutation",
              kRecorderMaximumDomCheckpointNodes);
      if (recorder_checkpoint_sequence == 0)
        continue;
      int recorder_node_count = 0;
      bool recorder_truncated = false;
      for (Node& recorder_node :
           NodeTraversal::InclusiveDescendantsOf(*recorder_document)) {
        if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
          recorder_truncated = true;
          break;
        }
        ContainerNode* recorder_parent = recorder_node.parentNode();
        a11y_recorder::RecordBlinkDomCheckpointNode(
            recorder_checkpoint_sequence, recorder_document_node_id,
            recorder_node_count, recorder_node.GetDomNodeId(),
            recorder_parent ? recorder_parent->GetDomNodeId() : 0,
            static_cast<int>(recorder_node.getNodeType()),
            recorder_node.nodeName().Utf8().c_str());
        ++recorder_node_count;
      }
      a11y_recorder::CompleteBlinkDomCheckpoint(
          recorder_checkpoint_sequence, recorder_document_node_id,
          "post-mutation", recorder_node_count, recorder_truncated,
          kRecorderMaximumDomCheckpointNodes);
    }
"""
LEGACY_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK = """\
    constexpr int kRecorderMaximumDomCheckpointNodes = 512;
    for (const auto& recorder_document : recorder_mutated_documents) {
      if (!recorder_document->HasFinishedParsing() ||
          !recorder_document->IsActive())
        continue;
      const int recorder_document_node_id =
          recorder_document->GetDomNodeId();
      const std::string recorder_document_token =
          recorder_document->Token().ToString();
      const uint64_t recorder_checkpoint_sequence =
          a11y_recorder::BeginBlinkDomCheckpoint(
              recorder_document_node_id, recorder_document_token,
              "post-mutation",
              kRecorderMaximumDomCheckpointNodes);
      if (recorder_checkpoint_sequence == 0)
        continue;
      int recorder_node_count = 0;
      bool recorder_truncated = false;
      for (Node& recorder_node :
           NodeTraversal::InclusiveDescendantsOf(*recorder_document)) {
        if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
          recorder_truncated = true;
          break;
        }
        ContainerNode* recorder_parent = recorder_node.parentNode();
        a11y_recorder::RecordBlinkDomCheckpointNode(
            recorder_checkpoint_sequence, recorder_document_node_id,
            recorder_document_token,
            recorder_node_count, recorder_node.GetDomNodeId(),
            recorder_parent ? recorder_parent->GetDomNodeId() : 0,
            static_cast<int>(recorder_node.getNodeType()),
            recorder_node.nodeName().Utf8().c_str());
        ++recorder_node_count;
      }
      a11y_recorder::CompleteBlinkDomCheckpoint(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token,
          "post-mutation", recorder_node_count, recorder_truncated,
          kRecorderMaximumDomCheckpointNodes);
    }
"""
INTERMEDIATE_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK = """\
    constexpr int kRecorderMaximumDomCheckpointNodes = 512;
    constexpr int kRecorderMaximumDomAttributesPerNode = 64;
    constexpr int kRecorderMaximumDomValueLength = 4096;
    for (const auto& recorder_document : recorder_mutated_documents) {
      if (!recorder_document->HasFinishedParsing() ||
          !recorder_document->IsActive())
        continue;
      const int recorder_document_node_id =
          recorder_document->GetDomNodeId();
      const std::string recorder_document_token =
          recorder_document->Token().ToString();
      const uint64_t recorder_checkpoint_sequence =
          a11y_recorder::BeginBlinkDomCheckpoint(
              recorder_document_node_id, recorder_document_token,
              "post-mutation",
              kRecorderMaximumDomCheckpointNodes);
      if (recorder_checkpoint_sequence == 0)
        continue;
      int recorder_node_count = 0;
      bool recorder_truncated = false;
      int recorder_attribute_count = 0;
      bool recorder_attributes_truncated = false;
      for (Node& recorder_node :
           NodeTraversal::InclusiveDescendantsOf(*recorder_document)) {
        if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
          recorder_truncated = true;
          break;
        }
        ContainerNode* recorder_parent = recorder_node.parentNode();
        a11y_recorder::RecordBlinkDomCheckpointNode(
            recorder_checkpoint_sequence, recorder_document_node_id,
            recorder_document_token,
            recorder_node_count, recorder_node.GetDomNodeId(),
            recorder_parent ? recorder_parent->GetDomNodeId() : 0,
            static_cast<int>(recorder_node.getNodeType()),
            recorder_node.nodeName().Utf8().c_str());
        ++recorder_node_count;
        Element* recorder_element = DynamicTo<Element>(recorder_node);
        if (!recorder_element)
          continue;
        int recorder_node_attribute_index = 0;
        for (const Attribute& recorder_attribute :
             recorder_element->Attributes()) {
          if (recorder_node_attribute_index >=
              kRecorderMaximumDomAttributesPerNode) {
            recorder_attributes_truncated = true;
            break;
          }
          const String recorder_attribute_value = recorder_attribute.Value();
          const int recorder_attribute_value_length =
              static_cast<int>(recorder_attribute_value.length());
          const bool recorder_attribute_value_truncated =
              recorder_attribute_value_length >
              kRecorderMaximumDomValueLength;
          const String recorder_recorded_attribute_value =
              recorder_attribute_value_truncated
                  ? recorder_attribute_value.Left(
                        kRecorderMaximumDomValueLength)
                  : recorder_attribute_value;
          a11y_recorder::RecordBlinkDomCheckpointNodeAttribute(
              recorder_checkpoint_sequence, recorder_document_node_id,
              recorder_document_token, recorder_node.GetDomNodeId(),
              recorder_node_attribute_index,
              recorder_attribute.NamespaceURI().Utf8().c_str(),
              recorder_attribute.LocalName().Utf8().c_str(),
              recorder_recorded_attribute_value.Utf8().c_str(),
              recorder_attribute_value_length,
              recorder_attribute_value_truncated,
              kRecorderMaximumDomValueLength);
          ++recorder_node_attribute_index;
          ++recorder_attribute_count;
        }
      }
      a11y_recorder::CompleteBlinkDomCheckpoint(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token,
          "post-mutation", recorder_node_count, recorder_truncated,
          kRecorderMaximumDomCheckpointNodes,
          recorder_attribute_count,
          recorder_attributes_truncated,
          kRecorderMaximumDomAttributesPerNode,
          kRecorderMaximumDomValueLength);
    }
"""

LEGACY_LIGHT_TREE_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK = """\
    constexpr int kRecorderMaximumDomCheckpointNodes = 512;
    constexpr int kRecorderMaximumDomAttributesPerNode = 64;
    constexpr int kRecorderMaximumDomValueLength = 4096;
    for (const auto& recorder_document : recorder_mutated_documents) {
      if (!recorder_document->HasFinishedParsing() ||
          !recorder_document->IsActive())
        continue;
      const int recorder_document_node_id =
          recorder_document->GetDomNodeId();
      const std::string recorder_document_token =
          recorder_document->Token().ToString();
      const uint64_t recorder_checkpoint_sequence =
          a11y_recorder::BeginBlinkDomCheckpoint(
              recorder_document_node_id, recorder_document_token,
              "post-mutation",
              kRecorderMaximumDomCheckpointNodes);
      if (recorder_checkpoint_sequence == 0)
        continue;
      int recorder_node_count = 0;
      bool recorder_truncated = false;
      int recorder_attribute_count = 0;
      bool recorder_attributes_truncated = false;
      for (Node& recorder_node :
           NodeTraversal::InclusiveDescendantsOf(*recorder_document)) {
        if (recorder_node_count >= kRecorderMaximumDomCheckpointNodes) {
          recorder_truncated = true;
          break;
        }
        ContainerNode* recorder_parent = recorder_node.parentNode();
        a11y_recorder::RecordBlinkDomCheckpointNode(
            recorder_checkpoint_sequence, recorder_document_node_id,
            recorder_document_token,
            recorder_node_count, recorder_node.GetDomNodeId(),
            recorder_parent ? recorder_parent->GetDomNodeId() : 0,
            static_cast<int>(recorder_node.getNodeType()),
            recorder_node.nodeName().Utf8().c_str());
        ++recorder_node_count;
        Element* recorder_element = DynamicTo<Element>(recorder_node);
        if (!recorder_element)
          continue;
        int recorder_node_attribute_index = 0;
        for (const Attribute& recorder_attribute :
             recorder_element->Attributes()) {
          if (recorder_node_attribute_index >=
              kRecorderMaximumDomAttributesPerNode) {
            recorder_attributes_truncated = true;
            break;
          }
          const String recorder_attribute_value = recorder_attribute.Value();
          const int recorder_attribute_value_length =
              static_cast<int>(recorder_attribute_value.length());
          const bool recorder_attribute_value_truncated =
              recorder_attribute_value_length >
              kRecorderMaximumDomValueLength;
          const String recorder_recorded_attribute_value =
              recorder_attribute_value_truncated
                  ? recorder_attribute_value.substr(
                        0, kRecorderMaximumDomValueLength)
                  : recorder_attribute_value;
          a11y_recorder::RecordBlinkDomCheckpointNodeAttribute(
              recorder_checkpoint_sequence, recorder_document_node_id,
              recorder_document_token, recorder_node.GetDomNodeId(),
              recorder_node_attribute_index,
              recorder_attribute.NamespaceURI().Utf8().c_str(),
              recorder_attribute.LocalName().Utf8().c_str(),
              recorder_recorded_attribute_value.Utf8().c_str(),
              recorder_attribute_value_length,
              recorder_attribute_value_truncated,
              kRecorderMaximumDomValueLength);
          ++recorder_node_attribute_index;
          ++recorder_attribute_count;
        }
      }
      a11y_recorder::CompleteBlinkDomCheckpoint(
          recorder_checkpoint_sequence, recorder_document_node_id,
          recorder_document_token,
          "post-mutation", recorder_node_count, recorder_truncated,
          kRecorderMaximumDomCheckpointNodes,
          recorder_attribute_count,
          recorder_attributes_truncated,
          kRecorderMaximumDomAttributesPerNode,
          kRecorderMaximumDomValueLength);
    }
"""
BLINK_MUTATION_AGENT_METHOD = """\
  void EnqueueRecorderDomCheckpoint(Document& document) {
    EnsureEnqueueMicrotask();
    recorder_mutated_documents_.insert(&document);
  }

"""
BLINK_MUTATION_AGENT_TRACE_HOOK = """\
    visitor->Trace(recorder_mutated_documents_);
"""
BLINK_MUTATION_AGENT_MEMBER = """\
  HeapHashSet<Member<Document>> recorder_mutated_documents_;
"""
INTERMEDIATE_BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER = """\
// Records one accepted attribute mutation as recorder evidence.
//
// A coalesced DOM checkpoint reports the attribute state of a tree but cannot
// report which attribute changed, so the transition is only recoverable from
// this record. The change type is derived from which value is null rather than
// from the calling function, so a modification that upstream reports with a
// null old or new value is still recorded as an addition or a removal instead
// of a self-contradicting change.
static void RecordRecorderElementAttributeMutation(
    Element& recorder_element,
    const QualifiedName& recorder_name,
    const AtomicString& recorder_old_value,
    const AtomicString& recorder_new_value) {
  constexpr int kRecorderMaximumDomValueLength = 4096;
  const bool recorder_has_value = !recorder_new_value.IsNull();
  const bool recorder_has_previous_value = !recorder_old_value.IsNull();
  if (!recorder_has_value && !recorder_has_previous_value) {
    return;
  }
  Document& recorder_document = recorder_element.GetDocument();
  const int recorder_document_node_id = recorder_document.GetDomNodeId();
  if (recorder_document_node_id <= 0) {
    return;
  }
  const int recorder_change_type =
      !recorder_has_previous_value ? 0 : (!recorder_has_value ? 1 : 2);
  const String recorder_value =
      recorder_has_value ? recorder_new_value.GetString() : g_empty_string;
  const String recorder_previous_value =
      recorder_has_previous_value ? recorder_old_value.GetString()
                                 : g_empty_string;
  const int recorder_value_length = static_cast<int>(recorder_value.length());
  const int recorder_previous_value_length =
      static_cast<int>(recorder_previous_value.length());
  const bool recorder_value_truncated =
      recorder_value_length > kRecorderMaximumDomValueLength;
  const bool recorder_previous_value_truncated =
      recorder_previous_value_length > kRecorderMaximumDomValueLength;
  const String recorder_recorded_value =
      recorder_value_truncated
          ? recorder_value.Left(kRecorderMaximumDomValueLength)
          : recorder_value;
  const String recorder_recorded_previous_value =
      recorder_previous_value_truncated
          ? recorder_previous_value.Left(kRecorderMaximumDomValueLength)
          : recorder_previous_value;
  a11y_recorder::RecordBlinkDomAttributeChanged(
      recorder_document_node_id, recorder_document.Token().ToString(),
      recorder_element.GetDomNodeId(),
      recorder_element.nodeName().Utf8().c_str(),
      recorder_name.NamespaceURI().Utf8().c_str(),
      recorder_name.LocalName().Utf8().c_str(), recorder_change_type,
      recorder_recorded_value.Utf8().c_str(), recorder_value_length,
      recorder_value_truncated,
      recorder_recorded_previous_value.Utf8().c_str(),
      recorder_previous_value_length, recorder_previous_value_truncated,
      kRecorderMaximumDomValueLength);
  // An attribute mutation does not change the child list, so nothing else
  // queues the document. Queuing it here keeps the tree state that follows a
  // recorded transition observable, and consumes the checkpoint identity the
  // transition record already reserved.
  MutationObserver::EnqueueRecorderDomCheckpoint(recorder_document);
}
"""

BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER = """\
// Records one accepted attribute mutation as recorder evidence.
//
// A coalesced DOM checkpoint reports the attribute state of a tree but cannot
// report which attribute changed, so the transition is only recoverable from
// this record. The change type is derived from which value is null rather than
// from the calling function, so a modification that upstream reports with a
// null old or new value is still recorded as an addition or a removal instead
// of a self-contradicting change.
static void RecordRecorderElementAttributeMutation(
    Element& recorder_element,
    const QualifiedName& recorder_name,
    const AtomicString& recorder_old_value,
    const AtomicString& recorder_new_value) {
  constexpr int kRecorderMaximumDomValueLength = 4096;
  const bool recorder_has_value = !recorder_new_value.IsNull();
  const bool recorder_has_previous_value = !recorder_old_value.IsNull();
  if (!recorder_has_value && !recorder_has_previous_value) {
    return;
  }
  Document& recorder_document = recorder_element.GetDocument();
  const int recorder_document_node_id = recorder_document.GetDomNodeId();
  if (recorder_document_node_id <= 0) {
    return;
  }
  const int recorder_change_type =
      !recorder_has_previous_value ? 0 : (!recorder_has_value ? 1 : 2);
  const String recorder_value =
      recorder_has_value ? recorder_new_value.GetString() : g_empty_string;
  const String recorder_previous_value =
      recorder_has_previous_value ? recorder_old_value.GetString()
                                 : g_empty_string;
  const int recorder_value_length = static_cast<int>(recorder_value.length());
  const int recorder_previous_value_length =
      static_cast<int>(recorder_previous_value.length());
  const bool recorder_value_truncated =
      recorder_value_length > kRecorderMaximumDomValueLength;
  const bool recorder_previous_value_truncated =
      recorder_previous_value_length > kRecorderMaximumDomValueLength;
  const String recorder_recorded_value =
      recorder_value_truncated
          ? recorder_value.substr(0, kRecorderMaximumDomValueLength)
          : recorder_value;
  const String recorder_recorded_previous_value =
      recorder_previous_value_truncated
          ? recorder_previous_value.substr(0, kRecorderMaximumDomValueLength)
          : recorder_previous_value;
  a11y_recorder::RecordBlinkDomAttributeChanged(
      recorder_document_node_id, recorder_document.Token().ToString(),
      recorder_element.GetDomNodeId(),
      recorder_element.nodeName().Utf8().c_str(),
      recorder_name.NamespaceURI().Utf8().c_str(),
      recorder_name.LocalName().Utf8().c_str(), recorder_change_type,
      recorder_recorded_value.Utf8().c_str(), recorder_value_length,
      recorder_value_truncated,
      recorder_recorded_previous_value.Utf8().c_str(),
      recorder_previous_value_length, recorder_previous_value_truncated,
      kRecorderMaximumDomValueLength);
  // An attribute mutation does not change the child list, so nothing else
  // queues the document. Queuing it here keeps the tree state that follows a
  // recorded transition observable, and consumes the checkpoint identity the
  // transition record already reserved.
  MutationObserver::EnqueueRecorderDomCheckpoint(recorder_document);
}
"""


BLINK_ELEMENT_ATTRIBUTE_ADDED_HOOK = """\
  RecordRecorderElementAttributeMutation(*this, name, g_null_atom, value);
"""


BLINK_ELEMENT_ATTRIBUTE_MODIFIED_HOOK = """\
  RecordRecorderElementAttributeMutation(*this, name, old_value, new_value);
"""


BLINK_ELEMENT_ATTRIBUTE_REMOVED_HOOK = """\
  RecordRecorderElementAttributeMutation(*this, name, old_value, g_null_atom);
"""


INTERMEDIATE_BLINK_CHARACTER_DATA_MUTATION_HOOK = """\
  // Parser-driven text updates are excluded. The text a document was parsed
  // with is already reported by the finished-parsing checkpoint, and recording
  // every parse-time chunk would queue a checkpoint per chunk during load.
  if (source != kUpdateFromParser) {
    constexpr int kRecorderMaximumDomValueLength = 4096;
    Document& recorder_document = GetDocument();
    const int recorder_document_node_id = recorder_document.GetDomNodeId();
    if (recorder_document_node_id > 0) {
      ContainerNode* recorder_parent = parentNode();
      const int recorder_text_length = static_cast<int>(new_data.length());
      const int recorder_previous_text_length =
          static_cast<int>(old_data.length());
      const bool recorder_text_truncated =
          recorder_text_length > kRecorderMaximumDomValueLength;
      const bool recorder_previous_text_truncated =
          recorder_previous_text_length > kRecorderMaximumDomValueLength;
      const String recorder_recorded_text =
          recorder_text_truncated
              ? new_data.Left(kRecorderMaximumDomValueLength)
              : new_data;
      const String recorder_recorded_previous_text =
          recorder_previous_text_truncated
              ? old_data.Left(kRecorderMaximumDomValueLength)
              : old_data;
      a11y_recorder::RecordBlinkDomCharacterDataChanged(
          recorder_document_node_id, recorder_document.Token().ToString(),
          GetDomNodeId(),
          recorder_parent ? recorder_parent->GetDomNodeId() : 0,
          static_cast<int>(getNodeType()),
          recorder_recorded_text.Utf8().c_str(), recorder_text_length,
          recorder_text_truncated,
          recorder_recorded_previous_text.Utf8().c_str(),
          recorder_previous_text_length, recorder_previous_text_truncated,
          kRecorderMaximumDomValueLength);
      MutationObserver::EnqueueRecorderDomCheckpoint(recorder_document);
    }
  }
"""

BLINK_CHARACTER_DATA_MUTATION_HOOK = """\
  // Parser-driven text updates are excluded. The text a document was parsed
  // with is already reported by the finished-parsing checkpoint, and recording
  // every parse-time chunk would queue a checkpoint per chunk during load.
  if (source != kUpdateFromParser) {
    constexpr int kRecorderMaximumDomValueLength = 4096;
    Document& recorder_document = GetDocument();
    const int recorder_document_node_id = recorder_document.GetDomNodeId();
    if (recorder_document_node_id > 0) {
      ContainerNode* recorder_parent = parentNode();
      const int recorder_text_length = static_cast<int>(new_data.length());
      const int recorder_previous_text_length =
          static_cast<int>(old_data.length());
      const bool recorder_text_truncated =
          recorder_text_length > kRecorderMaximumDomValueLength;
      const bool recorder_previous_text_truncated =
          recorder_previous_text_length > kRecorderMaximumDomValueLength;
      const String recorder_recorded_text =
          recorder_text_truncated
              ? new_data.substr(0, kRecorderMaximumDomValueLength)
              : new_data;
      const String recorder_recorded_previous_text =
          recorder_previous_text_truncated
              ? old_data.substr(0, kRecorderMaximumDomValueLength)
              : old_data;
      a11y_recorder::RecordBlinkDomCharacterDataChanged(
          recorder_document_node_id, recorder_document.Token().ToString(),
          GetDomNodeId(),
          recorder_parent ? recorder_parent->GetDomNodeId() : 0,
          static_cast<int>(getNodeType()),
          recorder_recorded_text.Utf8().c_str(), recorder_text_length,
          recorder_text_truncated,
          recorder_recorded_previous_text.Utf8().c_str(),
          recorder_previous_text_length, recorder_previous_text_truncated,
          kRecorderMaximumDomValueLength);
      MutationObserver::EnqueueRecorderDomCheckpoint(recorder_document);
    }
  }
"""


BLINK_MUTATION_OBSERVER_METHOD = """\
// static
void MutationObserver::EnqueueRecorderDomCheckpoint(Document& document) {
  MutationObserverAgentData::From(document.GetAgent())
      .EnqueueRecorderDomCheckpoint(document);
}

"""
BLINK_DOCUMENT_MUTATION_HOOK = """\
  if (HasFinishedParsing())
    MutationObserver::EnqueueRecorderDomCheckpoint(*this);
"""
BLINK_EVENT_LISTENER_INCLUDE = (
    '#include "third_party/blink/renderer/core/dom/events/event_listener.h"'
)

# Capturing where a listener registration came from needs Blink's own capture
# helper and the location type it returns.
BLINK_CAPTURE_SOURCE_LOCATION_INCLUDE = (
    '#include "third_party/blink/renderer/bindings/core/v8/'
    'capture_source_location.h"'
)
BLINK_SOURCE_LOCATION_INCLUDE = (
    '#include "third_party/blink/renderer/platform/bindings/source_location.h"'
)

# Reading the world a listener callback belongs to needs the script-based
# listener definition that holds the world and the world type itself.
BLINK_JS_BASED_EVENT_LISTENER_INCLUDE = (
    '#include "third_party/blink/renderer/bindings/core/v8/'
    'js_based_event_listener.h"'
)
BLINK_DOM_WRAPPER_WORLD_INCLUDE = (
    '#include "third_party/blink/renderer/platform/bindings/'
    'dom_wrapper_world.h"'
)
# The world name maps are main-thread only, so every hook that reads a world
# name also tests the thread.
BLINK_WTF_INCLUDE = '#include "third_party/blink/renderer/platform/wtf/wtf.h"'

BLINK_LISTENER_KIND_HELPER = """\
namespace {

// Reports how a listener entered Blink's listener map. Blink accepts an
// addEventListener call, an on-event IDL attribute assignment, and an inline
// content attribute through one internal registration path, so the form is read
// from the listener object Blink created rather than from the call site. A
// content attribute produces a JSEventHandlerForContentAttribute, any other
// event handler is the one an on-event attribute setter created, and a listener
// that is neither arrived through addEventListener.
const char* RecorderListenerRegistrationKind(const EventListener* listener) {
  if (!listener) {
    return a11y_recorder::kListenerRegistrationKindAddEventListener;
  }
  if (listener->IsEventHandlerForContentAttribute()) {
    return a11y_recorder::kListenerRegistrationKindInlineAttribute;
  }
  if (listener->IsEventHandler()) {
    return a11y_recorder::kListenerRegistrationKindEventHandlerProperty;
  }
  return a11y_recorder::kListenerRegistrationKindAddEventListener;
}

}  // namespace

"""
BLINK_LISTENER_WORLD_HELPER = """\
namespace {

// Reports the JavaScript world a listener callback belongs to. Blink holds the
// world on the callback object, so this is the world the registration was made
// from rather than whichever world happens to be current when a record is
// written. A listener Blink installed itself is not script based and belongs to
// no world, which is reported as no world rather than as the main world.
const DOMWrapperWorld* RecorderListenerWorld(const EventListener* listener) {
  const JSBasedEventListener* recorder_script_listener =
      DynamicTo<JSBasedEventListener>(listener);
  if (!recorder_script_listener) {
    return nullptr;
  }
  return &recorder_script_listener->GetWorldForInspector();
}

// Reports the recorder's name for a world type. The inspector's isolated worlds
// are tested before isolated worlds generally, because Blink classifies both as
// isolated. A world type the recorder does not name is reported as other rather
// than as one it is not.
const char* RecorderExecutionWorldKind(const DOMWrapperWorld& world) {
  if (world.IsMainWorld()) {
    return a11y_recorder::kExecutionWorldKindMain;
  }
  if (world.GetWorldType() == DOMWrapperWorld::WorldType::kInspectorIsolated) {
    return a11y_recorder::kExecutionWorldKindInspectorIsolated;
  }
  if (world.IsIsolatedWorld()) {
    return a11y_recorder::kExecutionWorldKindIsolated;
  }
  if (world.IsWorkerOrWorkletWorld()) {
    return a11y_recorder::kExecutionWorldKindWorkerOrWorklet;
  }
  if (world.IsShadowRealmWorld()) {
    return a11y_recorder::kExecutionWorldKindShadowRealm;
  }
  return a11y_recorder::kExecutionWorldKindOther;
}

}  // namespace

"""
BLINK_LISTENER_HOOK = """\
    {
      const EventListener* recorder_callback = registered_listener->Callback();
      const char* recorder_registration_kind =
          RecorderListenerRegistrationKind(recorder_callback);
      const DOMWrapperWorld* recorder_world =
          RecorderListenerWorld(recorder_callback);
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
              : 0,
          recorder_world ? RecorderExecutionWorldKind(*recorder_world) : "",
          recorder_world ? recorder_world->GetWorldId()
                         : a11y_recorder::kExecutionWorldIdUnobserved,
          recorder_world && !recorder_world->IsMainWorld() && IsMainThread()
              ? recorder_world->NonMainWorldHumanReadableName().Utf8().c_str()
              : "",
          recorder_world && !recorder_world->IsMainWorld() && IsMainThread()
              ? recorder_world->NonMainWorldStableId().Utf8().c_str()
              : "");
    }
"""
BLINK_LISTENER_REMOVED_HOOK = """\
  {
    const EventListener* recorder_callback = registered_listener->Callback();
    const char* recorder_registration_kind =
        RecorderListenerRegistrationKind(recorder_callback);
    const DOMWrapperWorld* recorder_world =
        RecorderListenerWorld(recorder_callback);
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
    a11y_recorder::RecordBlinkListenerRemoved(
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
        recorder_location ? static_cast<int>(recorder_location->ColumnNumber())
                          : 0,
        recorder_world ? RecorderExecutionWorldKind(*recorder_world) : "",
        recorder_world ? recorder_world->GetWorldId()
                       : a11y_recorder::kExecutionWorldIdUnobserved,
        recorder_world && !recorder_world->IsMainWorld() && IsMainThread()
            ? recorder_world->NonMainWorldHumanReadableName().Utf8().c_str()
            : "",
        recorder_world && !recorder_world->IsMainWorld() && IsMainThread()
            ? recorder_world->NonMainWorldStableId().Utf8().c_str()
            : "");
  }
"""
BLINK_LISTENER_ATTRIBUTE_REPLACEMENT_ANCHOR = """\
  if (registered_listener) {
    registered_listener->SetCallback(listener);
    return true;
  }
"""
# Assigning an on-event IDL attribute over a listener that a content attribute
# or an earlier assignment established replaces the callback in place. Blink
# neither adds nor removes a registration on that path, so neither hook above
# runs and the registration keeps its identity. Without this hook the archive
# would keep reporting the kind of the callback Blink no longer holds.
# The inner block is the region a later revision migrates, so it is defined
# once and composed into the hook rather than restated.
BLINK_LISTENER_ATTRIBUTE_REPLACEMENT_HOOK_REGION = """\
    {
      const EventListener* recorder_callback = listener;
      const char* recorder_registration_kind =
          RecorderListenerRegistrationKind(recorder_callback);
      const DOMWrapperWorld* recorder_world =
          RecorderListenerWorld(recorder_callback);
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
      a11y_recorder::RecordBlinkListenerCallbackReplaced(
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
              : 0,
          recorder_world ? RecorderExecutionWorldKind(*recorder_world) : "",
          recorder_world ? recorder_world->GetWorldId()
                         : a11y_recorder::kExecutionWorldIdUnobserved,
          recorder_world && !recorder_world->IsMainWorld() && IsMainThread()
              ? recorder_world->NonMainWorldHumanReadableName().Utf8().c_str()
              : "",
          recorder_world && !recorder_world->IsMainWorld() && IsMainThread()
              ? recorder_world->NonMainWorldStableId().Utf8().c_str()
              : "");
    }
"""
BLINK_LISTENER_ATTRIBUTE_REPLACEMENT_HOOK = (
    "  if (registered_listener) {\n"
    "    registered_listener->SetCallback(listener);\n"
    + BLINK_LISTENER_ATTRIBUTE_REPLACEMENT_HOOK_REGION
    + "    return true;\n"
    "  }\n"
)
LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK = """\
    a11y_recorder::BeginBlinkListenerInvocation(
        reinterpret_cast<uintptr_t>(&event),
        reinterpret_cast<uintptr_t>(registered_listener.Get()));
"""
LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK_NODE_ONLY = """\
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
BLINK_LISTENER_INVOCATION_STARTED_HOOK = """\
    {
      Node* recorder_current_target = ToNode();
      LocalDOMWindow* recorder_current_window = ToLocalDOMWindow();
      Element* recorder_current_element =
          DynamicTo<Element>(recorder_current_target);
      LocalDOMWindow* recorder_current_document_window =
          recorder_current_window
              ? recorder_current_window
              : DynamicTo<LocalDOMWindow>(GetExecutionContext());
      Document* recorder_current_document =
          recorder_current_target
              ? &recorder_current_target->GetDocument()
              : (recorder_current_document_window
                     ? recorder_current_document_window->document()
                     : nullptr);
      a11y_recorder::BeginBlinkListenerInvocation(
          reinterpret_cast<uintptr_t>(&event),
          reinterpret_cast<uintptr_t>(registered_listener.Get()),
          recorder_current_target
              ? a11y_recorder::kEventTargetKindNode
              : (recorder_current_window
                     ? a11y_recorder::kEventTargetKindWindow
                     : a11y_recorder::kEventTargetKindOther),
          InterfaceName().Utf8().c_str(),
          reinterpret_cast<uintptr_t>(this),
          recorder_current_document
              ? recorder_current_document->GetDomNodeId()
              : 0,
          recorder_current_target ? recorder_current_target->GetDomNodeId() : 0,
          recorder_current_target
              ? recorder_current_target->nodeName().Utf8().c_str()
              : "",
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
# Blink keeps the Window outside the Node event contexts, so the propagation
# path recorded from those contexts alone stopped at the document and said
# nothing about the Window that the same dispatch reaches. The Window entry is
# taken from Blink's own window event context, which is established before this
# hook runs, so the recorded path ends where Blink's does.
LEGACY_BLINK_DISPATCH_HOOK_WITHOUT_WINDOW = """\
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
LEGACY_BLINK_DISPATCH_HOOK_WITHOUT_SCOPES = (
    LEGACY_BLINK_DISPATCH_HOOK_WITHOUT_WINDOW.replace(
        """\
  a11y_recorder::CompleteBlinkDispatchStart(""",
        """\
  if (LocalDOMWindow* recorder_path_window =
          event_->GetEventPath().GetWindowEventContext().Window()) {
    a11y_recorder::RecordBlinkDispatchPathWindow(
        reinterpret_cast<uintptr_t>(event_),
        recorder_path_window->document()
            ? recorder_path_window->document()->GetDomNodeId()
            : 0,
        reinterpret_cast<uintptr_t>(recorder_path_window),
        recorder_path_window->InterfaceName().Utf8().c_str());
  }
  a11y_recorder::CompleteBlinkDispatchStart(""",
    )
)
# Each path entry is recorded with the tree scope Blink dispatches it in, the
# target and related target as retargeted for that scope, and the entries of
# the path that scope can see. The visible entries are the ones composedPath()
# returns to a listener in that scope; they are read from the same per-scope
# list Blink builds for composedPath(), which Blink computes once per dispatch
# and caches, so reading it here returns what a listener would receive.
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
  EventPath& recorder_event_path = event_->GetEventPath();
  LocalDOMWindow* recorder_path_window =
      recorder_event_path.GetWindowEventContext().Window();
  auto recorder_visible_indexes =
      [&recorder_event_path, recorder_path_window](
          TreeScopeEventContext& recorder_scope_context) {
        std::vector<int> recorder_indexes;
        for (const Member<EventTarget>& recorder_visible :
             recorder_scope_context.EnsureEventPath(recorder_event_path)) {
          int recorder_index = -1;
          int recorder_position = 0;
          for (const NodeEventContext& recorder_candidate :
               recorder_event_path.NodeEventContexts()) {
            if (&recorder_candidate.GetNode() == recorder_visible.Get()) {
              recorder_index = recorder_position;
              break;
            }
            ++recorder_position;
          }
          if (recorder_index < 0 && recorder_path_window &&
              recorder_visible.Get() == recorder_path_window) {
            recorder_index = recorder_position;
          }
          recorder_indexes.push_back(recorder_index);
        }
        return recorder_indexes;
      };
  auto recorder_target_node_id = [](EventTarget* recorder_target) {
    Node* recorder_target_node =
        recorder_target ? recorder_target->ToNode() : nullptr;
    return recorder_target_node ? recorder_target_node->GetDomNodeId() : 0;
  };
  for (NodeEventContext& recorder_context :
       recorder_event_path.NodeEventContexts()) {
    Node& recorder_path_node = recorder_context.GetNode();
    Element* recorder_path_element =
        DynamicTo<Element>(recorder_path_node);
    TreeScopeEventContext& recorder_scope_context =
        recorder_context.GetTreeScopeEventContext();
    ShadowRoot* recorder_scope_root =
        DynamicTo<ShadowRoot>(recorder_scope_context.RootNode());
    const char* recorder_scope_mode = "";
    if (recorder_scope_root) {
      const ShadowRootMode recorder_mode = recorder_scope_root->GetMode();
      recorder_scope_mode =
          recorder_mode == ShadowRootMode::kOpen     ? "open"
          : recorder_mode == ShadowRootMode::kClosed ? "closed"
                                                     : "user-agent";
    }
    a11y_recorder::RecordBlinkDispatchPathNode(
        reinterpret_cast<uintptr_t>(event_),
        recorder_path_node.GetDocument().GetDomNodeId(),
        recorder_path_node.GetDomNodeId(),
        recorder_path_node.nodeName().Utf8().c_str(),
        recorder_path_element
            ? recorder_path_element->GetIdAttribute().Utf8().c_str()
            : "",
        recorder_scope_context.RootNode().GetDomNodeId(),
        recorder_scope_mode,
        recorder_target_node_id(recorder_context.Target()),
        recorder_target_node_id(recorder_context.RelatedTarget()),
        recorder_visible_indexes(recorder_scope_context));
  }
  if (recorder_path_window) {
    WindowEventContext& recorder_window_context =
        recorder_event_path.GetWindowEventContext();
    a11y_recorder::RecordBlinkDispatchPathWindow(
        reinterpret_cast<uintptr_t>(event_),
        recorder_path_window->document()
            ? recorder_path_window->document()->GetDomNodeId()
            : 0,
        reinterpret_cast<uintptr_t>(recorder_path_window),
        recorder_path_window->InterfaceName().Utf8().c_str(),
        recorder_target_node_id(recorder_window_context.Target()),
        recorder_target_node_id(recorder_window_context.RelatedTarget()),
        recorder_event_path.IsEmpty()
            ? std::vector<int>()
            : recorder_visible_indexes(
                  recorder_event_path.TopNodeEventContext()
                      .GetTreeScopeEventContext()));
  }
  a11y_recorder::CompleteBlinkDispatchStart(
      reinterpret_cast<uintptr_t>(event_));
"""
BLINK_DISPATCH_SCOPE_INCLUDES = (
    '#include "third_party/blink/renderer/core/dom/events/node_event_context.h"',
    '#include "third_party/blink/renderer/core/dom/events/tree_scope_event_context.h"',
    '#include "third_party/blink/renderer/core/dom/shadow_root.h"',
)
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
  TaskQueueThrottler(base::sequence_manager::TaskQueue* task_queue,
                     const base::TickClock* tick_clock);
  TaskQueueThrottler(MainThreadTaskQueue* owner,
                     base::sequence_manager::TaskQueue* task_queue,
                     const base::TickClock* tick_clock);
"""
LEGACY_BLINK_THROTTLER_OWNER_CONSTRUCTOR_DECLARATION = """\
  TaskQueueThrottler(MainThreadTaskQueue* owner,
                     base::sequence_manager::TaskQueue* task_queue,
                     const base::TickClock* tick_clock);
"""
BLINK_THROTTLER_CONSTRUCTOR_IMPLEMENTATION = """\
TaskQueueThrottler::TaskQueueThrottler(
    base::sequence_manager::TaskQueue* task_queue,
    const base::TickClock* tick_clock)
    : task_queue_(task_queue), tick_clock_(tick_clock) {}

TaskQueueThrottler::TaskQueueThrottler(
    MainThreadTaskQueue* owner,
    base::sequence_manager::TaskQueue* task_queue,
    const base::TickClock* tick_clock)
    : owner_(owner->AsWeakPtr()),
      task_queue_(task_queue),
      tick_clock_(tick_clock) {}
"""
LEGACY_BLINK_THROTTLER_WEAK_CONSTRUCTOR_IMPLEMENTATION = """\
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
    return static_cast<int>(throttling_type_.get());
  }
"""
LEGACY_BLINK_FRAME_THROTTLING_ACCESSOR = """\
  int RecorderThrottlingType() const {
    return static_cast<int>(throttling_type_);
  }
"""
INTERMEDIATE_BLINK_FRAME_THROTTLING_ACCESSOR = """\
  int RecorderThrottlingType() const {
    return static_cast<int>(static_cast<ThrottlingType>(throttling_type_));
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


HISTORICAL_TEMPLATE_PREFIXES = (
    "LEGACY_",
    "INTERMEDIATE_",
    "ORIGINAL_",
    "TRACED_",
)
BRIDGE_CALL_PATTERN = re.compile(
    r"a11y_recorder::([A-Za-z_][A-Za-z0-9_]*)\s*\("
)
BRIDGE_EXPORT_MARKER = "COMPONENT_EXPORT(RECORDER_BRIDGE)"
_INTEGRATED_PATHS: list[Path] = []


def read_source(path: Path) -> str:
    """Reads a Chromium source and records it for later verification.

    Recording happens on read rather than on write, because a presence guard
    may leave a file untouched. An untouched file is exactly the case that
    needs verifying: it may already hold a hook body written by an earlier
    protocol revision.
    """
    if path not in _INTEGRATED_PATHS:
        _INTEGRATED_PATHS.append(path)
    text = path.read_text(encoding="utf-8")
    upgraded = upgrade_world_name_guards(text)
    if upgraded != text:
        write_patched(path, upgraded)
    return upgraded


# Revisions before the main-thread guard read isolated world names on any
# thread. A presence guard leaves those hook bodies in place, so the old guards
# are rewritten wherever a read source still holds them.
STALE_LISTENER_WORLD_NAME_GUARD = re.compile(
    r"recorder_world && !recorder_world->IsMainWorld\(\)(?! && IsMainThread\(\))"
)
STALE_ORIGIN_WORLD_NAME_GUARD = (
    "  if (!world.IsMainWorld()) {\n"
    "    origin.world_name = world.NonMainWorldHumanReadableName().Utf8();\n"
)
ORIGIN_WORLD_NAME_GUARD = (
    "  // Blink keeps isolated world names and stable identifiers in main-thread\n"
    "  // maps, so a worker world reports neither.\n"
    "  if (!world.IsMainWorld() && IsMainThread()) {\n"
    "    origin.world_name = world.NonMainWorldHumanReadableName().Utf8();\n"
)
UNGUARDED_WORLD_NAME_READ = re.compile(
    r"(NonMainWorldHumanReadableName|NonMainWorldStableId)\(\)"
)


def upgrade_world_name_guards(text: str) -> str:
    text = STALE_LISTENER_WORLD_NAME_GUARD.sub(
        "recorder_world && !recorder_world->IsMainWorld() && IsMainThread()",
        text,
    )
    return text.replace(STALE_ORIGIN_WORLD_NAME_GUARD, ORIGIN_WORLD_NAME_GUARD)


def describe_unguarded_world_name_reads(path: str, text: str) -> list[str]:
    lines = text.splitlines()
    problems: list[str] = []
    for index, line in enumerate(lines):
        if not UNGUARDED_WORLD_NAME_READ.search(line):
            continue
        context = "\n".join(lines[max(0, index - 4) : index + 1])
        if "IsMainThread()" not in context:
            problems.append(
                f"{path}:{index + 1} reads a world name without the "
                "main-thread guard"
            )
    return problems


def write_patched(path: Path, text: str) -> None:
    """Writes a patched Chromium source with fixed encoding and newlines."""
    path.write_text(text, encoding="utf-8", newline="\n")


def integrated_paths() -> tuple[Path, ...]:
    return tuple(_INTEGRATED_PATHS)


def strip_cxx_comments(text: str) -> str:
    without_block = re.sub(r"/\*.*?\*/", " ", text, flags=re.DOTALL)
    return re.sub(r"//[^\n]*", "", without_block)


def split_argument_list(
    text: str, open_paren: int
) -> tuple[int | None, int]:
    """Counts top-level arguments starting at an opening parenthesis.

    Returns the argument count and the index just past the matching closing
    parenthesis. String and character literals are skipped so that a comma
    inside a literal is not counted as an argument separator. Some hook
    templates hold only the leading arguments of a call, because they rewrite
    an existing call in place. An argument list that does not close within the
    given text counts as unknown rather than as an error, since the assembled
    Chromium source is verified separately.
    """
    depth = 0
    arguments = 0
    seen_content = False
    index = open_paren
    while index < len(text):
        character = text[index]
        if character in "\"'":
            quote = character
            index += 1
            while index < len(text):
                if text[index] == "\\":
                    index += 2
                    continue
                if text[index] == quote:
                    break
                index += 1
            seen_content = True
        elif character in "([{":
            depth += 1
            if depth > 1:
                seen_content = True
        elif character in ")]}":
            depth -= 1
            if depth == 0:
                return (arguments + 1 if seen_content else 0), index + 1
        elif character == "," and depth == 1:
            arguments += 1
        elif not character.isspace():
            seen_content = True
        index += 1
    return None, len(text)


def parse_bridge_signatures(header_text: str) -> dict[str, int]:
    """Maps each exported bridge entry point to its declared parameter count."""
    text = strip_cxx_comments(header_text)
    signatures: dict[str, int] = {}
    index = text.find(BRIDGE_EXPORT_MARKER)
    while index >= 0:
        start = index + len(BRIDGE_EXPORT_MARKER)
        open_paren = text.find("(", start)
        if open_paren < 0:
            break
        # An export marker on a data declaration has no parameter list. Reading
        # forward to the next parenthesis would attribute the following
        # function's parameters to it and drop that function from the check, so
        # a declaration that ends before its parenthesis is skipped instead.
        statement_end = text.find(";", start)
        if 0 <= statement_end < open_paren:
            index = text.find(BRIDGE_EXPORT_MARKER, statement_end)
            continue
        name = re.search(
            r"([A-Za-z_][A-Za-z0-9_]*)\s*$", text[start:open_paren]
        )
        if not name:
            index = text.find(BRIDGE_EXPORT_MARKER, start)
            continue
        count, end = split_argument_list(text, open_paren)
        if count is not None:
            signatures[name.group(1)] = count
        index = text.find(BRIDGE_EXPORT_MARKER, end)
    if not signatures:
        raise RuntimeError("no exported recorder bridge entry points found")
    return signatures


def bridge_call_arities(text: str) -> tuple[tuple[str, int, int], ...]:
    """Returns the name, argument count, and line of every complete call."""
    calls: list[tuple[str, int, int]] = []
    for match in BRIDGE_CALL_PATTERN.finditer(text):
        open_paren = match.end() - 1
        count, _ = split_argument_list(text, open_paren)
        if count is None:
            continue
        line = text.count("\n", 0, match.start()) + 1
        calls.append((match.group(1), count, line))
    return tuple(calls)


def describe_signature_mismatches(
    label: str, text: str, signatures: dict[str, int]
) -> list[str]:
    problems: list[str] = []
    for name, count, line in bridge_call_arities(text):
        expected = signatures.get(name)
        if expected is None:
            problems.append(
                f"{label}:{line}: calls unknown recorder bridge entry point "
                f"{name}"
            )
        elif expected != count:
            problems.append(
                f"{label}:{line}: {name} is called with {count} arguments but "
                f"the bridge declares {expected}"
            )
    return problems


def current_hook_templates() -> dict[str, str]:
    """Returns the hook templates that must match the current bridge."""
    templates = {}
    for name, value in sorted(globals().items()):
        if not isinstance(value, str) or not name.isupper():
            continue
        if "a11y_recorder::" not in value:
            continue
        if name.startswith(HISTORICAL_TEMPLATE_PREFIXES):
            continue
        templates[name] = value
    return templates


def historical_hook_templates() -> dict[str, str]:
    """Returns the templates that describe superseded hook shapes."""
    return {
        name: value
        for name, value in sorted(globals().items())
        if isinstance(value, str)
        and name.isupper()
        and name.startswith(HISTORICAL_TEMPLATE_PREFIXES)
        and "a11y_recorder::" in value
    }


def verify_hook_templates(signatures: dict[str, int]) -> None:
    """Fails when a hook template disagrees with the bridge declarations.

    A hook template is the exact text this script writes into a Chromium
    source. The recorder bridge is copied into the checkout on every run, so a
    template that still carries a superseded call shape produces a Chromium
    build failure at the patched call site rather than an integration failure.
    This check moves that failure forward to integration time.
    """
    problems: list[str] = []
    for name, template in current_hook_templates().items():
        problems.extend(
            describe_signature_mismatches(name, template, signatures)
        )

    source = Path(__file__).resolve().read_text(encoding="utf-8")
    for name, template in historical_hook_templates().items():
        mismatched = describe_signature_mismatches(name, template, signatures)
        if not mismatched:
            continue
        if source.count(name) < 2:
            problems.append(
                f"{name}: describes a superseded call shape but is never used "
                "to upgrade an already-patched checkout"
            )
    if problems:
        raise RuntimeError(
            "recorder bridge signatures and hook templates disagree:\n  "
            + "\n  ".join(problems)
        )


def verify_integrated_sources(signatures: dict[str, int]) -> None:
    """Fails when a patched checkout still holds a superseded call shape.

    Presence guards in this script key on symbol names, so a hook body written
    by an earlier protocol revision can read as already integrated. This check
    inspects what the checkout actually contains after patching.
    """
    problems: list[str] = []
    for path in integrated_paths():
        text = read_source(path)
        if "a11y_recorder::" not in text:
            continue
        problems.extend(
            describe_signature_mismatches(str(path), text, signatures)
        )
        problems.extend(describe_unguarded_world_name_reads(str(path), text))
    if problems:
        raise RuntimeError(
            "patched Chromium sources disagree with the recorder bridge:\n  "
            + "\n  ".join(problems)
        )

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


def migrate_bridge_hook(text: str, path: Path) -> str:
    """Rewrites an existing bridge hook to the current body.

    Chromium checkouts are reused between revisions and the hook is inserted
    only when absent, so a checkout patched by an earlier revision keeps that
    revision's body. Earlier bodies have differed in the diagnostic they write
    and in the exit code they return, so this replaces the region the hook
    introduces rather than matching any particular earlier text.
    """
    start = text.find(BRIDGE_HOOK_ANCHOR)
    if start < 0:
        return text
    if text.find(BRIDGE_HOOK_ANCHOR, start + 1) >= 0:
        raise RuntimeError(
            f"{path}: more than one recorder bridge hook is present"
        )
    end = text.find(BRIDGE_HOOK_TERMINATOR, start)
    if end < 0:
        raise RuntimeError(
            f"{path}: the recorder bridge hook is not terminated"
        )
    end += len(BRIDGE_HOOK_TERMINATOR)
    body = text[start:end]
    if "a11y_recorder::InitializeProcessBridge(" not in body:
        raise RuntimeError(
            f"{path}: the recorder bridge hook does not initialize the bridge"
        )
    if body == HOOK:
        return text
    return f"{text[:start]}{HOOK}{text[end:]}"


def migrate_hook_region(
    text: str, path: Path, symbol: str, template: str
) -> str:
    """Rewrites the block that introduces one bridge call to the current body.

    Chromium checkouts are reused between revisions and a hook is inserted only
    when its symbol is absent, so a checkout patched by an earlier revision
    keeps that revision's body. Carrying a verbatim copy of every earlier body
    requires anticipating every shape ever written and does nothing when it
    guesses wrong, so the block that directly contains the call is located and
    replaced wholesale instead.
    """
    marker = f"a11y_recorder::{symbol}("
    occurrences = text.count(marker)
    if occurrences == 0:
        return text
    if occurrences > 1:
        raise RuntimeError(
            f"{path}: more than one {symbol} hook is present"
        )

    call = text.index(marker)
    depth = 0
    index = call
    while index > 0:
        index -= 1
        character = text[index]
        if character == "}":
            depth += 1
        elif character == "{":
            if depth == 0:
                break
            depth -= 1
    else:
        raise RuntimeError(
            f"{path}: the {symbol} hook is not inside a block"
        )

    open_brace = index
    line_start = text.rfind("\n", 0, open_brace) + 1
    depth = 0
    end = open_brace
    while end < len(text):
        character = text[end]
        if character == "{":
            depth += 1
        elif character == "}":
            depth -= 1
            if depth == 0:
                break
        end += 1
    else:
        raise RuntimeError(
            f"{path}: the {symbol} hook block is not terminated"
        )

    line_end = text.find("\n", end)
    if line_end < 0:
        raise RuntimeError(
            f"{path}: the {symbol} hook block is not terminated by a line"
        )
    region = text[line_start:line_end + 1]

    # The region must be a block this script introduced, not a Blink function
    # body that happens to contain the call. A hook block opens either on its
    # own line or on a condition this script wrote, and it contains no bridge
    # call other than the one being migrated.
    opening = region.split("\n", 1)[0].strip()
    if opening != "{" and not opening.startswith("if ("):
        raise RuntimeError(
            f"{path}: the {symbol} hook block does not open a hook region"
        )
    other_calls = [
        name
        for name, _, _ in bridge_call_arities(region)
        if name != symbol
    ]
    if other_calls:
        raise RuntimeError(
            f"{path}: the {symbol} hook region also calls "
            + ", ".join(sorted(set(other_calls)))
        )

    if region == template:
        return text
    return f"{text[:line_start]}{template}{text[line_end + 1:]}"


def verify_bridge_failure_is_fatal(text: str, path: Path) -> None:
    """Fails when a failed bridge would not exit with the failure code.

    The recorder detects a failed bridge only by the browser's exit code, so a
    hook body that returns a normal exit code makes a failed session look like a
    browser the operator closed. A hand-edited body that this script cannot
    migrate is reported here rather than at recording time.
    """
    marker = "a11y_recorder::InitializeProcessBridge("
    start = text.find(marker)
    if start < 0:
        raise RuntimeError(
            f"{path}: the recorder bridge initialization hook is missing"
        )
    end = text.find("#endif", start)
    region = text[start:end if end > start else len(text)]
    if BRIDGE_FAILURE_RETURN.strip() not in region:
        raise RuntimeError(
            f"{path}: a failed recorder bridge must return "
            "a11y_recorder::kBridgeInitializationFailureExitCode, so that a "
            "failed bridge is not reported as a normal browser exit"
        )


def verify_protocol_query_is_answered(text: str, path: Path) -> None:
    """Fails when the hook would not answer the protocol version query.

    The recorder asks the browser which protocol version it speaks before it
    starts a session, and treats a browser that does not answer as one whose
    version is unknown. A hook body that omits the query therefore degrades that
    check silently instead of failing, so the omission is reported here.
    """
    marker = "a11y_recorder::InitializeProcessBridge("
    start = text.find(marker)
    if start < 0:
        raise RuntimeError(
            f"{path}: the recorder bridge initialization hook is missing"
        )
    region_start = text.rfind(BRIDGE_HOOK_ANCHOR, 0, start)
    if region_start < 0:
        raise RuntimeError(
            f"{path}: the recorder bridge hook region could not be located"
        )
    end = text.find("#endif", start)
    region = text[region_start:end if end > start else len(text)]
    if "a11y_recorder::WriteProtocolVersionIfRequested(" not in region:
        raise RuntimeError(
            f"{path}: the recorder bridge hook must answer the protocol "
            "version query, so that the recorder can refuse a mismatched "
            "browser before it starts a session"
        )
    if PROTOCOL_VERSION_QUERY_RETURN.strip() not in region:
        raise RuntimeError(
            f"{path}: answering the protocol version query must return "
            "a11y_recorder::kProtocolVersionQueryExitCode, so that a browser "
            "which ignored the switch is not mistaken for one that answered"
        )


def patch_main_delegate(path: Path) -> None:
    text = read_source(path)
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

    # A checkout patched by an earlier revision already contains the hook, so
    # the presence guard below leaves it untouched. Migrate its body first.
    text = migrate_bridge_hook(text, path)

    if "InitializeProcessBridge" not in text.replace(
        BRIDGE_INCLUDE, ""
    ):
        function = (
            "std::optional<int> ChromeMainDelegate::BasicStartupComplete() {"
        )
        text = replace_once(text, function, f"{function}\n{HOOK}", path)
    verify_bridge_failure_is_fatal(text, path)
    verify_protocol_query_is_answered(text, path)
    write_patched(path, text)


def patch_chrome_build(path: Path) -> None:
    text = read_source(path)
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
    write_patched(path, text)


def remove_legacy_child_launcher_hook(path: Path) -> None:
    text = read_source(path)
    text = text.replace(f"{CHILD_LAUNCHER_INCLUDE}\n", "")
    text = text.replace(TRACED_CHILD_LAUNCHER_HOOK, "")
    text = text.replace(ORIGINAL_CHILD_LAUNCHER_HOOK, "")
    write_patched(path, text)


def patch_child_launcher(path: Path) -> None:
    text = read_source(path)
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
    write_patched(path, text)


def patch_content_browser_build(path: Path) -> None:
    text = read_source(path)
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
    write_patched(path, text)


def patch_content_renderer_accessibility(path: Path) -> None:
    text = read_source(path)
    if LEGACY_CONTENT_RENDERER_FOCUSED_EXPRESSION in text:
        text = replace_once(
            text,
            LEGACY_CONTENT_RENDERER_FOCUSED_EXPRESSION,
            CONTENT_RENDERER_FOCUSED_EXPRESSION,
            path,
        )
    if LEGACY_CONTENT_RENDERER_ROLE_EXPRESSION in text:
        text = replace_once(
            text,
            LEGACY_CONTENT_RENDERER_ROLE_EXPRESSION,
            CONTENT_RENDERER_ROLE_EXPRESSION,
            path,
        )
    header = (
        '#include "content/renderer/accessibility/'
        'render_accessibility_impl.h"\n'
    )
    if CONTENT_RENDERER_ACCESSIBILITY_INCLUDE not in text:
        text = replace_once(
            text,
            header,
            header + "\n#include <unordered_map>\n\n"
            + f"{CONTENT_RENDERER_ACCESSIBILITY_INCLUDE}\n"
            + f"{CONTENT_RENDERER_AX_ENUM_INCLUDE}\n",
            path,
        )
    else:
        # A checkout patched by an earlier protocol revision keeps its hook
        # body, so each include this revision requires is added separately
        # rather than as one block.
        if "#include <unordered_map>" not in text:
            text = replace_once(
                text,
                header,
                header + "\n#include <unordered_map>\n",
                path,
            )
        if CONTENT_RENDERER_AX_ENUM_INCLUDE not in text:
            text = replace_once(
                text,
                f"{CONTENT_RENDERER_ACCESSIBILITY_INCLUDE}\n",
                f"{CONTENT_RENDERER_ACCESSIBILITY_INCLUDE}\n"
                + f"{CONTENT_RENDERER_AX_ENUM_INCLUDE}\n",
                path,
            )
    if "BeginRendererAccessibilityCheckpoint" not in text:
        anchor = (
            "  ax_annotators_manager_->AddDebuggingAttributes("
            "updates_and_events.updates);\n"
        )
        text = replace_once(
            text,
            anchor,
            anchor + CONTENT_RENDERER_ACCESSIBILITY_HOOK,
            path,
        )
    write_patched(path, text)


def patch_content_renderer_build(path: Path) -> None:
    text = read_source(path)
    if CONTENT_RENDERER_DEP in text:
        return
    target = 'target(link_target_type, "renderer") {'
    target_index = text.find(target)
    if target_index < 0:
        raise RuntimeError(f"{path}: content renderer target not found")
    target_end = text.find("\n}", target_index)
    deps = text.find("  deps = [\n", target_index)
    if deps < 0 or (target_end >= 0 and deps > target_end):
        raise RuntimeError(f"{path}: content renderer deps list not found")
    opening = "  deps = [\n"
    text = text[:deps] + text[deps:].replace(
        opening, opening + f"{CONTENT_RENDERER_DEP}\n", 1
    )
    write_patched(path, text)


def patch_web_contents_navigation(path: Path) -> None:
    text = read_source(path)
    if CONTENT_NAVIGATION_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "content/browser/web_contents/web_contents_impl.h"\n',
            '#include "content/browser/web_contents/web_contents_impl.h"\n'
            f"{CONTENT_NAVIGATION_INCLUDE}\n",
            path,
        )

    if LEGACY_CONTENT_NAVIGATION_STARTED_HOOK in text:
        text = replace_once(
            text,
            LEGACY_CONTENT_NAVIGATION_STARTED_HOOK,
            CONTENT_NAVIGATION_STARTED_HOOK,
            path,
        )
    if LEGACY_CONTENT_NAVIGATION_COMPLETED_HOOK in text:
        text = replace_once(
            text,
            LEGACY_CONTENT_NAVIGATION_COMPLETED_HOOK,
            CONTENT_NAVIGATION_COMPLETED_HOOK,
            path,
        )
    if INTERMEDIATE_CONTENT_NAVIGATION_COMPLETED_HOOK in text:
        text = replace_once(
            text,
            INTERMEDIATE_CONTENT_NAVIGATION_COMPLETED_HOOK,
            CONTENT_NAVIGATION_COMPLETED_HOOK,
            path,
        )

    if "RecordBrowserNavigationStarted" not in text:
        start_anchor = (
            "  const GURL url = navigation_handle->GetURL();\n\n"
            "  base::ElapsedTimer duration;\n"
        )
        text = replace_once(
            text,
            start_anchor,
            "  const GURL url = navigation_handle->GetURL();\n\n"
            f"{CONTENT_NAVIGATION_STARTED_HOOK}\n"
            "  base::ElapsedTimer duration;\n",
            path,
        )

    if "RecordBrowserNavigationCompleted" not in text:
        finish_anchor = (
            '  TRACE_EVENT1("navigation", "WebContentsImpl::DidFinishNavigation",\n'
            '               "navigation_handle", navigation_handle);\n\n'
        )
        text = replace_once(
            text,
            finish_anchor,
            finish_anchor + CONTENT_NAVIGATION_COMPLETED_HOOK + "\n",
            path,
        )
    write_patched(path, text)


def ensure_checkpoint_attribute_includes(text: str, path: Path) -> str:
    """Makes the Attribute and Element declarations visible in a patched file.

    The checkpoint hooks read element attributes, so the patched translation
    unit needs both declarations even when upstream only pulled them in
    transitively. The includes are added after the bridge include, which every
    patched file already carries.
    """
    required = (
        '#include "third_party/blink/renderer/core/dom/attribute.h"',
        '#include "third_party/blink/renderer/core/dom/element.h"',
    )
    missing = [include for include in required if include not in text]
    if not missing:
        return text
    anchor = f"{BLINK_BRIDGE_INCLUDE}\n"
    return replace_once(
        text,
        anchor,
        anchor + "".join(f"{include}\n" for include in missing),
        path,
    )


def patch_blink_element(path: Path) -> None:
    text = read_source(path)
    if BLINK_BRIDGE_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "third_party/blink/renderer/core/dom/element.h"\n',
            '#include "third_party/blink/renderer/core/dom/element.h"\n'
            f"{BLINK_BRIDGE_INCLUDE}\n",
            path,
        )
    mutation_observer_include = (
        '#include "third_party/blink/renderer/core/dom/mutation_observer.h"'
    )
    if mutation_observer_include not in text:
        text = replace_once(
            text,
            f"{BLINK_BRIDGE_INCLUDE}\n",
            f"{BLINK_BRIDGE_INCLUDE}\n{mutation_observer_include}\n",
            path,
        )
    added_anchor = (
        "void Element::DidAddAttribute(const QualifiedName& name,\n"
        "                              const AtomicString& value) {\n"
    )
    if INTERMEDIATE_BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER in text:
        text = replace_once(
            text,
            INTERMEDIATE_BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER,
            BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER,
            path,
        )
    if "RecordRecorderElementAttributeMutation" not in text:
        text = replace_once(
            text,
            added_anchor,
            BLINK_ELEMENT_ATTRIBUTE_MUTATION_HELPER
            + "\n"
            + added_anchor
            + BLINK_ELEMENT_ATTRIBUTE_ADDED_HOOK,
            path,
        )
        modified_anchor = (
            "void Element::DidModifyAttribute(const QualifiedName& name,\n"
            "                                 const AtomicString& old_value,\n"
            "                                 const AtomicString& new_value,\n"
            "                                 AttributeModificationReason "
            "reason) {\n"
        )
        text = replace_once(
            text,
            modified_anchor,
            modified_anchor + BLINK_ELEMENT_ATTRIBUTE_MODIFIED_HOOK,
            path,
        )
        removed_anchor = (
            "void Element::DidRemoveAttribute(const QualifiedName& name,\n"
            "                                 const AtomicString& old_value) "
            "{\n"
        )
        text = replace_once(
            text,
            removed_anchor,
            removed_anchor + BLINK_ELEMENT_ATTRIBUTE_REMOVED_HOOK,
            path,
        )
    write_patched(path, text)


def patch_blink_character_data(path: Path) -> None:
    text = read_source(path)
    if BLINK_BRIDGE_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "third_party/blink/renderer/core/dom/character_data.h"\n',
            '#include "third_party/blink/renderer/core/dom/character_data.h"\n'
            f"{BLINK_BRIDGE_INCLUDE}\n",
            path,
        )
    for include in (
        '#include "third_party/blink/renderer/core/dom/document.h"',
        '#include "third_party/blink/renderer/core/dom/mutation_observer.h"',
    ):
        if include in text:
            continue
        text = replace_once(
            text,
            f"{BLINK_BRIDGE_INCLUDE}\n",
            f"{BLINK_BRIDGE_INCLUDE}\n{include}\n",
            path,
        )
    if INTERMEDIATE_BLINK_CHARACTER_DATA_MUTATION_HOOK in text:
        text = replace_once(
            text,
            INTERMEDIATE_BLINK_CHARACTER_DATA_MUTATION_HOOK,
            BLINK_CHARACTER_DATA_MUTATION_HOOK,
            path,
        )
    if "RecordBlinkDomCharacterDataChanged" not in text:
        anchor = "  String old_data = this->data();\n"
        text = replace_once(
            text,
            anchor,
            anchor + BLINK_CHARACTER_DATA_MUTATION_HOOK,
            path,
        )
    write_patched(path, text)


def patch_blink_event_target(path: Path) -> None:
    text = read_source(path)
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

    # Calling the virtuals that report how a listener was created needs the
    # listener definition, which this source reaches only indirectly.
    if BLINK_EVENT_LISTENER_INCLUDE not in text:
        text = replace_once(
            text,
            BLINK_BRIDGE_INCLUDE,
            f"{BLINK_BRIDGE_INCLUDE}\n{BLINK_EVENT_LISTENER_INCLUDE}",
            path,
        )

    # Reading where a registration came from needs Blink's capture helper and
    # the location type it returns, neither of which this source reaches on its
    # own.
    for include in (
        BLINK_CAPTURE_SOURCE_LOCATION_INCLUDE,
        BLINK_SOURCE_LOCATION_INCLUDE,
        BLINK_JS_BASED_EVENT_LISTENER_INCLUDE,
        BLINK_DOM_WRAPPER_WORLD_INCLUDE,
        BLINK_WTF_INCLUDE,
    ):
        if include in text:
            continue
        text = replace_once(
            text,
            BLINK_BRIDGE_INCLUDE,
            f"{BLINK_BRIDGE_INCLUDE}\n{include}",
            path,
        )
    if "RecorderListenerRegistrationKind(" not in text:
        anchor = "bool EventTarget::AddEventListenerInternal("
        text = replace_once(
            text,
            anchor,
            f"{BLINK_LISTENER_KIND_HELPER}{anchor}",
            path,
        )
    # A checkout patched before worlds were recorded already holds the
    # registration-kind helper, so the world helper needs its own guard rather
    # than riding along with that one. The guard reads the helper definition
    # rather than a call to it, so a checkout that holds hook bodies from more
    # than one revision still has the definition restored.
    if "const DOMWrapperWorld* RecorderListenerWorld(" not in text:
        anchor = "bool EventTarget::AddEventListenerInternal("
        text = replace_once(
            text,
            anchor,
            f"{BLINK_LISTENER_WORLD_HELPER}{anchor}",
            path,
        )
    if LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK in text:
        text = replace_once(
            text,
            LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK,
            BLINK_LISTENER_INVOCATION_STARTED_HOOK,
            path,
        )
    # A checkout patched by an earlier revision holds that revision's hook
    # bodies, and the presence guards below read them as already integrated.
    # Each listener hook body is replaced by rewriting the region it introduces,
    # so any earlier body converges on the current one without this script
    # having to carry a copy of every shape it has ever written.
    text = migrate_hook_region(
        text, path, "RecordBlinkListenerRegistered", BLINK_LISTENER_HOOK
    )
    text = migrate_hook_region(
        text, path, "RecordBlinkListenerRemoved", BLINK_LISTENER_REMOVED_HOOK
    )
    if LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK_NODE_ONLY in text:
        text = replace_once(
            text,
            LEGACY_BLINK_LISTENER_INVOCATION_STARTED_HOOK_NODE_ONLY,
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
    if "RecordBlinkListenerCallbackReplaced" not in text:
        text = replace_once(
            text,
            BLINK_LISTENER_ATTRIBUTE_REPLACEMENT_ANCHOR,
            BLINK_LISTENER_ATTRIBUTE_REPLACEMENT_HOOK,
            path,
        )
    else:
        text = migrate_hook_region(
            text,
            path,
            "RecordBlinkListenerCallbackReplaced",
            BLINK_LISTENER_ATTRIBUTE_REPLACEMENT_HOOK_REGION,
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
    write_patched(path, text)


def patch_blink_document(path: Path) -> None:
    text = read_source(path)
    if BLINK_BRIDGE_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "third_party/blink/renderer/core/dom/document.h"\n',
            '#include "third_party/blink/renderer/core/dom/document.h"\n'
            f"{BLINK_BRIDGE_INCLUDE}\n",
            path,
        )
    text = ensure_checkpoint_attribute_includes(text, path)
    if ORIGINAL_BLINK_DOM_CHECKPOINT_HOOK in text:
        text = replace_once(
            text,
            ORIGINAL_BLINK_DOM_CHECKPOINT_HOOK,
            BLINK_DOM_CHECKPOINT_HOOK,
            path,
        )
    if LEGACY_BLINK_DOM_CHECKPOINT_HOOK in text:
        text = replace_once(
            text,
            LEGACY_BLINK_DOM_CHECKPOINT_HOOK,
            BLINK_DOM_CHECKPOINT_HOOK,
            path,
        )
    if INTERMEDIATE_BLINK_DOM_CHECKPOINT_HOOK in text:
        text = replace_once(
            text,
            INTERMEDIATE_BLINK_DOM_CHECKPOINT_HOOK,
            BLINK_DOM_CHECKPOINT_HOOK,
            path,
        )
    if LEGACY_LIGHT_TREE_BLINK_DOM_CHECKPOINT_HOOK in text:
        text = replace_once(
            text,
            LEGACY_LIGHT_TREE_BLINK_DOM_CHECKPOINT_HOOK,
            BLINK_DOM_CHECKPOINT_HOOK,
            path,
        )
    text = add_includes_after(
        text, BLINK_BRIDGE_INCLUDE, BLINK_DOM_CHECKPOINT_INCLUDES, path
    )
    text = add_includes_after(
        text, BLINK_BRIDGE_INCLUDE, BLINK_INTERACTION_CHECKPOINT_INCLUDES, path
    )
    # A tree patched for protocol 0.28 holds a DOM helper that does not
    # follow its checkpoint with an interaction checkpoint.
    if LEGACY_SHADOW_TREE_BLINK_DOM_CHECKPOINT_HELPER in text:
        text = replace_once(
            text,
            LEGACY_SHADOW_TREE_BLINK_DOM_CHECKPOINT_HELPER,
            BLINK_DOM_CHECKPOINT_HELPER,
            path,
        )
    text = insert_before_once(
        text,
        "void Document::FinishedParsing() {\n",
        BLINK_DOM_CHECKPOINT_HELPER,
        BLINK_DOM_CHECKPOINT_HELPER_MARKER,
        path,
    )
    text = insert_before_once(
        text,
        "void Document::FinishedParsing() {\n",
        BLINK_INTERACTION_CHECKPOINT_HELPER,
        BLINK_INTERACTION_CHECKPOINT_HELPER_MARKER,
        path,
    )
    if BLINK_DOM_CHECKPOINT_HOOK not in text:
        anchor = "  DocumentParserTiming::From(*this).MarkParserStop();\n\n"
        text = replace_once(
            text,
            anchor,
            anchor + BLINK_DOM_CHECKPOINT_HOOK + "\n",
            path,
        )
    if "EnqueueRecorderDomCheckpoint(*this)" not in text:
        anchor = (
            "void Document::NotifyChangeChildren(\n"
            "    const ContainerNode& container,\n"
            "    const ContainerNode::ChildrenChange& change) {\n"
        )
        text = replace_once(
            text,
            anchor,
            anchor + BLINK_DOCUMENT_MUTATION_HOOK,
            path,
        )
    write_patched(path, text)


def patch_blink_mutation_observer_header(path: Path) -> None:
    text = read_source(path)
    if "EnqueueRecorderDomCheckpoint" not in text:
        anchor = "  static void EnqueueSlotChange(HTMLSlotElement&);\n"
        text = replace_once(
            text,
            anchor,
            (
                "  static void EnqueueRecorderDomCheckpoint(Document&);\n"
                + anchor
            ),
            path,
        )
    write_patched(path, text)


def patch_blink_mutation_observer(path: Path) -> None:
    text = read_source(path)
    if BLINK_BRIDGE_INCLUDE not in text:
        text = replace_once(
            text,
            '#include "third_party/blink/renderer/core/dom/mutation_observer.h"\n',
            '#include "third_party/blink/renderer/core/dom/mutation_observer.h"\n'
            f"{BLINK_BRIDGE_INCLUDE}\n",
            path,
        )
    node_traversal_include = (
        '#include "third_party/blink/renderer/core/dom/node_traversal.h"'
    )
    if node_traversal_include not in text:
        text = replace_once(
            text,
            '#include "third_party/blink/renderer/core/dom/node.h"\n',
            '#include "third_party/blink/renderer/core/dom/node.h"\n'
            f"{node_traversal_include}\n",
            path,
        )
    text = ensure_checkpoint_attribute_includes(text, path)
    if ORIGINAL_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK in text:
        text = replace_once(
            text,
            ORIGINAL_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
            BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
            path,
        )
    if LEGACY_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK in text:
        text = replace_once(
            text,
            LEGACY_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
            BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
            path,
        )
    if INTERMEDIATE_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK in text:
        text = replace_once(
            text,
            INTERMEDIATE_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
            BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
            path,
        )
    if LEGACY_LIGHT_TREE_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK in text:
        text = replace_once(
            text,
            LEGACY_LIGHT_TREE_BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
            BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
            path,
        )
    text = insert_before_once(
        text,
        "class MutationObserverAgentData",
        BLINK_DOM_CHECKPOINT_DECLARATION,
        "void RecorderRecordDomCheckpoint(",
        path,
    )
    if "recorder_mutated_documents" not in text:
        enqueue_condition = (
            "    if (active_mutation_observers_.empty() &&\n"
            "        active_slot_change_list_.empty()) {\n"
        )
        text = replace_once(
            text,
            enqueue_condition,
            (
                "    if (active_mutation_observers_.empty() &&\n"
                "        active_slot_change_list_.empty() &&\n"
                "        recorder_mutated_documents_.empty()) {\n"
            ),
            path,
        )
        trace_anchor = "    visitor->Trace(active_mutation_observers_);\n"
        text = replace_once(
            text,
            trace_anchor,
            trace_anchor + BLINK_MUTATION_AGENT_TRACE_HOOK,
            path,
        )
        method_anchor = "  void ActivateObserver(MutationObserver* observer) {\n"
        text = replace_once(
            text,
            method_anchor,
            BLINK_MUTATION_AGENT_METHOD + method_anchor,
            path,
        )
        collection_anchor = (
            "    MutationObserverVector observers(active_mutation_observers_);\n"
        )
        text = replace_once(
            text,
            collection_anchor,
            collection_anchor + BLINK_POST_MUTATION_DOM_CHECKPOINT_HOOK,
            path,
        )
        delivery_anchor = (
            "    for (const auto& slot : slots)\n"
            "      slot->DispatchSlotChangeEvent();\n"
        )
        text = replace_once(
            text,
            delivery_anchor,
            delivery_anchor
            + BLINK_POST_MUTATION_DOM_CHECKPOINT_DELIVERY_HOOK,
            path,
        )
        member_anchor = (
            "  MutationObserverSet active_mutation_observers_;\n"
        )
        text = replace_once(
            text,
            member_anchor,
            BLINK_MUTATION_AGENT_MEMBER + member_anchor,
            path,
        )
        observer_method_anchor = (
            "// static\n"
            "void MutationObserver::EnqueueSlotChange(HTMLSlotElement& slot) {\n"
        )
        text = replace_once(
            text,
            observer_method_anchor,
            BLINK_MUTATION_OBSERVER_METHOD + observer_method_anchor,
            path,
        )
    write_patched(path, text)


def patch_blink_event_dispatcher(path: Path) -> None:
    text = read_source(path)
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
    if (
        "RecordBlinkDispatchPathWindow" not in text
        and LEGACY_BLINK_DISPATCH_HOOK_WITHOUT_WINDOW in text
    ):
        text = replace_once(
            text,
            LEGACY_BLINK_DISPATCH_HOOK_WITHOUT_WINDOW,
            BLINK_DISPATCH_HOOK,
            path,
        )
    text = upgrade_legacy_hooks(
        text,
        ((LEGACY_BLINK_DISPATCH_HOOK_WITHOUT_SCOPES, BLINK_DISPATCH_HOOK),),
        path,
    )
    text = add_includes_after(
        text, BLINK_BRIDGE_INCLUDE, BLINK_DISPATCH_SCOPE_INCLUDES, path
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
    write_patched(path, text)


def patch_blink_dom_timer(path: Path) -> None:
    text = read_source(path)
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
    write_patched(path, text)


def patch_blink_animation_frame_callbacks(path: Path) -> None:
    text = read_source(path)
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
    write_patched(path, text)


def patch_blink_idle_callbacks(path: Path) -> None:
    text = read_source(path)
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
    write_patched(path, text)


def patch_blink_core_build(path: Path) -> None:
    text = read_source(path)
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
    write_patched(path, text)


def patch_blink_task_queue_throttler_header(path: Path) -> None:
    text = read_source(path)
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
        if LEGACY_BLINK_THROTTLER_OWNER_CONSTRUCTOR_DECLARATION in text:
            text = replace_once(
                text,
                LEGACY_BLINK_THROTTLER_OWNER_CONSTRUCTOR_DECLARATION,
                BLINK_THROTTLER_CONSTRUCTOR_DECLARATION,
                path,
            )
        else:
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
    write_patched(path, text)


def patch_blink_task_queue_throttler(path: Path) -> None:
    text = read_source(path)
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
        elif LEGACY_BLINK_THROTTLER_WEAK_CONSTRUCTOR_IMPLEMENTATION in text:
            text = replace_once(
                text,
                LEGACY_BLINK_THROTTLER_WEAK_CONSTRUCTOR_IMPLEMENTATION,
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
    write_patched(path, text)


def patch_blink_main_thread_task_queue(path: Path) -> None:
    text = read_source(path)
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
    write_patched(path, text)


def patch_blink_frame_scheduler_header(path: Path) -> None:
    text = read_source(path)
    if BLINK_FRAME_THROTTLING_ACCESSOR in text:
        return
    for old_accessor in (
        LEGACY_BLINK_FRAME_THROTTLING_ACCESSOR,
        INTERMEDIATE_BLINK_FRAME_THROTTLING_ACCESSOR,
    ):
        if old_accessor in text:
            text = replace_once(
                text,
                old_accessor,
                BLINK_FRAME_THROTTLING_ACCESSOR,
                path,
            )
            write_patched(path, text)
            return
    anchor = "  void UpdatePolicy();\n"
    text = replace_once(
        text,
        anchor,
        anchor + "\n" + BLINK_FRAME_THROTTLING_ACCESSOR,
        path,
    )
    write_patched(path, text)


def patch_blink_scheduler_build(path: Path) -> None:
    text = read_source(path)
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
    write_patched(path, text)


# Cookie operations. Every cookie hook forwards names and non-value attributes
# only. A hook that holds cookie text passes it through one of the bridge's
# reading functions, which return names and attributes and keep no value.
BLINK_COOKIE_ORIGIN_INCLUDES = (
    BLINK_CAPTURE_SOURCE_LOCATION_INCLUDE,
    BLINK_SOURCE_LOCATION_INCLUDE,
    BLINK_DOM_WRAPPER_WORLD_INCLUDE,
    BLINK_WTF_INCLUDE,
    '#include "v8/include/v8-isolate.h"',
)
BLINK_COOKIE_ORIGIN_HELPER = """\
namespace {

// Reports the recorder's name for a world type, following the listener hooks.
// The inspector's isolated worlds are tested before isolated worlds generally,
// because Blink classifies both as isolated.
const char* RecorderCookieWorldKind(const DOMWrapperWorld& world) {
  if (world.IsMainWorld()) {
    return a11y_recorder::kExecutionWorldKindMain;
  }
  if (world.GetWorldType() == DOMWrapperWorld::WorldType::kInspectorIsolated) {
    return a11y_recorder::kExecutionWorldKindInspectorIsolated;
  }
  if (world.IsIsolatedWorld()) {
    return a11y_recorder::kExecutionWorldKindIsolated;
  }
  if (world.IsWorkerOrWorkletWorld()) {
    return a11y_recorder::kExecutionWorldKindWorkerOrWorklet;
  }
  if (world.IsShadowRealmWorld()) {
    return a11y_recorder::kExecutionWorldKindShadowRealm;
  }
  return a11y_recorder::kExecutionWorldKindOther;
}

// Reads where a cookie call came from and which JavaScript world was current
// when it was made. A call made while no script context is entered reports no
// location and no world rather than the main world.
a11y_recorder::CookieCallOrigin RecorderCookieCallOrigin(
    ExecutionContext* context) {
  a11y_recorder::CookieCallOrigin origin;
  if (!context) {
    return origin;
  }
  v8::Isolate* isolate = context->GetIsolate();
  if (!isolate || !isolate->InContext()) {
    return origin;
  }
  if (SourceLocation* location = CaptureSourceLocation(context)) {
    origin.script_url = location->Url().Utf8();
    origin.function_name = location->Function().Utf8();
    origin.script_id = location->ScriptId();
    origin.line_number = static_cast<int>(location->LineNumber());
    origin.column_number = static_cast<int>(location->ColumnNumber());
  }
  const DOMWrapperWorld& world = DOMWrapperWorld::Current(isolate);
  origin.world_kind = RecorderCookieWorldKind(world);
  origin.world_id = world.GetWorldId();
  // Blink keeps isolated world names and stable identifiers in main-thread
  // maps, so a worker world reports neither.
  if (!world.IsMainWorld() && IsMainThread()) {
    origin.world_name = world.NonMainWorldHumanReadableName().Utf8();
    origin.world_stable_id = world.NonMainWorldStableId().Utf8();
  }
  return origin;
}

}  // namespace

"""
BLINK_DOCUMENT_COOKIE_HELPER = """\
namespace {

// Records one document.cookie read. The cookie string passes through the
// bridge's reader, which returns the names and keeps no value.
void RecorderRecordDocumentCookieRead(const Document& document,
                                      const char* outcome,
                                      const char* served_from,
                                      const String& cookie_string) {
  a11y_recorder::RecordBlinkDocumentCookieRead(
      const_cast<Document&>(document).GetDomNodeId(),
      document.Token().ToString(), document.CookieURL().GetString().Utf8(),
      outcome, served_from,
      a11y_recorder::ReadCookieNamesFromCookieString(cookie_string.Utf8()),
      RecorderCookieCallOrigin(document.GetExecutionContext()));
}

// Records one document.cookie assignment. The assignment passes through the
// bridge's reader, which returns the name and attributes and keeps no value.
void RecorderRecordDocumentCookieWrite(const Document& document,
                                       const char* outcome,
                                       const String& cookie_line) {
  a11y_recorder::RecordBlinkDocumentCookieWrite(
      const_cast<Document&>(document).GetDomNodeId(),
      document.Token().ToString(), document.CookieURL().GetString().Utf8(),
      outcome, a11y_recorder::ReadCookieWriteRequest(cookie_line.Utf8()),
      RecorderCookieCallOrigin(document.GetExecutionContext()));
}

}  // namespace

"""
BLINK_DOCUMENT_COOKIE_HELPER_MARKER = "void RecorderRecordDocumentCookieRead("
BLINK_COOKIE_ORIGIN_HELPER_MARKER = (
    "a11y_recorder::CookieCallOrigin RecorderCookieCallOrigin("
)

# Cookie jar anchors, in the order Blink reaches them.
BLINK_COOKIE_JAR_READ_NO_URL_ANCHOR = """\
  if (cookie_url.IsEmpty())
    return String();
"""
BLINK_COOKIE_JAR_READ_NO_URL_HOOK = """\
  if (cookie_url.IsEmpty()) {
    RecorderRecordDocumentCookieRead(
        *document_, a11y_recorder::kCookieOutcomeNoCookieUrl, "", String());
    return String();
  }
"""
BLINK_COOKIE_JAR_READ_FAILED_ANCHOR = """\
      InvalidateCache();
      return g_empty_string;
"""
BLINK_COOKIE_JAR_READ_FAILED_HOOK = """\
      InvalidateCache();
      RecorderRecordDocumentCookieRead(
          *document_, a11y_recorder::kCookieOutcomeCookieManagerCallFailed, "",
          String());
      return g_empty_string;
"""
BLINK_COOKIE_JAR_READ_RETURNED_ANCHOR = """\
  return last_cookies_;
}

bool CookieJar::CookiesEnabled() {
"""
BLINK_COOKIE_JAR_READ_RETURNED_HOOK = """\
  RecorderRecordDocumentCookieRead(
      *document_, a11y_recorder::kCookieOutcomeReturned,
      ipc_needed ? a11y_recorder::kCookieServedFromCookieManager
                 : a11y_recorder::kCookieServedFromRendererCache,
      last_cookies_);
  return last_cookies_;
}

bool CookieJar::CookiesEnabled() {
"""
BLINK_COOKIE_JAR_WRITE_NO_URL_ANCHOR = """\
  if (cookie_url.IsEmpty()) {
    return false;
  }
"""
BLINK_COOKIE_JAR_WRITE_NO_URL_HOOK = """\
  if (cookie_url.IsEmpty()) {
    RecorderRecordDocumentCookieWrite(
        *document_, a11y_recorder::kCookieOutcomeNoCookieUrl, value);
    return false;
  }
"""
BLINK_COOKIE_JAR_WRITE_SENT_ANCHOR = """\
      is_ad_tagged, apply_devtools_overrides, value);
  last_operation_was_set_ = true;
"""
BLINK_COOKIE_JAR_WRITE_SENT_HOOK = """\
      is_ad_tagged, apply_devtools_overrides, value);
  last_operation_was_set_ = true;
  RecorderRecordDocumentCookieWrite(
      *document_, a11y_recorder::kCookieOutcomeSentToCookieManager, value);
"""

# Document anchors for the refusals Blink makes before the cookie jar is
# reached.
BLINK_DOCUMENT_COOKIE_READ_DISABLED_ANCHOR = """\
  if (!dom_window_ || !GetSettings()->GetCookieEnabled())
    return String();

  CountUse(WebFeature::kCookieGet);
"""
BLINK_DOCUMENT_COOKIE_READ_DISABLED_HOOK = """\
  if (!dom_window_ || !GetSettings()->GetCookieEnabled()) {
    RecorderRecordDocumentCookieRead(
        *this, a11y_recorder::kCookieOutcomeCookiesDisabled, "", String());
    return String();
  }

  CountUse(WebFeature::kCookieGet);
"""
BLINK_DOCUMENT_COOKIE_READ_SECURITY_ANCHOR = """\
    return String();
  } else if (dom_window_->GetSecurityOrigin()->IsLocal()) {
    CountUse(WebFeature::kFileAccessedCookies);
"""
BLINK_DOCUMENT_COOKIE_READ_SECURITY_HOOK = """\
    RecorderRecordDocumentCookieRead(
        *this, a11y_recorder::kCookieOutcomeSecurityError, "", String());
    return String();
  } else if (dom_window_->GetSecurityOrigin()->IsLocal()) {
    CountUse(WebFeature::kFileAccessedCookies);
"""
BLINK_DOCUMENT_COOKIE_WRITE_DISABLED_ANCHOR = """\
  if (!dom_window_ || !GetSettings()->GetCookieEnabled())
    return;

  UseCounter::Count(*this, WebFeature::kCookieSet);
"""
BLINK_DOCUMENT_COOKIE_WRITE_DISABLED_HOOK = """\
  if (!dom_window_ || !GetSettings()->GetCookieEnabled()) {
    RecorderRecordDocumentCookieWrite(
        *this, a11y_recorder::kCookieOutcomeCookiesDisabled, value);
    return;
  }

  UseCounter::Count(*this, WebFeature::kCookieSet);
"""
BLINK_DOCUMENT_COOKIE_WRITE_SECURITY_ANCHOR = """\
    return;
  } else if (dom_window_->GetSecurityOrigin()->IsLocal()) {
    UseCounter::Count(*this, WebFeature::kFileAccessedCookies);
"""
BLINK_DOCUMENT_COOKIE_WRITE_SECURITY_HOOK = """\
    RecorderRecordDocumentCookieWrite(
        *this, a11y_recorder::kCookieOutcomeSecurityError, value);
    return;
  } else if (dom_window_->GetSecurityOrigin()->IsLocal()) {
    UseCounter::Count(*this, WebFeature::kFileAccessedCookies);
"""
BLINK_DOCUMENT_COOKIE_HELPER_ANCHOR = (
    "String Document::cookie(ExceptionState& exception_state) const {\n"
)

BLINK_COOKIE_STORE_HELPER = """\
namespace {

// Names the kind of script context a CookieStore belongs to.
const char* RecorderCookieStoreContextKind(ExecutionContext* context) {
  if (IsA<LocalDOMWindow>(context)) {
    return "window";
  }
  if (IsA<ServiceWorkerGlobalScope>(context)) {
    return "service-worker";
  }
  return "other";
}

// Returns the document of a window CookieStore. A service worker CookieStore
// has no document.
Document* RecorderCookieStoreDocument(ExecutionContext* context) {
  LocalDOMWindow* window = DynamicTo<LocalDOMWindow>(context);
  return window ? window->document() : nullptr;
}

// Records one Cookie Store API read call. A call that threw passes no
// resolver, since no result will follow it.
void RecorderRecordCookieStoreRead(uintptr_t resolver_identity,
                                   const char* method,
                                   ScriptState* script_state,
                                   const CookieStoreGetOptions* options,
                                   bool threw) {
  ExecutionContext* context = ExecutionContext::From(script_state);
  Document* document = RecorderCookieStoreDocument(context);
  a11y_recorder::RecordBlinkCookieStoreRead(
      threw ? 0 : resolver_identity, method,
      RecorderCookieStoreContextKind(context),
      document ? document->GetDomNodeId() : 0,
      document ? document->Token().ToString() : std::string(),
      options->hasName(),
      options->hasName() ? options->name().Utf8() : std::string(),
      options->hasUrl(),
      options->hasUrl() ? options->url().Utf8() : std::string(),
      threw ? a11y_recorder::kCookieOutcomeThrew
            : a11y_recorder::kCookieOutcomeSentToCookieManager,
      RecorderCookieCallOrigin(context));
}

// Reads the name and attributes of the write Blink built for a set or delete
// call. The value is never read.
a11y_recorder::CookieWriteRequest RecorderCookieStoreWriteRequest(
    const CookieInit* options) {
  a11y_recorder::CookieWriteRequest request;
  request.name = options->name().Utf8();
  request.domain_present = !options->domain().IsNull();
  request.domain = options->domain().Utf8();
  request.path_present = options->hasPath();
  request.path = options->path().Utf8();
  request.same_site_present = true;
  switch (options->sameSite().AsEnum()) {
    case V8CookieSameSite::Enum::kStrict:
      request.same_site = "strict";
      break;
    case V8CookieSameSite::Enum::kLax:
      request.same_site = "lax";
      break;
    case V8CookieSameSite::Enum::kNone:
      request.same_site = "none";
      break;
  }
  request.partitioned = options->partitioned();
  request.expires_present = options->expires().has_value();
  request.max_age_present = options->hasMaxAge();
  return request;
}

// Records one Cookie Store API write call after Blink has either sent it to
// the cookie manager or thrown.
void RecorderRecordCookieStoreWrite(const char* method,
                                    ScriptState* script_state,
                                    const CookieInit* options,
                                    bool threw) {
  ExecutionContext* context = ExecutionContext::From(script_state);
  Document* document = RecorderCookieStoreDocument(context);
  a11y_recorder::RecordBlinkCookieStoreWrite(
      method, RecorderCookieStoreContextKind(context),
      document ? document->GetDomNodeId() : 0,
      document ? document->Token().ToString() : std::string(), threw,
      RecorderCookieStoreWriteRequest(options),
      RecorderCookieCallOrigin(context));
}

// Records the names the cookie manager returned to a Cookie Store API read.
void RecorderRecordCookieStoreReadResult(
    uintptr_t resolver_identity,
    bool context_valid,
    const Vector<network::mojom::blink::CookieWithAccessResultPtr>&
        backend_cookies) {
  std::vector<std::string> recorder_names;
  recorder_names.reserve(backend_cookies.size());
  for (const auto& backend_cookie : backend_cookies) {
    recorder_names.push_back(backend_cookie->cookie.Name());
  }
  a11y_recorder::RecordBlinkCookieStoreReadResult(
      resolver_identity, context_valid, std::move(recorder_names));
}

// Names a cookie change cause. A cause this revision does not name is
// reported as other rather than as one it is not.
const char* RecorderCookieChangeCause(
    ::network::mojom::CookieChangeCause cause) {
  using Cause = ::network::mojom::CookieChangeCause;
  if (cause == Cause::INSERTED) {
    return "inserted";
  }
  if (cause == Cause::EXPLICIT) {
    return "explicit";
  }
  if (cause == Cause::UNKNOWN_DELETION) {
    return "unknown-deletion";
  }
  if (cause == Cause::OVERWRITE) {
    return "overwrite";
  }
  if (cause == Cause::EXPIRED) {
    return "expired";
  }
  if (cause == Cause::EVICTED) {
    return "evicted";
  }
  if (cause == Cause::EXPIRED_OVERWRITE) {
    return "expired-overwrite";
  }
  if (cause == Cause::INSERTED_NO_CHANGE_OVERWRITE) {
    return "inserted-no-change-overwrite";
  }
  if (cause == Cause::INSERTED_NO_VALUE_CHANGE_OVERWRITE) {
    return "inserted-no-value-change-overwrite";
  }
  return "other";
}

// Records one cookie change reported to a CookieStore with change listeners.
void RecorderRecordCookieStoreChange(
    ExecutionContext* context,
    const network::mojom::blink::CookieChangeInfoPtr& change,
    bool dispatched) {
  Document* document = RecorderCookieStoreDocument(context);
  a11y_recorder::RecordBlinkCookieStoreChange(
      RecorderCookieStoreContextKind(context),
      document ? document->GetDomNodeId() : 0,
      document ? document->Token().ToString() : std::string(),
      change->cookie.Name(), change->cookie.Domain(), change->cookie.Path(),
      RecorderCookieChangeCause(change->cause), dispatched);
}

}  // namespace

"""
BLINK_COOKIE_STORE_HELPER_MARKER = "void RecorderRecordCookieStoreRead("
BLINK_COOKIE_STORE_HELPER_ANCHOR = """\
ScriptPromise<IDLSequence<CookieListItem>> CookieStore::getAll(
    ScriptState* script_state,
    const String& name,
"""
BLINK_COOKIE_STORE_GET_ALL_ANCHOR = """\
         BindOnce(&CookieStore::GetAllForUrlToGetAllResult,
                  WrapPersistent(resolver)),
         exception_state);
"""
BLINK_COOKIE_STORE_GET_ALL_HOOK = """\
         BindOnce(&CookieStore::GetAllForUrlToGetAllResult,
                  WrapPersistent(resolver)),
         exception_state);
  RecorderRecordCookieStoreRead(reinterpret_cast<uintptr_t>(resolver),
                                "getAll", script_state, options,
                                exception_state.HadException());
"""
BLINK_COOKIE_STORE_GET_EMPTY_ANCHOR = """\
    exception_state.ThrowTypeError("CookieStoreGetOptions must not be empty");
"""
BLINK_COOKIE_STORE_GET_EMPTY_HOOK = """\
    exception_state.ThrowTypeError("CookieStoreGetOptions must not be empty");
    RecorderRecordCookieStoreRead(0, "get", script_state, options, true);
"""
BLINK_COOKIE_STORE_GET_ANCHOR = """\
      BindOnce(&CookieStore::GetAllForUrlToGetResult, WrapPersistent(resolver)),
      exception_state);
"""
BLINK_COOKIE_STORE_GET_HOOK = """\
      BindOnce(&CookieStore::GetAllForUrlToGetResult, WrapPersistent(resolver)),
      exception_state);
  RecorderRecordCookieStoreRead(reinterpret_cast<uintptr_t>(resolver), "get",
                                script_state, options,
                                exception_state.HadException());
"""
BLINK_COOKIE_STORE_SET_ANCHOR = """\
                    WebFeature::kCookieStoreAPI);

  return DoWrite(script_state, options, exception_state);
}
"""
BLINK_COOKIE_STORE_SET_HOOK = """\
                    WebFeature::kCookieStoreAPI);

  auto recorder_promise = DoWrite(script_state, options, exception_state);
  RecorderRecordCookieStoreWrite("set", script_state, options,
                                 exception_state.HadException());
  return recorder_promise;
}
"""
BLINK_COOKIE_STORE_DELETE_NAME_ANCHOR = """\
  set_options->setExpires(0);
  return DoWrite(script_state, set_options, exception_state);
}

ScriptPromise<IDLUndefined> CookieStore::Delete(
"""
BLINK_COOKIE_STORE_DELETE_NAME_HOOK = """\
  set_options->setExpires(0);
  auto recorder_promise = DoWrite(script_state, set_options, exception_state);
  RecorderRecordCookieStoreWrite("delete", script_state, set_options,
                                 exception_state.HadException());
  return recorder_promise;
}

ScriptPromise<IDLUndefined> CookieStore::Delete(
"""
BLINK_COOKIE_STORE_DELETE_OPTIONS_ANCHOR = """\
  set_options->setPartitioned(options->partitioned());
  return DoWrite(script_state, set_options, exception_state);
}
"""
BLINK_COOKIE_STORE_DELETE_OPTIONS_HOOK = """\
  set_options->setPartitioned(options->partitioned());
  auto recorder_promise = DoWrite(script_state, set_options, exception_state);
  RecorderRecordCookieStoreWrite("delete", script_state, set_options,
                                 exception_state.HadException());
  return recorder_promise;
}
"""
BLINK_COOKIE_STORE_READ_ALL_RESULT_ANCHOR = """\
void CookieStore::GetAllForUrlToGetAllResult(
    ScriptPromiseResolver<IDLSequence<CookieListItem>>* resolver,
    const Vector<network::mojom::blink::CookieWithAccessResultPtr>
        backend_cookies) {
"""
BLINK_COOKIE_STORE_READ_ONE_RESULT_ANCHOR = """\
void CookieStore::GetAllForUrlToGetResult(
    ScriptPromiseResolver<IDLNullable<CookieListItem>>* resolver,
    const Vector<network::mojom::blink::CookieWithAccessResultPtr>
        backend_cookies) {
"""
BLINK_COOKIE_STORE_READ_RESULT_HOOK = """\
  RecorderRecordCookieStoreReadResult(
      reinterpret_cast<uintptr_t>(resolver),
      resolver->GetScriptState()->ContextIsValid(), backend_cookies);
"""
BLINK_COOKIE_STORE_WRITE_NOTE_ANCHOR = """\
  auto* resolver = MakeGarbageCollected<ScriptPromiseResolver<IDLUndefined>>(
      script_state, exception_state.GetContext());
  backend_->SetCanonicalCookie(
"""
BLINK_COOKIE_STORE_WRITE_NOTE_HOOK = """\
  auto* resolver = MakeGarbageCollected<ScriptPromiseResolver<IDLUndefined>>(
      script_state, exception_state.GetContext());
  a11y_recorder::NoteBlinkCookieStoreWriteResolver(
      reinterpret_cast<uintptr_t>(resolver));
  backend_->SetCanonicalCookie(
"""
BLINK_COOKIE_STORE_WRITE_RESULT_ANCHOR = """\
    ScriptPromiseResolver<IDLUndefined>* resolver,
    bool backend_success) {
"""
BLINK_COOKIE_STORE_WRITE_RESULT_HOOK = """\
    ScriptPromiseResolver<IDLUndefined>* resolver,
    bool backend_success) {
  a11y_recorder::RecordBlinkCookieStoreWriteResult(
      reinterpret_cast<uintptr_t>(resolver), backend_success);
"""
BLINK_COOKIE_STORE_CHANGE_ANCHOR = """\
  CookieChangeEvent::ToEventInfo(change, changed, deleted);
"""
BLINK_COOKIE_STORE_CHANGE_HOOK = """\
  CookieChangeEvent::ToEventInfo(change, changed, deleted);
  RecorderRecordCookieStoreChange(GetExecutionContext(), change,
                                  !(changed.empty() && deleted.empty()));
"""
BLINK_COOKIE_STORE_BUILD_DEPS = (
    '  deps = [ "//third_party/blink/renderer/platform" ]\n'
)
BLINK_COOKIE_STORE_BUILD_PATCHED_DEPS = """\
  deps = [
    "//chromium/recorder_bridge",
    "//third_party/blink/renderer/platform",
  ]
"""

CONTENT_COOKIE_ACCESS_INCLUDES = (
    '#include "net/cookies/canonical_cookie.h"',
    '#include "net/cookies/cookie_constants.h"',
    '#include "net/cookies/cookie_inclusion_status.h"',
)
CONTENT_COOKIE_ACCESS_HELPER = """\
namespace {

// Copies the cookies of one network service access notification into the
// bridge's entry shape. A cookie Chromium parsed contributes its canonical
// attributes; a Set-Cookie line it could not parse contributes only the name
// read from the line. No cookie value is read.
std::vector<a11y_recorder::CookieAccessEntry> RecorderCookieAccessEntries(
    const network::mojom::CookieAccessDetailsPtr& details) {
  std::vector<a11y_recorder::CookieAccessEntry> entries;
  entries.reserve(details->cookie_list.size());
  const base::Time now = base::Time::Now();
  for (const auto& item : details->cookie_list) {
    a11y_recorder::CookieAccessEntry entry;
    if (item->cookie_or_line->is_cookie()) {
      const net::CanonicalCookie& cookie = item->cookie_or_line->get_cookie();
      entry.parsed = true;
      entry.name = cookie.Name();
      entry.domain = cookie.Domain();
      entry.path = cookie.Path();
      entry.same_site = net::CookieSameSiteToString(cookie.SameSite());
      entry.secure = cookie.SecureAttribute();
      entry.http_only = cookie.IsHttpOnly();
      entry.host_only = cookie.IsHostCookie();
      entry.partitioned = cookie.IsPartitioned();
      entry.persistent = cookie.IsPersistent();
      entry.expired = cookie.IsExpired(now);
    } else {
      entry.name = a11y_recorder::ReadCookieNameFromSetCookieLine(
          item->cookie_or_line->get_cookie_string());
    }
    entry.inclusion_status = item->access_result.status.GetDebugString();
    entries.push_back(std::move(entry));
  }
  return entries;
}

}  // namespace

"""
CONTENT_COOKIE_ACCESS_HELPER_MARKER = (
    "std::vector<a11y_recorder::CookieAccessEntry> RecorderCookieAccessEntries("
)
CONTENT_FRAME_COOKIE_ACCESS_ANCHOR = """\
    EmitCookieWarningsAndMetrics(/*rfh=*/this, details);
"""
CONTENT_FRAME_COOKIE_ACCESS_HOOK = """\
    {
      const base::Process& recorder_process = GetProcess()->GetProcess();
      a11y_recorder::RecordBrowserFrameCookieAccess(
          GetMainFrame()->GetFrameTreeNodeId().GetUnsafeValue(),
          GetFrameTreeNodeId().GetUnsafeValue(), GetNavigationId(),
          GetDocumentToken().ToString(),
          recorder_process.IsValid()
              ? static_cast<int>(recorder_process.Pid())
              : 0,
          details->type == network::mojom::CookieAccessDetails::Type::kChange,
          details->url.spec(),
          details->frame_origin ? details->frame_origin->Serialize()
                                : std::string(),
          details->top_frame_origin.Serialize(),
          details->devtools_request_id.value_or(std::string()),
          details->is_ad_tagged, RecorderCookieAccessEntries(details));
    }
    EmitCookieWarningsAndMetrics(/*rfh=*/this, details);
"""
CONTENT_FRAME_COOKIE_HELPER_ANCHOR = "void RenderFrameHostImpl::NotifyCookiesAccessed(\n"
CONTENT_NAVIGATION_COOKIE_ACCESS_ANCHOR = """\
    EmitCookieWarningsAndMetrics(frame_tree_node()->current_frame_host(),
                                 details);
"""
CONTENT_NAVIGATION_COOKIE_ACCESS_HOOK = """\
    {
      int recorder_page_frame_tree_node_id =
          GetFrameTreeNodeId().GetUnsafeValue();
      if (RenderFrameHostImpl* recorder_parent = GetParentFrame()) {
        recorder_page_frame_tree_node_id =
            recorder_parent->GetMainFrame()->GetFrameTreeNodeId()
                .GetUnsafeValue();
      }
      a11y_recorder::RecordBrowserNavigationCookieAccess(
          GetNavigationId(), recorder_page_frame_tree_node_id,
          GetFrameTreeNodeId().GetUnsafeValue(),
          details->type == network::mojom::CookieAccessDetails::Type::kChange,
          details->url.spec(),
          details->frame_origin ? details->frame_origin->Serialize()
                                : std::string(),
          details->top_frame_origin.Serialize(),
          details->devtools_request_id.value_or(std::string()),
          details->is_ad_tagged, RecorderCookieAccessEntries(details));
    }
    EmitCookieWarningsAndMetrics(frame_tree_node()->current_frame_host(),
                                 details);
"""
CONTENT_NAVIGATION_COOKIE_HELPER_ANCHOR = "void NavigationRequest::NotifyCookiesAccessed(\n"


def apply_cookie_hook(
    text: str, anchor: str, hook: str, path: Path
) -> str:
    """Replaces a cookie anchor with its hook unless the hook is present.

    Each cookie hook contains the text of its anchor or rewrites it in place,
    so the finished hook is its own presence guard and a second run leaves the
    source unchanged.
    """
    if hook in text:
        return text
    return replace_once(text, anchor, hook, path)


def add_includes_after(
    text: str, after: str, includes: tuple[str, ...], path: Path
) -> str:
    # Each include is written directly after the anchor, so they are visited
    # in reverse to leave them in the order given.
    for include in reversed(includes):
        if f"{include}\n" in text:
            continue
        text = replace_once(text, f"{after}\n", f"{after}\n{include}\n", path)
    return text


def insert_before_once(
    text: str, anchor: str, block: str, marker: str, path: Path
) -> str:
    if marker in text:
        return text
    return replace_once(text, anchor, f"{block}{anchor}", path)


def patch_blink_cookie_jar(path: Path) -> None:
    text = read_source(path)
    text = add_includes_after(
        text,
        '#include "third_party/blink/renderer/core/loader/cookie_jar.h"',
        (BLINK_BRIDGE_INCLUDE, *BLINK_COOKIE_ORIGIN_INCLUDES),
        path,
    )
    anchor = "// Controls whether we apply an artificial delay to priming the"
    text = insert_before_once(
        text,
        anchor,
        BLINK_COOKIE_ORIGIN_HELPER,
        BLINK_COOKIE_ORIGIN_HELPER_MARKER,
        path,
    )
    text = insert_before_once(
        text,
        anchor,
        BLINK_DOCUMENT_COOKIE_HELPER,
        BLINK_DOCUMENT_COOKIE_HELPER_MARKER,
        path,
    )
    for cookie_anchor, hook in (
        (BLINK_COOKIE_JAR_READ_NO_URL_ANCHOR, BLINK_COOKIE_JAR_READ_NO_URL_HOOK),
        (BLINK_COOKIE_JAR_READ_FAILED_ANCHOR, BLINK_COOKIE_JAR_READ_FAILED_HOOK),
        (
            BLINK_COOKIE_JAR_READ_RETURNED_ANCHOR,
            BLINK_COOKIE_JAR_READ_RETURNED_HOOK,
        ),
        (
            BLINK_COOKIE_JAR_WRITE_NO_URL_ANCHOR,
            BLINK_COOKIE_JAR_WRITE_NO_URL_HOOK,
        ),
        (BLINK_COOKIE_JAR_WRITE_SENT_ANCHOR, BLINK_COOKIE_JAR_WRITE_SENT_HOOK),
    ):
        text = apply_cookie_hook(text, cookie_anchor, hook, path)
    write_patched(path, text)


def patch_blink_document_cookie(path: Path) -> None:
    text = read_source(path)
    text = add_includes_after(
        text,
        BLINK_BRIDGE_INCLUDE,
        BLINK_COOKIE_ORIGIN_INCLUDES,
        path,
    )
    text = insert_before_once(
        text,
        BLINK_DOCUMENT_COOKIE_HELPER_ANCHOR,
        BLINK_COOKIE_ORIGIN_HELPER,
        BLINK_COOKIE_ORIGIN_HELPER_MARKER,
        path,
    )
    text = insert_before_once(
        text,
        BLINK_DOCUMENT_COOKIE_HELPER_ANCHOR,
        BLINK_DOCUMENT_COOKIE_HELPER,
        BLINK_DOCUMENT_COOKIE_HELPER_MARKER,
        path,
    )
    for cookie_anchor, hook in (
        (
            BLINK_DOCUMENT_COOKIE_READ_DISABLED_ANCHOR,
            BLINK_DOCUMENT_COOKIE_READ_DISABLED_HOOK,
        ),
        (
            BLINK_DOCUMENT_COOKIE_READ_SECURITY_ANCHOR,
            BLINK_DOCUMENT_COOKIE_READ_SECURITY_HOOK,
        ),
        (
            BLINK_DOCUMENT_COOKIE_WRITE_DISABLED_ANCHOR,
            BLINK_DOCUMENT_COOKIE_WRITE_DISABLED_HOOK,
        ),
        (
            BLINK_DOCUMENT_COOKIE_WRITE_SECURITY_ANCHOR,
            BLINK_DOCUMENT_COOKIE_WRITE_SECURITY_HOOK,
        ),
    ):
        text = apply_cookie_hook(text, cookie_anchor, hook, path)
    write_patched(path, text)


def patch_blink_cookie_store(path: Path) -> None:
    text = read_source(path)
    text = add_includes_after(
        text,
        '#include "third_party/blink/renderer/modules/cookie_store/'
        'cookie_store.h"',
        (BLINK_BRIDGE_INCLUDE, *BLINK_COOKIE_ORIGIN_INCLUDES),
        path,
    )
    text = insert_before_once(
        text,
        BLINK_COOKIE_STORE_HELPER_ANCHOR,
        BLINK_COOKIE_ORIGIN_HELPER,
        BLINK_COOKIE_ORIGIN_HELPER_MARKER,
        path,
    )
    text = insert_before_once(
        text,
        BLINK_COOKIE_STORE_HELPER_ANCHOR,
        BLINK_COOKIE_STORE_HELPER,
        BLINK_COOKIE_STORE_HELPER_MARKER,
        path,
    )
    for cookie_anchor, hook in (
        (BLINK_COOKIE_STORE_GET_ALL_ANCHOR, BLINK_COOKIE_STORE_GET_ALL_HOOK),
        (BLINK_COOKIE_STORE_GET_EMPTY_ANCHOR, BLINK_COOKIE_STORE_GET_EMPTY_HOOK),
        (BLINK_COOKIE_STORE_GET_ANCHOR, BLINK_COOKIE_STORE_GET_HOOK),
        (BLINK_COOKIE_STORE_SET_ANCHOR, BLINK_COOKIE_STORE_SET_HOOK),
        (
            BLINK_COOKIE_STORE_DELETE_NAME_ANCHOR,
            BLINK_COOKIE_STORE_DELETE_NAME_HOOK,
        ),
        (
            BLINK_COOKIE_STORE_DELETE_OPTIONS_ANCHOR,
            BLINK_COOKIE_STORE_DELETE_OPTIONS_HOOK,
        ),
        (
            BLINK_COOKIE_STORE_READ_ALL_RESULT_ANCHOR,
            BLINK_COOKIE_STORE_READ_ALL_RESULT_ANCHOR
            + BLINK_COOKIE_STORE_READ_RESULT_HOOK,
        ),
        (
            BLINK_COOKIE_STORE_READ_ONE_RESULT_ANCHOR,
            BLINK_COOKIE_STORE_READ_ONE_RESULT_ANCHOR
            + BLINK_COOKIE_STORE_READ_RESULT_HOOK,
        ),
        (
            BLINK_COOKIE_STORE_WRITE_NOTE_ANCHOR,
            BLINK_COOKIE_STORE_WRITE_NOTE_HOOK,
        ),
        (
            BLINK_COOKIE_STORE_WRITE_RESULT_ANCHOR,
            BLINK_COOKIE_STORE_WRITE_RESULT_HOOK,
        ),
        (BLINK_COOKIE_STORE_CHANGE_ANCHOR, BLINK_COOKIE_STORE_CHANGE_HOOK),
    ):
        text = apply_cookie_hook(text, cookie_anchor, hook, path)
    write_patched(path, text)


def patch_blink_cookie_store_build(path: Path) -> None:
    text = read_source(path)
    if '"//chromium/recorder_bridge"' in text:
        return
    text = replace_once(
        text,
        BLINK_COOKIE_STORE_BUILD_DEPS,
        BLINK_COOKIE_STORE_BUILD_PATCHED_DEPS,
        path,
    )
    write_patched(path, text)


def patch_content_cookie_access(
    path: Path,
    own_include: str,
    helper_anchor: str,
    hook_anchor: str,
    hook: str,
) -> None:
    text = read_source(path)
    text = add_includes_after(
        text,
        own_include,
        (CONTENT_NAVIGATION_INCLUDE, *CONTENT_COOKIE_ACCESS_INCLUDES),
        path,
    )
    text = insert_before_once(
        text,
        helper_anchor,
        CONTENT_COOKIE_ACCESS_HELPER,
        CONTENT_COOKIE_ACCESS_HELPER_MARKER,
        path,
    )
    text = apply_cookie_hook(text, hook_anchor, hook, path)
    write_patched(path, text)


def patch_content_frame_cookie_access(path: Path) -> None:
    patch_content_cookie_access(
        path,
        '#include "content/browser/renderer_host/render_frame_host_impl.h"',
        CONTENT_FRAME_COOKIE_HELPER_ANCHOR,
        CONTENT_FRAME_COOKIE_ACCESS_ANCHOR,
        CONTENT_FRAME_COOKIE_ACCESS_HOOK,
    )


def patch_content_navigation_cookie_access(path: Path) -> None:
    patch_content_cookie_access(
        path,
        '#include "content/browser/renderer_host/navigation_request.h"',
        CONTENT_NAVIGATION_COOKIE_HELPER_ANCHOR,
        CONTENT_NAVIGATION_COOKIE_ACCESS_ANCHOR,
        CONTENT_NAVIGATION_COOKIE_ACCESS_HOOK,
    )

# Interaction-state hooks. Each hook reports focus, selection, an element set
# as an active descendant by reflection, or a text-control value as Blink holds
# it once the change is committed. The script origin helper shared with the
# cookie hooks reports the calling script and world, and reports none for a
# change no script made.
BLINK_EXECUTION_CONTEXT_INCLUDE = (
    '#include "third_party/blink/renderer/core/execution_context/'
    'execution_context.h"'
)
BLINK_INTERACTION_INCLUDES = (
    BLINK_BRIDGE_INCLUDE,
    *BLINK_COOKIE_ORIGIN_INCLUDES,
    BLINK_EXECUTION_CONTEXT_INCLUDE,
)

BLINK_FOCUS_CHANGE_HELPER = """\
namespace {

// Defined with the cookie hooks further down this file, in the same unnamed
// namespace.
a11y_recorder::CookieCallOrigin
RecorderCookieCallOrigin(ExecutionContext* context);

const char* RecorderFocusTypeName(mojom::blink::FocusType type) {
  switch (type) {
    case mojom::blink::FocusType::kNone:
      return "none";
    case mojom::blink::FocusType::kScript:
      return "script";
    case mojom::blink::FocusType::kForward:
      return "forward";
    case mojom::blink::FocusType::kBackward:
      return "backward";
    case mojom::blink::FocusType::kSpatialNavigation:
      return "spatial-navigation";
    case mojom::blink::FocusType::kMouse:
      return "mouse";
    case mojom::blink::FocusType::kAccessKey:
      return "access-key";
    case mojom::blink::FocusType::kPage:
      return "page";
  }
  return "none";
}

int RecorderFocusNodeId(Element* element) {
  return element ? static_cast<int>(element->GetDomNodeId()) : 0;
}

// Records the outcome of one SetFocusedElement call when the call returns.
// Blur, focusout, focus, and focusin handlers run inside the call and can move
// focus again, and the call has several return paths, so the outcome is read
// from the document on the way out rather than at any one of them. The call's
// script origin is read on the way in, while the calling script is current.
class RecorderFocusChangeScope {
  STACK_ALLOCATED();

 public:
  RecorderFocusChangeScope(Document& document,
                           Element* previous,
                           Element* requested,
                           const FocusParams& params)
      : document_(document),
        previous_node_id_(RecorderFocusNodeId(previous)),
        requested_node_id_(RecorderFocusNodeId(requested)),
        focus_type_(RecorderFocusTypeName(params.type)),
        focus_trigger_(params.focus_trigger == FocusTrigger::kUserGesture
                           ? "user-gesture"
                           : "script"),
        prevent_scroll_(params.options && params.options->preventScroll()),
        focus_visible_present_(params.options &&
                               params.options->hasFocusVisible()),
        focus_visible_(focus_visible_present_ &&
                       params.options->focusVisible()),
        origin_(RecorderCookieCallOrigin(document.GetExecutionContext())) {}
  RecorderFocusChangeScope(const RecorderFocusChangeScope&) = delete;
  RecorderFocusChangeScope& operator=(const RecorderFocusChangeScope&) =
      delete;

  ~RecorderFocusChangeScope() {
    const int document_node_id =
        static_cast<int>(document_.GetDomNodeId());
    if (document_node_id <= 0) {
      return;
    }
    Element* focused = document_.FocusedElement();
    Element* active_descendant =
        focused ? focused->GetElementAttribute(
                      html_names::kAriaActivedescendantAttr)
                : nullptr;
    a11y_recorder::RecordBlinkFocusChanged(
        document_node_id, document_.Token().ToString(), previous_node_id_,
        requested_node_id_, RecorderFocusNodeId(focused),
        RecorderFocusNodeId(active_descendant), focus_type_, focus_trigger_,
        prevent_scroll_, focus_visible_present_, focus_visible_,
        std::move(origin_));
  }

 private:
  Document& document_;
  const int previous_node_id_;
  const int requested_node_id_;
  const char* const focus_type_;
  const char* const focus_trigger_;
  const bool prevent_scroll_;
  const bool focus_visible_present_;
  const bool focus_visible_;
  a11y_recorder::CookieCallOrigin origin_;
};

}  // namespace

"""
BLINK_FOCUS_CHANGE_HELPER_MARKER = "class RecorderFocusChangeScope {"
BLINK_FOCUS_CHANGE_HELPER_ANCHOR = (
    "void Document::SetLastFocusType(mojom::blink::FocusType last_focus_type) "
    "{\n"
)
BLINK_FOCUS_CHANGE_ANCHOR = """\
  bool focus_change_blocked = false;
  Element* old_focused_element = focused_element_;
"""
BLINK_FOCUS_CHANGE_HOOK = """\
  // Reports this focus change once every return path below has been taken.
  RecorderFocusChangeScope recorder_focus_change_scope(
      *this, focused_element_.Get(), new_focused_element, params);
  bool focus_change_blocked = false;
  Element* old_focused_element = focused_element_;
"""

BLINK_SELECTION_CHANGE_HELPER = """\
namespace {

int RecorderSelectionNodeId(const Position& position) {
  Node* node = position.ComputeContainerNode();
  return node ? static_cast<int>(node->GetDomNodeId()) : 0;
}

int RecorderSelectionOffset(const Position& position) {
  return position.IsNull()
             ? 0
             : static_cast<int>(position.ComputeOffsetInContainerNode());
}

// Records the selection the frame holds once a set-selection call has been
// committed and focus has followed it.
void RecorderRecordSelectionChanged(Document& document,
                                    const SelectionInDomTree& selection,
                                    bool set_by_user,
                                    bool directional) {
  const int document_node_id = static_cast<int>(document.GetDomNodeId());
  if (document_node_id <= 0) {
    return;
  }
  const char* selection_type = selection.IsNone()    ? "none"
                               : selection.IsCaret() ? "caret"
                                                     : "range";
  TextControlElement* text_control =
      selection.IsNone() ? nullptr : EnclosingTextControl(selection.Anchor());
  if (text_control && !text_control->IsTextControl()) {
    text_control = nullptr;
  }
  a11y_recorder::RecordBlinkSelectionChanged(
      document_node_id, document.Token().ToString(),
      set_by_user ? "user" : "system", selection_type,
      RecorderSelectionNodeId(selection.Anchor()),
      RecorderSelectionOffset(selection.Anchor()),
      RecorderSelectionNodeId(selection.Focus()),
      RecorderSelectionOffset(selection.Focus()), directional,
      text_control ? static_cast<int>(text_control->GetDomNodeId()) : 0,
      text_control ? static_cast<int>(text_control->selectionStart()) : 0,
      text_control ? static_cast<int>(text_control->selectionEnd()) : 0,
      text_control ? text_control->selectionDirection().Utf8()
                   : std::string(),
      RecorderCookieCallOrigin(document.GetExecutionContext()));
}

}  // namespace

"""
BLINK_SELECTION_CHANGE_HELPER_MARKER = "void RecorderRecordSelectionChanged("
BLINK_SELECTION_CHANGE_HELPER_ANCHOR = (
    "void FrameSelection::DidSetSelectionDeprecated(\n"
)
BLINK_SELECTION_CHANGE_ANCHOR = """\
  NotifyAccessibilityForSelectionChange();
  NotifyCompositorForSelectionChange();
  NotifyEventHandlerForSelectionChange();
"""
BLINK_SELECTION_CHANGE_HOOK = """\
  RecorderRecordSelectionChanged(GetDocument(), GetSelectionInDomTree(),
                                 set_selection_by == SetSelectionBy::kUser,
                                 options.IsDirectional());
  NotifyAccessibilityForSelectionChange();
  NotifyCompositorForSelectionChange();
  NotifyEventHandlerForSelectionChange();
"""

BLINK_TEXT_CONTROL_VALUE_HELPER = """\
namespace {

// Records a text control's value after a value set or a user edit changed it.
// The value is recorded verbatim up to the DOM value bound, as DOM attribute
// values and character data are, so a password typed during a test session is
// recorded.
void RecorderRecordTextControlValue(TextControlElement& control,
                                    const char* source) {
  constexpr int kRecorderMaximumTextControlValueLength = 4096;
  if (!control.IsTextControl()) {
    return;
  }
  Document& document = control.GetDocument();
  const int document_node_id = static_cast<int>(document.GetDomNodeId());
  if (document_node_id <= 0) {
    return;
  }
  const String value = control.Value();
  const int value_length = static_cast<int>(value.length());
  const bool value_truncated =
      value_length > kRecorderMaximumTextControlValueLength;
  const String recorded_value =
      value_truncated ? value.substr(0, kRecorderMaximumTextControlValueLength)
                      : value;
  a11y_recorder::RecordBlinkTextControlValueChanged(
      document_node_id, document.Token().ToString(),
      static_cast<int>(control.GetDomNodeId()),
      control.FormControlTypeAsString().Utf8(), source,
      recorded_value.Utf8(), value_length, value_truncated,
      kRecorderMaximumTextControlValueLength,
      static_cast<int>(control.selectionStart()),
      static_cast<int>(control.selectionEnd()),
      control.selectionDirection().Utf8(),
      RecorderCookieCallOrigin(document.GetExecutionContext()));
}

}  // namespace

"""
BLINK_TEXT_CONTROL_VALUE_HELPER_MARKER = "void RecorderRecordTextControlValue("

BLINK_INPUT_SET_VALUE_HELPER_ANCHOR = (
    "void HTMLInputElement::SetValue(const String& value,\n"
)
BLINK_INPUT_SET_VALUE_ANCHOR = """\
    input_type_view_->DidSetValue(sanitized_value, value_changed);
"""
BLINK_INPUT_SET_VALUE_HOOK = """\
    input_type_view_->DidSetValue(sanitized_value, value_changed);
    if (value_changed && IsTextField()) {
      RecorderRecordTextControlValue(*this, "value-set");
    }
"""

BLINK_TEXT_FIELD_EDIT_HELPER_ANCHOR = (
    "void TextFieldInputType::SubtreeHasChanged() {\n"
)
BLINK_TEXT_FIELD_EDIT_ANCHOR = """\
  GetElement().SetValueFromRenderer(SanitizeUserInputValue(
      ConvertFromVisibleValue(GetElement().InnerEditorValue())));
"""
BLINK_TEXT_FIELD_EDIT_HOOK = """\
  GetElement().SetValueFromRenderer(SanitizeUserInputValue(
      ConvertFromVisibleValue(GetElement().InnerEditorValue())));
  RecorderRecordTextControlValue(GetElement(), "user-edit");
"""

BLINK_TEXT_AREA_HELPER_ANCHOR = "void HTMLTextAreaElement::SubtreeHasChanged() {\n"
BLINK_TEXT_AREA_EDIT_ANCHOR = """\
  UpdateValue();
  CheckIfValueWasReverted(Value());
"""
BLINK_TEXT_AREA_EDIT_HOOK = """\
  UpdateValue();
  CheckIfValueWasReverted(Value());
  RecorderRecordTextControlValue(*this, "user-edit");
"""
BLINK_TEXT_AREA_SET_VALUE_ANCHOR = """\
  SetAutofillState(autofill_state);
  NotifyFormStateChanged();
  switch (event_behavior) {
"""
BLINK_TEXT_AREA_SET_VALUE_HOOK = """\
  SetAutofillState(autofill_state);
  NotifyFormStateChanged();
  RecorderRecordTextControlValue(*this, "value-set");
  switch (event_behavior) {
"""

BLINK_ACTIVE_DESCENDANT_HELPER = """\
namespace {

// Records an element stored as an aria-activedescendant by reflection. The
// content attribute reflection writes is empty, so the referenced element is
// not part of the recorded attribute state.
void RecorderRecordActiveDescendantReferenceSet(Element& element,
                                                Element& referenced) {
  Document& document = element.GetDocument();
  const int document_node_id = static_cast<int>(document.GetDomNodeId());
  if (document_node_id <= 0) {
    return;
  }
  a11y_recorder::RecordBlinkActiveDescendantReferenceSet(
      document_node_id, document.Token().ToString(),
      static_cast<int>(element.GetDomNodeId()),
      static_cast<int>(referenced.GetDomNodeId()),
      RecorderCookieCallOrigin(document.GetExecutionContext()));
}

}  // namespace

"""
BLINK_ACTIVE_DESCENDANT_HELPER_MARKER = (
    "void RecorderRecordActiveDescendantReferenceSet("
)
BLINK_ACTIVE_DESCENDANT_HELPER_ANCHOR = (
    "void Element::SetElementAttribute(const QualifiedName& name, "
    "Element* element) {\n"
)
BLINK_ACTIVE_DESCENDANT_ANCHOR = """\
  result.stored_value->value->insert(element);
"""
BLINK_ACTIVE_DESCENDANT_HOOK = """\
  result.stored_value->value->insert(element);
  if (name == html_names::kAriaActivedescendantAttr) {
    RecorderRecordActiveDescendantReferenceSet(*this, *element);
  }
"""


def patch_blink_interaction_source(
    path: Path,
    own_include: str | None,
    helpers: tuple[tuple[str, str, str], ...],
    hooks: tuple[tuple[str, str], ...],
) -> None:
    """Adds the interaction-state helpers and hooks to one Blink source.

    Each helper is written before its anchor unless its marker is present, and
    each hook contains its anchor, so a second run leaves the source unchanged.
    """
    text = read_source(path)
    if own_include is None:
        text = add_includes_after(
            text,
            BLINK_BRIDGE_INCLUDE,
            BLINK_INTERACTION_INCLUDES[1:],
            path,
        )
    else:
        text = add_includes_after(
            text, own_include, BLINK_INTERACTION_INCLUDES, path
        )
    for anchor, helper, marker in helpers:
        text = insert_before_once(text, anchor, helper, marker, path)
    for anchor, hook in hooks:
        text = apply_cookie_hook(text, anchor, hook, path)
    write_patched(path, text)


def patch_blink_document_focus(path: Path) -> None:
    # The cookie patch has already given this file the bridge include and the
    # script origin helper, which the focus helper declares ahead of its use.
    patch_blink_interaction_source(
        path,
        None,
        (
            (
                BLINK_FOCUS_CHANGE_HELPER_ANCHOR,
                BLINK_FOCUS_CHANGE_HELPER,
                BLINK_FOCUS_CHANGE_HELPER_MARKER,
            ),
        ),
        ((BLINK_FOCUS_CHANGE_ANCHOR, BLINK_FOCUS_CHANGE_HOOK),),
    )


def patch_blink_frame_selection(path: Path) -> None:
    patch_blink_interaction_source(
        path,
        '#include "third_party/blink/renderer/core/editing/frame_selection.h"',
        (
            (
                BLINK_SELECTION_CHANGE_HELPER_ANCHOR,
                BLINK_COOKIE_ORIGIN_HELPER,
                BLINK_COOKIE_ORIGIN_HELPER_MARKER,
            ),
            (
                BLINK_SELECTION_CHANGE_HELPER_ANCHOR,
                BLINK_SELECTION_CHANGE_HELPER,
                BLINK_SELECTION_CHANGE_HELPER_MARKER,
            ),
        ),
        ((BLINK_SELECTION_CHANGE_ANCHOR, BLINK_SELECTION_CHANGE_HOOK),),
    )


def text_control_helpers(anchor: str) -> tuple[tuple[str, str, str], ...]:
    return (
        (anchor, BLINK_COOKIE_ORIGIN_HELPER, BLINK_COOKIE_ORIGIN_HELPER_MARKER),
        (
            anchor,
            BLINK_TEXT_CONTROL_VALUE_HELPER,
            BLINK_TEXT_CONTROL_VALUE_HELPER_MARKER,
        ),
    )


def patch_blink_input_element(path: Path) -> None:
    patch_blink_interaction_source(
        path,
        '#include "third_party/blink/renderer/core/html/forms/'
        'html_input_element.h"',
        text_control_helpers(BLINK_INPUT_SET_VALUE_HELPER_ANCHOR),
        ((BLINK_INPUT_SET_VALUE_ANCHOR, BLINK_INPUT_SET_VALUE_HOOK),),
    )


def patch_blink_text_field_input_type(path: Path) -> None:
    patch_blink_interaction_source(
        path,
        '#include "third_party/blink/renderer/core/html/forms/'
        'text_field_input_type.h"',
        text_control_helpers(BLINK_TEXT_FIELD_EDIT_HELPER_ANCHOR),
        ((BLINK_TEXT_FIELD_EDIT_ANCHOR, BLINK_TEXT_FIELD_EDIT_HOOK),),
    )


def patch_blink_text_area_element(path: Path) -> None:
    patch_blink_interaction_source(
        path,
        '#include "third_party/blink/renderer/core/html/forms/'
        'html_text_area_element.h"',
        text_control_helpers(BLINK_TEXT_AREA_HELPER_ANCHOR),
        (
            (BLINK_TEXT_AREA_EDIT_ANCHOR, BLINK_TEXT_AREA_EDIT_HOOK),
            (BLINK_TEXT_AREA_SET_VALUE_ANCHOR, BLINK_TEXT_AREA_SET_VALUE_HOOK),
        ),
    )


def patch_blink_element_active_descendant(path: Path) -> None:
    # The DOM attribute patch has already given this file the bridge include.
    patch_blink_interaction_source(
        path,
        None,
        (
            (
                BLINK_ACTIVE_DESCENDANT_HELPER_ANCHOR,
                BLINK_COOKIE_ORIGIN_HELPER,
                BLINK_COOKIE_ORIGIN_HELPER_MARKER,
            ),
            (
                BLINK_ACTIVE_DESCENDANT_HELPER_ANCHOR,
                BLINK_ACTIVE_DESCENDANT_HELPER,
                BLINK_ACTIVE_DESCENDANT_HELPER_MARKER,
            ),
        ),
        ((BLINK_ACTIVE_DESCENDANT_ANCHOR, BLINK_ACTIVE_DESCENDANT_HOOK),),
    )


# The computed-style properties a layout checkpoint records, in the order the
# checkpoint lists them. The list and its rationale are documented in
# docs/architecture/layout-and-style-checkpoint-evidence-model.md, and the
# verifier compares the recorded list against the same names.
LAYOUT_STYLE_PROPERTIES = (
    "display",
    "visibility",
    "opacity",
    "position",
    "top",
    "right",
    "bottom",
    "left",
    "z-index",
    "float",
    "box-sizing",
    "width",
    "height",
    "min-width",
    "min-height",
    "max-width",
    "max-height",
    "overflow-x",
    "overflow-y",
    "clip",
    "clip-path",
    "text-overflow",
    "content-visibility",
    "transform",
    "filter",
    "margin-top",
    "margin-right",
    "margin-bottom",
    "margin-left",
    "padding-top",
    "padding-right",
    "padding-bottom",
    "padding-left",
    "border-top-width",
    "border-right-width",
    "border-bottom-width",
    "border-left-width",
    "border-top-style",
    "border-right-style",
    "border-bottom-style",
    "border-left-style",
    "border-top-color",
    "border-right-color",
    "border-bottom-color",
    "border-left-color",
    "outline-style",
    "outline-width",
    "outline-color",
    "outline-offset",
    "box-shadow",
    "text-shadow",
    "color",
    "background-color",
    "background-image",
    "font-family",
    "font-size",
    "font-weight",
    "font-style",
    "line-height",
    "letter-spacing",
    "word-spacing",
    "text-transform",
    "text-decoration-line",
    "text-align",
    "text-indent",
    "white-space-collapse",
    "text-wrap-mode",
    "direction",
    "writing-mode",
    "cursor",
    "pointer-events",
    "animation-name",
    "animation-duration",
    "transition-property",
    "transition-duration",
    # Color, contrast, blending, and forced colors.
    "-webkit-tap-highlight-color",
    "-webkit-text-fill-color",
    "accent-color",
    "backdrop-filter",
    "background-blend-mode",
    "caret-color",
    "color-scheme",
    "dynamic-range-limit",
    "forced-color-adjust",
    "isolation",
    "mix-blend-mode",
    "print-color-adjust",
    # Text decoration, underline, stroke, and emphasis.
    "-webkit-text-decorations-in-effect",
    "-webkit-text-stroke-color",
    "-webkit-text-stroke-width",
    "text-decoration",
    "text-decoration-color",
    "text-decoration-skip-ink",
    "text-decoration-skip-spaces",
    "text-decoration-style",
    "text-decoration-thickness",
    "text-emphasis-color",
    "text-emphasis-position",
    "text-emphasis-style",
    "text-underline-offset",
    "text-underline-position",
    # Text layout, wrapping, breaking, spacing, and bidirectional ordering.
    "-webkit-line-break",
    "-webkit-line-clamp",
    "-webkit-rtl-ordering",
    "-webkit-ruby-position",
    "-webkit-text-combine",
    "-webkit-text-orientation",
    "-webkit-text-security",
    "-webkit-writing-mode",
    "alignment-baseline",
    "baseline-shift",
    "baseline-source",
    "content",
    "dominant-baseline",
    "hyphenate-character",
    "hyphenate-limit-chars",
    "hyphens",
    "initial-letter",
    "line-break",
    "orphans",
    "overflow-wrap",
    "quotes",
    "ruby-align",
    "ruby-overhang",
    "ruby-position",
    "tab-size",
    "text-align-last",
    "text-anchor",
    "text-autospace",
    "text-box-edge",
    "text-box-trim",
    "text-combine-upright",
    "text-fit",
    "text-justify",
    "text-orientation",
    "text-spacing-trim",
    "text-wrap-style",
    "unicode-bidi",
    "vertical-align",
    "widows",
    "word-break",
    # Reading order, interaction, and form-control presentation.
    "-webkit-user-drag",
    "-webkit-user-modify",
    "app-region",
    "appearance",
    "caret-animation",
    "caret-shape",
    "field-sizing",
    "interactivity",
    "interest-delay-end",
    "interest-delay-start",
    "reading-flow",
    "reading-order",
    "resize",
    "speak",
    "touch-action",
    "user-select",
    "window-drag",
    # Sizing, containment, lists and counters, and anchor positioning.
    "anchor-name",
    "anchor-scope",
    "aspect-ratio",
    "break-after",
    "break-before",
    "break-inside",
    "buffered-rendering",
    "caption-side",
    "clear",
    "contain",
    "contain-intrinsic-height",
    "contain-intrinsic-size",
    "contain-intrinsic-width",
    "container-name",
    "container-type",
    "counter-increment",
    "counter-reset",
    "counter-set",
    "empty-cells",
    "frame-sizing",
    "image-orientation",
    "image-rendering",
    "interpolate-size",
    "list-style-image",
    "list-style-position",
    "list-style-type",
    "margin-trim",
    "object-fit",
    "object-position",
    "object-view-box",
    "overflow-anchor",
    "overflow-clip-margin",
    "overlay",
    "position-anchor",
    "position-area",
    "position-try-fallbacks",
    "position-try-order",
    "position-visibility",
    "shape-image-threshold",
    "shape-margin",
    "shape-outside",
    "table-layout",
    "will-change",
    "zoom",
    # Transforms and motion paths.
    "backface-visibility",
    "offset-anchor",
    "offset-distance",
    "offset-path",
    "offset-position",
    "offset-rotate",
    "perspective",
    "perspective-origin",
    "rotate",
    "scale",
    "transform-box",
    "transform-origin",
    "transform-style",
    "translate",
    # Scrolling, scroll snapping, and scrollbars.
    "overscroll-behavior-block",
    "overscroll-behavior-inline",
    "overscroll-behavior-x",
    "overscroll-behavior-y",
    "scroll-axis-lock",
    "scroll-behavior",
    "scroll-initial-target",
    "scroll-margin-block-end",
    "scroll-margin-block-start",
    "scroll-margin-bottom",
    "scroll-margin-inline-end",
    "scroll-margin-inline-start",
    "scroll-margin-left",
    "scroll-margin-right",
    "scroll-margin-top",
    "scroll-marker-group",
    "scroll-padding-block-end",
    "scroll-padding-block-start",
    "scroll-padding-bottom",
    "scroll-padding-inline-end",
    "scroll-padding-inline-start",
    "scroll-padding-left",
    "scroll-padding-right",
    "scroll-padding-top",
    "scroll-snap-align",
    "scroll-snap-stop",
    "scroll-snap-type",
    "scroll-target-group",
    "scroll-timeline-axis",
    "scroll-timeline-name",
    "scrollbar-color",
    "scrollbar-gutter",
    "scrollbar-width",
    # SVG presentation and geometry.
    "clip-rule",
    "color-interpolation",
    "color-interpolation-filters",
    "color-rendering",
    "cx",
    "cy",
    "d",
    "fill",
    "fill-opacity",
    "fill-rule",
    "flood-color",
    "flood-opacity",
    "lighting-color",
    "marker-end",
    "marker-mid",
    "marker-start",
    "paint-order",
    "r",
    "rx",
    "ry",
    "shape-rendering",
    "stop-color",
    "stop-opacity",
    "stroke",
    "stroke-dasharray",
    "stroke-dashoffset",
    "stroke-linecap",
    "stroke-linejoin",
    "stroke-miterlimit",
    "stroke-opacity",
    "stroke-width",
    "vector-effect",
    "x",
    "y",
)

BLINK_LAYOUT_CHECKPOINT_INCLUDES = (
    BLINK_BRIDGE_INCLUDE,
    "#include <string>",
    "#include <vector>",
    '#include "base/no_destructor.h"',
    '#include "third_party/blink/renderer/core/css/css_value.h"',
    '#include "third_party/blink/renderer/core/css/properties/css_property.h"',
    '#include "third_party/blink/renderer/core/css/style_engine.h"',
    '#include "third_party/blink/renderer/core/display_lock/'
    'display_lock_utilities.h"',
    BLINK_DOCUMENT_INCLUDE,
    '#include "third_party/blink/renderer/core/dom/element.h"',
    '#include "third_party/blink/renderer/core/dom/column_pseudo_element.h"',
    '#include "third_party/blink/renderer/core/dom/node_traversal.h"',
    '#include "third_party/blink/renderer/core/dom/pseudo_element.h"',
    '#include "third_party/blink/renderer/core/dom/shadow_root.h"',
    '#include "third_party/blink/renderer/core/layout/layout_object.h"',
    '#include "third_party/blink/renderer/core/layout/layout_text.h"',
    '#include "third_party/blink/renderer/core/view_transition/'
    'view_transition_utils.h"',
    '#include "third_party/blink/renderer/platform/wtf/text/string_builder.h"',
    '#include "ui/gfx/geometry/quad_f.h"',
    '#include "ui/gfx/geometry/rect_f.h"',
    '#include "base/time/time.h"',
    '#include "third_party/blink/renderer/core/frame/web_frame_widget_impl.h"',
    '#include "third_party/blink/renderer/core/frame/web_local_frame_impl.h"',
)
BLINK_LAYOUT_CHECKPOINT_HELPER_MARKER = "RecorderRecordLayoutCheckpoint("
BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR = """\
bool LocalFrameView::UpdateLifecyclePhases(
    DocumentLifecycle::LifecycleState target_state,
"""
BLINK_LAYOUT_CHECKPOINT_HELPER = """\
namespace {

// The computed-style properties recorded for each element, in recorded order.
@@RECORDER_LAYOUT_STYLE_PROPERTY_ARRAY@@
const std::vector<std::string>& RecorderLayoutStylePropertyNames() {
  static const base::NoDestructor<std::vector<std::string>> names([] {
    std::vector<std::string> result;
    for (CSSPropertyID id : kRecorderLayoutStyleProperties) {
      result.push_back(CSSProperty::Get(id).GetPropertyNameString().Utf8());
    }
    return result;
  }());
  return *names;
}

const char* RecorderShadowRootModeName(ShadowRootMode recorder_mode) {
  switch (recorder_mode) {
    case ShadowRootMode::kOpen:
      return "open";
    case ShadowRootMode::kClosed:
      return "closed";
    case ShadowRootMode::kUserAgent:
      return "user-agent";
  }
  return "user-agent";
}

// Appends every pseudo-element Blink currently holds for the element, and
// recursively the pseudo-elements those hold, in the order Blink attaches
// them. Nothing is created: an absent pseudo-element is not recorded.
void RecorderAppendPseudoElements(
    const Element& recorder_element,
    HeapVector<Member<PseudoElement>>& recorder_result) {
  constexpr PseudoId kRecorderPseudoIds[] = {
      kPseudoIdCheckMark,
      kPseudoIdBefore,
      kPseudoIdAfter,
      kPseudoIdExpandIcon,
      kPseudoIdPickerIcon,
      kPseudoIdInterestButton,
      kPseudoIdMarker,
      kPseudoIdFirstLetter,
      kPseudoIdBackdrop,
      kPseudoIdOverscrollBackdrop,
      kPseudoIdOverscrollAreaParent,
      kPseudoIdSkeleton,
      kPseudoIdScrollMarkerGroupBefore,
      kPseudoIdScrollMarkerGroupAfter,
      kPseudoIdScrollMarker,
      kPseudoIdScrollButtonBlockStart,
      kPseudoIdScrollButtonInlineStart,
      kPseudoIdScrollButtonInlineEnd,
      kPseudoIdScrollButtonBlockEnd,
  };
  for (PseudoId recorder_pseudo_id : kRecorderPseudoIds) {
    if (PseudoElement* recorder_pseudo =
            recorder_element.GetPseudoElement(recorder_pseudo_id)) {
      recorder_result.push_back(recorder_pseudo);
      RecorderAppendPseudoElements(*recorder_pseudo, recorder_result);
    }
  }
  ViewTransitionUtils::ForEachTransitionPseudo(
      recorder_element,
      [&recorder_result](PseudoElement* recorder_pseudo) {
        recorder_result.push_back(recorder_pseudo);
        RecorderAppendPseudoElements(*recorder_pseudo, recorder_result);
      },
      ViewTransitionUtils::Filter::kDirectChildren);
  if (const ColumnPseudoElementsVector* recorder_columns =
          recorder_element.GetColumnPseudoElements()) {
    for (const Member<ColumnPseudoElement>& recorder_column :
         *recorder_columns) {
      recorder_result.push_back(recorder_column.Get());
      RecorderAppendPseudoElements(*recorder_column, recorder_result);
    }
  }
}

// Reads the text laid out inside a pseudo-element's layout subtree, as
// rendered after text-transform, up to a fixed length.
void RecorderReadGeneratedText(
    const LayoutObject* recorder_layout_object,
    a11y_recorder::LayoutCheckpointNode& recorder_record) {
  constexpr unsigned kRecorderMaximumGeneratedTextLength = 4096;
  StringBuilder recorder_text;
  for (const LayoutObject* recorder_descendant = recorder_layout_object;
       recorder_descendant;
       recorder_descendant =
           recorder_descendant->NextInPreOrder(recorder_layout_object)) {
    if (const auto* recorder_layout_text =
            DynamicTo<LayoutText>(recorder_descendant)) {
      recorder_text.Append(recorder_layout_text->TransformedText());
    }
  }
  const String recorder_full_text = recorder_text.ToString();
  recorder_record.generated_text_length =
      static_cast<int>(recorder_full_text.length());
  recorder_record.generated_text_truncated =
      recorder_full_text.length() > kRecorderMaximumGeneratedTextLength;
  recorder_record.generated_text =
      recorder_record.generated_text_truncated
          ? recorder_full_text.substr(0, kRecorderMaximumGeneratedTextLength)
                .Utf8()
          : recorder_full_text.Utf8();
}

// Asks the local-root widget of a frame to report what happens to the
// compositor frame that carries a layout checkpoint's rendering update. A
// frame with no widget is recorded as such, so every completed layout
// checkpoint has exactly one presentation request.
void RecorderRequestLayoutPresentation(
    LocalFrame& recorder_frame,
    uint64_t recorder_checkpoint_sequence,
    int recorder_document_node_id,
    const std::string& recorder_document_token) {
  WebLocalFrameImpl* recorder_local_root =
      WebLocalFrameImpl::FromFrame(recorder_frame.LocalFrameRoot());
  WebFrameWidgetImpl* recorder_widget =
      recorder_local_root ? recorder_local_root->FrameWidgetImpl() : nullptr;
  if (!recorder_widget) {
    a11y_recorder::BeginBlinkPresentationRequest(
        recorder_document_node_id, recorder_document_token,
        recorder_checkpoint_sequence,
        a11y_recorder::PresentationWidgetIdentity{}, "no-widget", -1, false,
        base::TimeTicks::IsHighResolution());
    return;
  }
  recorder_widget->RecorderRequestPresentationEvidence(
      recorder_checkpoint_sequence, recorder_document_node_id,
      recorder_document_token);
}

// Records the layout geometry and computed styles of one frame view's document
// after a rendering update reached the paint-clean state. Everything is read
// from the style and layout Blink already produced; nothing here requests a
// style recalculation or a layout.
void RecorderRecordLayoutCheckpoint(LocalFrameView& frame_view) {
  constexpr int kRecorderMaximumLayoutCheckpointNodes = 100000;
  LocalFrame& recorder_frame = frame_view.GetFrame();
  Document* recorder_document = recorder_frame.GetDocument();
  if (!recorder_document || !recorder_document->IsActive() ||
      !frame_view.GetLayoutView() ||
      recorder_document->Lifecycle().GetState() !=
          DocumentLifecycle::kPaintClean) {
    return;
  }
  const float recorder_zoom = recorder_frame.LayoutZoomFactor();
  if (recorder_zoom <= 0) {
    return;
  }
  a11y_recorder::LayoutCheckpointFrame recorder_geometry;
  const gfx::SizeF recorder_viewport =
      frame_view.ViewportSizeForMediaQueries();
  recorder_geometry.viewport_width = recorder_viewport.width();
  recorder_geometry.viewport_height = recorder_viewport.height();
  if (PaintLayerScrollableArea* recorder_scroller =
          frame_view.LayoutViewport()) {
    const ScrollOffset recorder_offset = recorder_scroller->GetScrollOffset();
    recorder_geometry.scroll_x = recorder_offset.x() / recorder_zoom;
    recorder_geometry.scroll_y = recorder_offset.y() / recorder_zoom;
  }
  recorder_geometry.device_pixel_ratio = recorder_frame.DevicePixelRatio();
  recorder_geometry.layout_zoom_factor = recorder_zoom;
  const int recorder_document_node_id = recorder_document->GetDomNodeId();
  const std::string recorder_document_token =
      recorder_document->Token().ToString();
  const uint64_t recorder_checkpoint_sequence =
      a11y_recorder::BeginBlinkLayoutCheckpoint(
          recorder_document_node_id, recorder_document_token,
          recorder_document->GetStyleEngine().StyleForElementCount(),
          frame_view.LayoutCountForTesting(), recorder_geometry,
          RecorderLayoutStylePropertyNames(),
          kRecorderMaximumLayoutCheckpointNodes);
  if (recorder_checkpoint_sequence == 0) {
    return;
  }
  const std::vector<std::string>& recorder_property_names =
      RecorderLayoutStylePropertyNames();
  int recorder_node_count = 0;
  bool recorder_truncated = false;
  int recorder_pseudo_element_count = 0;
  int recorder_shadow_root_count = 0;
  // Records one element, laid-out text node, or pseudo-element. Returns false
  // once the node limit is reached.
  auto recorder_record_node = [&](Node& recorder_node,
                                  PseudoElement* recorder_pseudo) {
    Element* recorder_element = DynamicTo<Element>(recorder_node);
    LayoutObject* recorder_layout_object = recorder_node.GetLayoutObject();
    if (recorder_node.IsTextNode() && !recorder_layout_object) {
      return true;
    }
    if (recorder_node_count >= kRecorderMaximumLayoutCheckpointNodes) {
      recorder_truncated = true;
      return false;
    }
    a11y_recorder::LayoutCheckpointNode recorder_record;
    recorder_record.node_index = recorder_node_count;
    recorder_record.node_id = recorder_node.GetDomNodeId();
    recorder_record.node_type = static_cast<int>(recorder_node.getNodeType());
    recorder_record.node_name = recorder_node.nodeName().Utf8();
    recorder_record.layout_object_present = recorder_layout_object != nullptr;
    recorder_record.display_locked =
        DisplayLockUtilities::LockedAncestorPreventingLayout(recorder_node) !=
        nullptr;
    if (recorder_layout_object) {
      gfx::RectF recorder_rect;
      if (recorder_element) {
        recorder_rect =
            recorder_element->GetBoundingClientRectNoLifecycleUpdate();
      } else {
        Vector<gfx::QuadF> recorder_quads;
        recorder_layout_object->AbsoluteQuads(recorder_quads);
        for (const gfx::QuadF& recorder_quad : recorder_quads) {
          recorder_rect.Union(recorder_quad.BoundingBox());
        }
        if (recorder_rect != gfx::RectF()) {
          recorder_document->AdjustRectForScrollAndAbsoluteZoom(
              recorder_rect, *recorder_layout_object);
        }
      }
      recorder_record.x = recorder_rect.x();
      recorder_record.y = recorder_rect.y();
      recorder_record.width = recorder_rect.width();
      recorder_record.height = recorder_rect.height();
    }
    const ComputedStyle* recorder_style =
        recorder_element ? recorder_element->GetComputedStyle() : nullptr;
    if (recorder_style && !recorder_style->IsEnsuredInDisplayNone()) {
      recorder_record.computed_style_present = true;
      recorder_record.computed_style.reserve(
          std::size(kRecorderLayoutStyleProperties));
      size_t recorder_index = 0;
      for (CSSPropertyID recorder_property_id :
           kRecorderLayoutStyleProperties) {
        const CSSValue* recorder_value =
            CSSProperty::Get(recorder_property_id)
                .CSSValueFromComputedStyle(*recorder_style,
                                           recorder_layout_object,
                                           /*allow_visited_style=*/false,
                                           CSSValuePhase::kResolvedValue);
        a11y_recorder::LayoutCheckpointStyleValue recorder_entry;
        recorder_entry.property_name = recorder_property_names[recorder_index];
        recorder_entry.value_present = recorder_value != nullptr;
        if (recorder_value) {
          recorder_entry.value = recorder_value->CssText().Utf8();
        }
        recorder_record.computed_style.push_back(std::move(recorder_entry));
        ++recorder_index;
      }
    }
    if (recorder_pseudo) {
      recorder_record.pseudo_element_present = true;
      Element* recorder_originating =
          recorder_pseudo->ParentOrShadowHostElement();
      recorder_record.originating_node_id =
          recorder_originating ? recorder_originating->GetDomNodeId() : 0;
      recorder_record.pseudo_type =
          PseudoElement::PseudoElementNameForEvents(recorder_pseudo).Utf8();
      RecorderReadGeneratedText(recorder_layout_object, recorder_record);
    }
    if (ShadowRoot* recorder_containing_root =
            recorder_node.ContainingShadowRoot()) {
      recorder_record.shadow_host_node_id =
          recorder_containing_root->host().GetDomNodeId();
      recorder_record.shadow_root_mode =
          RecorderShadowRootModeName(recorder_containing_root->GetMode());
    }
    a11y_recorder::RecordBlinkLayoutCheckpointNode(
        recorder_checkpoint_sequence, recorder_document_node_id,
        recorder_document_token, std::move(recorder_record));
    ++recorder_node_count;
    return true;
  };
  // Composed-tree preorder: each node, then its pseudo-elements, then the
  // shadow tree it hosts, then its own children.
  HeapVector<Member<Node>> recorder_pending;
  recorder_pending.push_back(recorder_document);
  while (!recorder_pending.empty()) {
    Node& recorder_node = *recorder_pending.back();
    recorder_pending.pop_back();
    for (Node* recorder_child = recorder_node.lastChild(); recorder_child;
         recorder_child = recorder_child->previousSibling()) {
      recorder_pending.push_back(recorder_child);
    }
    if (IsA<ShadowRoot>(recorder_node)) {
      ++recorder_shadow_root_count;
      continue;
    }
    Element* recorder_element = DynamicTo<Element>(recorder_node);
    if (recorder_element) {
      if (ShadowRoot* recorder_hosted_root =
              recorder_element->GetShadowRoot()) {
        recorder_pending.push_back(recorder_hosted_root);
      }
    } else if (!recorder_node.IsTextNode()) {
      continue;
    }
    if (!recorder_record_node(recorder_node, nullptr)) {
      break;
    }
    if (!recorder_element) {
      continue;
    }
    HeapVector<Member<PseudoElement>> recorder_pseudo_elements;
    RecorderAppendPseudoElements(*recorder_element, recorder_pseudo_elements);
    bool recorder_limit_reached = false;
    for (const Member<PseudoElement>& recorder_pseudo :
         recorder_pseudo_elements) {
      if (!recorder_record_node(*recorder_pseudo, recorder_pseudo.Get())) {
        recorder_limit_reached = true;
        break;
      }
      ++recorder_pseudo_element_count;
    }
    if (recorder_limit_reached) {
      break;
    }
  }
  a11y_recorder::CompleteBlinkLayoutCheckpoint(
      recorder_checkpoint_sequence, recorder_document_node_id,
      recorder_document_token, recorder_node_count, recorder_truncated,
      kRecorderMaximumLayoutCheckpointNodes, recorder_pseudo_element_count,
      recorder_shadow_root_count);
  RecorderRequestLayoutPresentation(recorder_frame,
                                    recorder_checkpoint_sequence,
                                    recorder_document_node_id,
                                    recorder_document_token);
  RecorderRecordInteractionCheckpoint(*recorder_document,
                                      recorder_checkpoint_sequence,
                                      "browser.layout", "rendering-update");
}

}  // namespace

"""

def blink_css_property_enum(name: str) -> str:
    """Returns the CSSPropertyID enumerator Blink generates for a property."""
    return "k" + "".join(word.capitalize() for word in name.split("-"))


# The property array is generated from LAYOUT_STYLE_PROPERTIES so the compiled
# list and the documented list cannot drift apart.
BLINK_LAYOUT_STYLE_PROPERTY_ARRAY = (
    "constexpr CSSPropertyID kRecorderLayoutStyleProperties[] = {\n"
    + "".join(
        f"    CSSPropertyID::{blink_css_property_enum(name)},\n"
        for name in LAYOUT_STYLE_PROPERTIES
    )
    + "};\n"
)
BLINK_LAYOUT_CHECKPOINT_HELPER = (
    BLINK_LAYOUT_INTERACTION_CHECKPOINT_DECLARATION
    + BLINK_LAYOUT_CHECKPOINT_HELPER.replace(
        "@@RECORDER_LAYOUT_STYLE_PROPERTY_ARRAY@@\n",
        BLINK_LAYOUT_STYLE_PROPERTY_ARRAY,
    )
)
BLINK_LAYOUT_STYLE_PROPERTY_ARRAY_PATTERN = re.compile(
    r"constexpr CSSPropertyID kRecorderLayoutStyleProperties\[\] = \{\n"
    r"(?:    CSSPropertyID::k[A-Za-z]+,\n)+"
    r"\};\n"
)
BLINK_LAYOUT_CHECKPOINT_ANCHOR = """\
    ForAllNonThrottledLocalFrameViews([](LocalFrameView& frame_view) {
      auto lifecycle_observers = frame_view.lifecycle_observers_;
      for (auto& observer : lifecycle_observers)
        observer->DidFinishLifecycleUpdate(frame_view);
    });
"""
BLINK_LAYOUT_CHECKPOINT_HOOK = """\
    ForAllNonThrottledLocalFrameViews([](LocalFrameView& frame_view) {
      auto lifecycle_observers = frame_view.lifecycle_observers_;
      for (auto& observer : lifecycle_observers)
        observer->DidFinishLifecycleUpdate(frame_view);
    });
    ForAllNonThrottledLocalFrameViews([](LocalFrameView& frame_view) {
      RecorderRecordLayoutCheckpoint(frame_view);
    });
"""


BLINK_LAYOUT_CHECKPOINT_LEGACY_STYLE_LOOPS = (
    (
        """\
      for (size_t recorder_index = 0;
           recorder_index < std::size(kRecorderLayoutStyleProperties);
           ++recorder_index) {
        const CSSValue* recorder_value =
            CSSProperty::Get(kRecorderLayoutStyleProperties[recorder_index])
                .CSSValueFromComputedStyle(""",
        """\
      size_t recorder_index = 0;
      for (CSSPropertyID recorder_property_id :
           kRecorderLayoutStyleProperties) {
        const CSSValue* recorder_value =
            CSSProperty::Get(recorder_property_id)
                .CSSValueFromComputedStyle(""",
    ),
    (
        """\
        recorder_record.computed_style.push_back(std::move(recorder_entry));
      }
    }
    a11y_recorder::RecordBlinkLayoutCheckpointNode(""",
        """\
        recorder_record.computed_style.push_back(std::move(recorder_entry));
        ++recorder_index;
      }
    }
    a11y_recorder::RecordBlinkLayoutCheckpointNode(""",
    ),
)


def replace_layout_checkpoint_helper(text: str, path: Path) -> str:
    """Replaces a layout helper written by an earlier revision.

    The helper is inserted once, directly before its anchor, and opens with
    the only anonymous namespace in that region. A tree patched before the
    composed-tree traversal holds a helper that records the light tree only,
    so the whole region is rewritten rather than matched against any
    particular earlier text.
    """
    if BLINK_LAYOUT_CHECKPOINT_HELPER in text:
        return text
    if BLINK_LAYOUT_CHECKPOINT_HELPER_MARKER not in text:
        return text
    end = text.find(BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR)
    if end < 0 or text.find(BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR, end + 1) >= 0:
        raise RuntimeError(
            f"{path}: expected exactly one layout checkpoint helper anchor"
        )
    start = text.rfind("namespace {\n", 0, end)
    # A helper written for protocol 0.29 or later is preceded by the
    # declaration of the interaction checkpoint, which belongs to the region.
    if start >= 0 and text[:start].endswith(
        BLINK_LAYOUT_INTERACTION_CHECKPOINT_DECLARATION
    ):
        start -= len(BLINK_LAYOUT_INTERACTION_CHECKPOINT_DECLARATION)
    if (
        start < 0
        or BLINK_LAYOUT_CHECKPOINT_HELPER_MARKER not in text[start:end]
        or not text[start:end].endswith("}  // namespace\n\n")
    ):
        raise RuntimeError(
            f"{path}: the layout checkpoint helper region is not recognised"
        )
    return text[:start] + BLINK_LAYOUT_CHECKPOINT_HELPER + text[end:]


def patch_blink_local_frame_view(path: Path) -> None:
    """Adds the layout checkpoint helper and its paint-clean hook."""
    text = read_source(path)
    text = add_includes_after(
        text,
        '#include "third_party/blink/renderer/core/frame/local_frame_view.h"',
        BLINK_LAYOUT_CHECKPOINT_INCLUDES,
        path,
    )
    # A tree patched by revision e459624 indexes the property array, which
    # Blink rejects as unsafe buffer access. Upgrade that helper in place.
    if "for (size_t recorder_index = 0;" in text:
        text = upgrade_legacy_hooks(
            text, BLINK_LAYOUT_CHECKPOINT_LEGACY_STYLE_LOOPS, path
        )
    # A tree patched by an earlier revision holds an older property list.
    # Replace that list in place, since the helper is inserted only once.
    if BLINK_LAYOUT_CHECKPOINT_HELPER_MARKER in text:
        arrays = BLINK_LAYOUT_STYLE_PROPERTY_ARRAY_PATTERN.findall(text)
        if len(arrays) != 1:
            raise RuntimeError(
                f"{path}: expected one layout style property array, "
                f"found {len(arrays)}"
            )
        text = text.replace(arrays[0], BLINK_LAYOUT_STYLE_PROPERTY_ARRAY)
    text = replace_layout_checkpoint_helper(text, path)
    text = insert_before_once(
        text,
        BLINK_LAYOUT_CHECKPOINT_HELPER_ANCHOR,
        BLINK_LAYOUT_CHECKPOINT_HELPER,
        BLINK_LAYOUT_CHECKPOINT_HELPER_MARKER,
        path,
    )
    text = apply_cookie_hook(
        text, BLINK_LAYOUT_CHECKPOINT_ANCHOR, BLINK_LAYOUT_CHECKPOINT_HOOK, path
    )
    write_patched(path, text)



# Rendered-frame correlation. Each layout checkpoint asks its local-root
# widget to queue a swap promise on the widget's layer tree. The promise
# records the compositor frame token when the frame carrying the update is
# submitted, or each reason it was not, and then registers for viz's
# presentation feedback for that token. Nothing here changes what Chromium
# draws or when: the promise only observes the commit it rides.
BLINK_PRESENTATION_WIDGET_HEADER_DECLARATION_ANCHOR = """\
  void NotifyPresentationTime(
      base::OnceCallback<void(const viz::FrameTimingDetails&)>
          presentation_callback) override;
"""
BLINK_PRESENTATION_WIDGET_HEADER_DECLARATION = """\
  // Recorder evidence: records a presentation request for the compositor
  // frame that carries the named layout checkpoint's rendering update, and
  // queues a swap promise that follows that frame when this widget composites.
  void RecorderRequestPresentationEvidence(uint64_t layout_checkpoint_sequence,
                                           int document_node_id,
                                           std::string document_token);
"""
BLINK_PRESENTATION_WIDGET_HEADER_FRIEND_ANCHOR = (
    "  friend class ReportTimeSwapPromise;\n"
)
BLINK_PRESENTATION_WIDGET_HEADER_FRIEND = (
    "  friend class RecorderPresentationSwapPromise;\n"
)
BLINK_PRESENTATION_WIDGET_INCLUDES = (
    BLINK_BRIDGE_INCLUDE,
    "#include <atomic>",
    "#include <string>",
    '#include "base/memory/ref_counted.h"',
    '#include "cc/trees/swap_promise.h"',
    '#include "components/viz/common/frame_timing_details.h"',
    '#include "components/viz/common/quads/compositor_frame_metadata.h"',
)
BLINK_PRESENTATION_WIDGET_MARKER = "class RecorderPresentationSwapPromise"
BLINK_PRESENTATION_WIDGET_ANCHOR = """\
void WebFrameWidgetImpl::WaitForDebuggerWhenShown() {
"""
BLINK_PRESENTATION_WIDGET_BLOCK = """\
// Recorder evidence: the facts every record of one presentation request
// repeats. The swap promise and the presentation callback share them across
// the main and compositor threads, so they never change once made.
class RecorderPresentationRequestFacts
    : public base::RefCountedThreadSafe<RecorderPresentationRequestFacts> {
 public:
  RecorderPresentationRequestFacts(
      uint64_t request_sequence,
      int document_node_id,
      std::string document_token,
      a11y_recorder::PresentationWidgetIdentity widget,
      bool high_resolution_ticks)
      : request_sequence(request_sequence),
        document_node_id(document_node_id),
        document_token(std::move(document_token)),
        widget(std::move(widget)),
        high_resolution_ticks(high_resolution_ticks) {}

  const uint64_t request_sequence;
  const int document_node_id;
  const std::string document_token;
  const a11y_recorder::PresentationWidgetIdentity widget;
  const bool high_resolution_ticks;

 private:
  friend class base::RefCountedThreadSafe<RecorderPresentationRequestFacts>;
  ~RecorderPresentationRequestFacts() = default;
};

static int64_t RecorderTimeTicksMicroseconds(base::TimeTicks recorder_time) {
  return recorder_time.is_null()
             ? 0
             : (recorder_time - base::TimeTicks()).InMicroseconds();
}

static const char* RecorderDidNotSwapReasonName(
    cc::SwapPromise::DidNotSwapReason recorder_reason) {
  switch (recorder_reason) {
    case cc::SwapPromise::SWAP_FAILS:
      return "swap-fails";
    case cc::SwapPromise::COMMIT_FAILS:
      return "commit-fails";
    case cc::SwapPromise::COMMIT_NO_UPDATE:
      return "commit-no-update";
    case cc::SwapPromise::ACTIVATION_FAILS:
      return "activation-fails";
  }
  return "unknown";
}

// Follows the compositor frame that carries one layout checkpoint's rendering
// update. The promise breaks on the reasons ReportTimeSwapPromise treats as
// failures, a swap that fails or a commit with no update, and otherwise stays
// active for a later frame. DidNotSwap may run on either thread, so the count
// of calls is atomic.
class RecorderPresentationSwapPromise : public cc::SwapPromise {
 public:
  RecorderPresentationSwapPromise(
      scoped_refptr<RecorderPresentationRequestFacts> facts,
      scoped_refptr<base::SingleThreadTaskRunner> task_runner,
      WebFrameWidgetImpl* widget)
      : facts_(std::move(facts)),
        task_runner_(std::move(task_runner)),
        widget_(MakeCrossThreadWeakHandle(widget)) {}

  RecorderPresentationSwapPromise(const RecorderPresentationSwapPromise&) =
      delete;
  RecorderPresentationSwapPromise& operator=(
      const RecorderPresentationSwapPromise&) = delete;

  ~RecorderPresentationSwapPromise() override = default;

  void DidActivate() override {}

  void WillSwap(viz::CompositorFrameMetadata* metadata) override {
    frame_token_ = metadata->frame_token;
  }

  void DidSwap() override {
    const int recorder_not_swapped_count = not_swapped_count_.load();
    a11y_recorder::RecordBlinkPresentationSwapped(
        facts_->request_sequence, facts_->document_node_id,
        facts_->document_token, facts_->widget, frame_token_,
        recorder_not_swapped_count);
    PostCrossThreadTask(
        *task_runner_, FROM_HERE,
        CrossThreadBindOnce(&AddFeedbackCallback,
                            MakeUnwrappingCrossThreadWeakHandle(widget_),
                            facts_, frame_token_, recorder_not_swapped_count));
  }

  DidNotSwapAction DidNotSwap(DidNotSwapReason reason,
                              base::TimeTicks timestamp) override {
    const bool recorder_kept_active =
        reason != DidNotSwapReason::SWAP_FAILS &&
        reason != DidNotSwapReason::COMMIT_NO_UPDATE;
    const int recorder_index = not_swapped_count_.fetch_add(1);
    a11y_recorder::RecordBlinkPresentationNotSwapped(
        facts_->request_sequence, facts_->document_node_id,
        facts_->document_token, facts_->widget,
        RecorderDidNotSwapReasonName(reason), recorder_kept_active,
        recorder_index, RecorderTimeTicksMicroseconds(timestamp),
        facts_->high_resolution_ticks);
    return recorder_kept_active ? DidNotSwapAction::KEEP_ACTIVE
                                : DidNotSwapAction::BREAK_PROMISE;
  }

  int64_t GetTraceId() const override { return 0; }

 private:
  // Runs on the main thread. A widget that was collected or closed before the
  // task ran registers nothing, so the request ends without feedback.
  static void AddFeedbackCallback(
      WebFrameWidgetImpl* widget,
      scoped_refptr<RecorderPresentationRequestFacts> facts,
      uint32_t frame_token,
      int not_swapped_count) {
    if (!widget || !widget->widget_base_) {
      return;
    }
    widget->widget_base_->AddPresentationCallback(
        frame_token, blink::BindOnce(&RecordFeedback, std::move(facts),
                                     frame_token, not_swapped_count));
  }

  static void RecordFeedback(
      scoped_refptr<RecorderPresentationRequestFacts> facts,
      uint32_t frame_token,
      int not_swapped_count,
      const viz::FrameTimingDetails& details) {
    a11y_recorder::PresentationFeedbackTiming recorder_timing;
    recorder_timing.presented_microseconds = RecorderTimeTicksMicroseconds(
        details.presentation_feedback.timestamp);
    recorder_timing.interval_microseconds =
        details.presentation_feedback.interval.InMicroseconds();
    recorder_timing.flags = details.presentation_feedback.flags;
    recorder_timing.received_compositor_frame_microseconds =
        RecorderTimeTicksMicroseconds(
            details.received_compositor_frame_timestamp);
    recorder_timing.draw_start_microseconds =
        RecorderTimeTicksMicroseconds(details.draw_start_timestamp);
    recorder_timing.swap_start_microseconds =
        RecorderTimeTicksMicroseconds(details.swap_timings.swap_start);
    recorder_timing.swap_end_microseconds =
        RecorderTimeTicksMicroseconds(details.swap_timings.swap_end);
    a11y_recorder::RecordBlinkPresentationFeedback(
        facts->request_sequence, facts->document_node_id,
        facts->document_token, facts->widget, frame_token, recorder_timing,
        not_swapped_count, facts->high_resolution_ticks);
  }

  const scoped_refptr<RecorderPresentationRequestFacts> facts_;
  const scoped_refptr<base::SingleThreadTaskRunner> task_runner_;
  CrossThreadWeakHandle<WebFrameWidgetImpl> widget_;
  uint32_t frame_token_ = 0;
  std::atomic<int> not_swapped_count_{0};
};

void WebFrameWidgetImpl::RecorderRequestPresentationEvidence(
    uint64_t layout_checkpoint_sequence,
    int document_node_id,
    std::string document_token) {
  const bool recorder_high_resolution = base::TimeTicks::IsHighResolution();
  WebLocalFrameImpl* recorder_local_root = LocalRootImpl();
  if (!recorder_local_root || !recorder_local_root->GetFrame()) {
    a11y_recorder::BeginBlinkPresentationRequest(
        document_node_id, std::move(document_token),
        layout_checkpoint_sequence,
        a11y_recorder::PresentationWidgetIdentity{}, "no-widget", -1, false,
        recorder_high_resolution);
    return;
  }
  a11y_recorder::PresentationWidgetIdentity recorder_widget;
  recorder_widget.present = true;
  recorder_widget.frame_sink_client_id = frame_sink_id_.client_id();
  recorder_widget.frame_sink_id = frame_sink_id_.sink_id();
  recorder_widget.local_root_frame_token =
      recorder_local_root->GetFrame()->GetLocalFrameToken().ToString();
  cc::LayerTreeHost* recorder_host =
      widget_base_ ? widget_base_->LayerTreeHost() : nullptr;
  const bool recorder_composites = View()->does_composite() && recorder_host;
  const uint64_t recorder_request_sequence =
      a11y_recorder::BeginBlinkPresentationRequest(
          document_node_id, document_token, layout_checkpoint_sequence,
          recorder_widget, recorder_composites ? "" : "not-compositing",
          recorder_composites ? recorder_host->SourceFrameNumber() : -1,
          ForMainFrame(), recorder_high_resolution);
  if (recorder_request_sequence == 0) {
    return;
  }
  recorder_host->QueueSwapPromise(
      std::make_unique<RecorderPresentationSwapPromise>(
          base::MakeRefCounted<RecorderPresentationRequestFacts>(
              recorder_request_sequence, document_node_id,
              std::move(document_token), std::move(recorder_widget),
              recorder_high_resolution),
          recorder_host->GetTaskRunnerProvider()->MainThreadTaskRunner(),
          this));
}

"""


def patch_blink_web_frame_widget_header(path: Path) -> None:
    """Declares the presentation request and befriends its swap promise."""
    text = read_source(path)
    text = add_includes_after(
        text, '#include "base/time/time.h"', ("#include <string>",), path
    )
    if BLINK_PRESENTATION_WIDGET_HEADER_DECLARATION not in text:
        text = replace_once(
            text,
            BLINK_PRESENTATION_WIDGET_HEADER_DECLARATION_ANCHOR,
            BLINK_PRESENTATION_WIDGET_HEADER_DECLARATION_ANCHOR
            + BLINK_PRESENTATION_WIDGET_HEADER_DECLARATION,
            path,
        )
    if BLINK_PRESENTATION_WIDGET_HEADER_FRIEND not in text:
        text = replace_once(
            text,
            BLINK_PRESENTATION_WIDGET_HEADER_FRIEND_ANCHOR,
            BLINK_PRESENTATION_WIDGET_HEADER_FRIEND_ANCHOR
            + BLINK_PRESENTATION_WIDGET_HEADER_FRIEND,
            path,
        )
    write_patched(path, text)


def patch_blink_web_frame_widget(path: Path) -> None:
    """Adds the presentation swap promise and the request that queues it."""
    text = read_source(path)
    text = add_includes_after(
        text,
        '#include "third_party/blink/renderer/core/frame/'
        'web_frame_widget_impl.h"',
        BLINK_PRESENTATION_WIDGET_INCLUDES,
        path,
    )
    text = insert_before_once(
        text,
        BLINK_PRESENTATION_WIDGET_ANCHOR,
        BLINK_PRESENTATION_WIDGET_BLOCK,
        BLINK_PRESENTATION_WIDGET_MARKER,
        path,
    )
    write_patched(path, text)

# Network metadata. The Blink hooks record request and response metadata at
# the resource load observers, which see every load a frame or worker makes
# once it has an inspector identifier. The browser hooks record the headers the
# network service reports sending and receiving, and each finished
# navigation's request and response. No hook reads a body. Header values pass
# through the bridge's classifier, which withholds credential values.
def recorder_enum_value_name(enumerator: str) -> str:
    """Returns the recorder's kebab-case name for a Chromium enumerator."""
    body = enumerator[1:] if enumerator.startswith("k") else enumerator
    words = re.findall(r"[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+|[0-9]+", body)
    return "-".join(word.lower() for word in words)


def recorder_enum_name_function(
    function: str, value_type: str, enumerators: tuple[str, ...]
) -> str:
    """Writes a function naming each enumerator, with "unknown" otherwise.

    The chain is written as comparisons rather than a switch, so an enumerator
    Chromium adds later reads as unknown rather than failing the build.
    """
    lines = [f"const char* {function}({value_type} value) {{"]
    for enumerator in enumerators:
        lines.append(f"  if (value == {value_type}::{enumerator}) {{")
        lines.append(f'    return "{recorder_enum_value_name(enumerator)}";')
        lines.append("  }")
    lines.append('  return "unknown";')
    lines.append("}")
    return "\n".join(lines) + "\n"


BLINK_NETWORK_ENUMS = (
    (
        "RecorderNetworkPriorityName",
        "ResourceLoadPriority",
        ("kUnresolved", "kVeryLow", "kLow", "kMedium", "kHigh", "kVeryHigh"),
    ),
    (
        "RecorderNetworkRequestModeName",
        "network::mojom::RequestMode",
        (
            "kSameOrigin",
            "kNoCors",
            "kCors",
            "kCorsWithForcedPreflight",
            "kNavigate",
        ),
    ),
    (
        "RecorderNetworkRedirectModeName",
        "network::mojom::RedirectMode",
        ("kFollow", "kError", "kManual"),
    ),
    (
        "RecorderNetworkCredentialsModeName",
        "network::mojom::CredentialsMode",
        ("kOmit", "kSameOrigin", "kInclude", "kOmitBug_775438_Workaround"),
    ),
    (
        "RecorderNetworkCacheModeName",
        "mojom::blink::FetchCacheMode",
        (
            "kDefault",
            "kNoStore",
            "kBypassCache",
            "kValidateCache",
            "kForceCache",
            "kOnlyIfCached",
            "kUnspecifiedOnlyIfCachedStrict",
            "kUnspecifiedForceCacheMiss",
        ),
    ),
    (
        "RecorderNetworkFetchPriorityHintName",
        "mojom::blink::FetchPriorityHint",
        ("kLow", "kAuto", "kHigh"),
    ),
    (
        "RecorderNetworkRenderBlockingName",
        "RenderBlockingBehavior",
        (
            "kUnset",
            "kBlocking",
            "kNonBlocking",
            "kNonBlockingDynamic",
            "kPotentiallyBlocking",
            "kInBodyParserBlocking",
        ),
    ),
    (
        "RecorderNetworkResponseTypeName",
        "network::mojom::FetchResponseType",
        ("kBasic", "kCors", "kDefault", "kError", "kOpaque", "kOpaqueRedirect"),
    ),
    (
        "RecorderNetworkResponseSourceName",
        "network::mojom::FetchResponseSource",
        ("kUnspecified", "kNetwork", "kHttpCache", "kCacheStorage"),
    ),
    (
        "RecorderNetworkBlockedReasonName",
        "ResourceRequestBlockedReason",
        (
            "kOther",
            "kCSP",
            "kMixedContent",
            "kOrigin",
            "kInspector",
            "kIntegrity",
            "kSubresourceFilter",
            "kContentType",
            "kCoepFrameResourceNeedsCoepHeader",
            "kCoopSandboxedIFrameCannotNavigateToCoopPage",
            "kCorpNotSameOrigin",
            "kCorpNotSameOriginAfterDefaultedToSameOriginByCoep",
            "kCorpNotSameOriginAfterDefaultedToSameOriginByDip",
            "kCorpNotSameOriginAfterDefaultedToSameOriginByCoepAndDip",
            "kCorpNotSameSite",
            "kConversionRequest",
            "kSRIMessageSignatureMismatch",
        ),
    ),
    (
        "RecorderNetworkCorsErrorName",
        "network::mojom::CorsError",
        (
            "kDisallowedByMode",
            "kInvalidResponse",
            "kWildcardOriginNotAllowed",
            "kMissingAllowOriginHeader",
            "kMultipleAllowOriginValues",
            "kInvalidAllowOriginValue",
            "kAllowOriginMismatch",
            "kInvalidAllowCredentials",
            "kCorsDisabledScheme",
            "kPreflightInvalidStatus",
            "kPreflightDisallowedRedirect",
            "kPreflightWildcardOriginNotAllowed",
            "kPreflightMissingAllowOriginHeader",
            "kPreflightMultipleAllowOriginValues",
            "kPreflightInvalidAllowOriginValue",
            "kPreflightAllowOriginMismatch",
            "kPreflightInvalidAllowCredentials",
            "kInvalidAllowMethodsPreflightResponse",
            "kInvalidAllowHeadersPreflightResponse",
            "kMethodDisallowedByPreflightResponse",
            "kHeaderDisallowedByPreflightResponse",
            "kRedirectContainsCredentials",
            "kInsecureLocalNetwork",
            "kInvalidLocalNetworkAccess",
            "kLocalNetworkAccessPermissionDenied",
        ),
    ),
)

BLINK_NETWORK_INCLUDES = (
    BLINK_BRIDGE_INCLUDE,
    *BLINK_COOKIE_ORIGIN_INCLUDES,
    BLINK_EXECUTION_CONTEXT_INCLUDE,
    '#include "net/base/ip_endpoint.h"',
    '#include "net/base/net_errors.h"',
    '#include "services/network/public/cpp/cors/cors_error_status.h"',
    '#include "services/network/public/cpp/request_destination.h"',
    '#include "services/network/public/mojom/cors.mojom-shared.h"',
    '#include "services/network/public/mojom/fetch_api.mojom-shared.h"',
    # The cache mode and priority hint names are compared by enumerator, which
    # needs the complete enum types rather than the forward declarations the
    # worker observer otherwise sees.
    '#include "third_party/blink/public/mojom/fetch/'
    'fetch_api_request.mojom-blink.h"',
    '#include "third_party/blink/public/platform/'
    'resource_request_blocked_reason.h"',
    '#include "third_party/blink/renderer/platform/loader/fetch/'
    'fetch_initiator_info.h"',
    '#include "third_party/blink/renderer/platform/loader/fetch/'
    'fetch_initiator_type_names.h"',
    '#include "third_party/blink/renderer/platform/loader/fetch/'
    'render_blocking_behavior.h"',
    '#include "third_party/blink/renderer/platform/loader/fetch/resource.h"',
    '#include "third_party/blink/renderer/platform/loader/fetch/'
    'resource_error.h"',
    '#include "third_party/blink/renderer/platform/loader/fetch/'
    'resource_load_timing.h"',
    '#include "third_party/blink/renderer/platform/loader/fetch/'
    'resource_request.h"',
    '#include "third_party/blink/renderer/platform/loader/fetch/'
    'resource_response.h"',
    '#include "third_party/blink/renderer/platform/network/http_header_map.h"',
    '#include "third_party/blink/renderer/platform/weborigin/'
    'security_policy.h"',
)

BLINK_NETWORK_HELPER_MARKER = (
    "a11y_recorder::NetworkRequestFacts RecorderNetworkRequestFacts("
)
BLINK_NETWORK_HELPER = (
    "namespace {\n\n"
    "// Names the Chromium enumerations a network record carries. Each name is\n"
    "// the enumerator in kebab case.\n"
    + "\n".join(
        recorder_enum_name_function(function, value_type, enumerators)
        for function, value_type, enumerators in BLINK_NETWORK_ENUMS
    )
    + """
// Copies a header map. Values are copied as Blink holds them; the bridge
// withholds the value of a header that carries a credential.
std::vector<a11y_recorder::NetworkHeader> RecorderNetworkHeaders(
    const HTTPHeaderMap& map) {
  std::vector<a11y_recorder::NetworkHeader> headers;
  for (const auto& header : map) {
    a11y_recorder::NetworkHeader entry;
    entry.name = header.key.Utf8();
    entry.value = header.value.Utf8();
    headers.push_back(std::move(entry));
  }
  return headers;
}

int64_t RecorderNetworkMicrosecondsAfter(base::TimeTicks start,
                                         base::TimeTicks time) {
  if (start.is_null() || time.is_null()) {
    return a11y_recorder::kNetworkTimeUnobserved;
  }
  return (time - start).InMicroseconds();
}

int64_t RecorderNetworkMicrosecondsBeforeNow(base::TimeTicks time) {
  if (time.is_null()) {
    return a11y_recorder::kNetworkTimeUnobserved;
  }
  return (base::TimeTicks::Now() - time).InMicroseconds();
}

a11y_recorder::NetworkLoadTiming RecorderNetworkLoadTiming(
    const ResourceLoadTiming* timing) {
  a11y_recorder::NetworkLoadTiming facts;
  if (!timing || timing->RequestTime().is_null()) {
    return facts;
  }
  const base::TimeTicks start = timing->RequestTime();
  facts.present = true;
  facts.request_start_before_record =
      RecorderNetworkMicrosecondsBeforeNow(start);
  facts.proxy_start =
      RecorderNetworkMicrosecondsAfter(start, timing->ProxyStart());
  facts.proxy_end = RecorderNetworkMicrosecondsAfter(start, timing->ProxyEnd());
  facts.domain_lookup_start =
      RecorderNetworkMicrosecondsAfter(start, timing->DomainLookupStart());
  facts.domain_lookup_end =
      RecorderNetworkMicrosecondsAfter(start, timing->DomainLookupEnd());
  facts.connect_start =
      RecorderNetworkMicrosecondsAfter(start, timing->ConnectStart());
  facts.connect_end =
      RecorderNetworkMicrosecondsAfter(start, timing->ConnectEnd());
  facts.ssl_start = RecorderNetworkMicrosecondsAfter(start, timing->SslStart());
  facts.ssl_end = RecorderNetworkMicrosecondsAfter(start, timing->SslEnd());
  facts.worker_start =
      RecorderNetworkMicrosecondsAfter(start, timing->WorkerStart());
  facts.worker_ready =
      RecorderNetworkMicrosecondsAfter(start, timing->WorkerReady());
  facts.worker_fetch_start =
      RecorderNetworkMicrosecondsAfter(start, timing->WorkerFetchStart());
  facts.worker_respond_with_settled = RecorderNetworkMicrosecondsAfter(
      start, timing->WorkerRespondWithSettled());
  facts.worker_router_evaluation_start = RecorderNetworkMicrosecondsAfter(
      start, timing->WorkerRouterEvaluationStart());
  facts.worker_cache_lookup_start = RecorderNetworkMicrosecondsAfter(
      start, timing->WorkerCacheLookupStart());
  facts.send_start = RecorderNetworkMicrosecondsAfter(start, timing->SendStart());
  facts.send_end = RecorderNetworkMicrosecondsAfter(start, timing->SendEnd());
  facts.receive_headers_start =
      RecorderNetworkMicrosecondsAfter(start, timing->ReceiveHeadersStart());
  facts.receive_headers_end =
      RecorderNetworkMicrosecondsAfter(start, timing->ReceiveHeadersEnd());
  facts.receive_non_informational_headers_start =
      RecorderNetworkMicrosecondsAfter(
          start, timing->ReceiveNonInformationalHeadersStart());
  facts.receive_early_hints_start = RecorderNetworkMicrosecondsAfter(
      start, timing->ReceiveEarlyHintsStart());
  facts.push_start = RecorderNetworkMicrosecondsAfter(start, timing->PushStart());
  facts.push_end = RecorderNetworkMicrosecondsAfter(start, timing->PushEnd());
  facts.response_end =
      RecorderNetworkMicrosecondsAfter(start, timing->ResponseEnd());
  return facts;
}

// Reads one request. The initiator position is recorded one-based, and is
// absent when Blink recorded none.
a11y_recorder::NetworkRequestFacts RecorderNetworkRequestFacts(
    const ResourceRequest& request,
    ResourceType resource_type,
    const FetchInitiatorInfo& initiator,
    RenderBlockingBehavior render_blocking_behavior) {
  a11y_recorder::NetworkRequestFacts facts;
  facts.inspector_id = request.InspectorId();
  facts.request_id = request.GetDevToolsId().Utf8();
  facts.url = request.Url().GetString().Utf8();
  facts.method = request.HttpMethod().Utf8();
  if (const char* type_name =
          Resource::ResourceTypeToString(resource_type, initiator.name)) {
    facts.resource_type = type_name;
  }
  facts.initiator_type = initiator.name.Utf8();
  facts.initiator_url = initiator.initiator_url.GetString().Utf8();
  if (!(initiator.position == TextPosition::BelowRangePosition())) {
    facts.initiator_line = initiator.position.line_.OneBasedInt();
    facts.initiator_column = initiator.position.column_.OneBasedInt();
  }
  facts.link_preload = initiator.is_link_preload;
  facts.internal = initiator.name == fetch_initiator_type_names::kInternal;
  facts.destination = network::RequestDestinationToString(
      request.GetRequestDestination(),
      network::EmptyRequestDestinationOption::kUseFiveCharEmptyString);
  facts.mode = RecorderNetworkRequestModeName(request.GetMode());
  facts.credentials_mode =
      RecorderNetworkCredentialsModeName(request.GetCredentialsMode());
  facts.redirect_mode =
      RecorderNetworkRedirectModeName(request.GetRedirectMode());
  facts.cache_mode = RecorderNetworkCacheModeName(request.GetCacheMode());
  facts.priority = RecorderNetworkPriorityName(request.Priority());
  facts.initial_priority =
      RecorderNetworkPriorityName(request.InitialPriority());
  facts.fetch_priority_hint =
      RecorderNetworkFetchPriorityHintName(request.GetFetchPriorityHint());
  facts.render_blocking =
      RecorderNetworkRenderBlockingName(render_blocking_behavior);
  facts.referrer = request.ReferrerString().Utf8();
  facts.referrer_policy =
      SecurityPolicy::ReferrerPolicyAsString(request.GetReferrerPolicy())
          .Utf8();
  facts.keepalive = request.GetKeepalive();
  facts.user_gesture = request.HasUserGesture();
  facts.ad_resource = request.IsAdResource();
  facts.form_submission = request.IsFormSubmission();
  facts.headers = RecorderNetworkHeaders(request.HttpHeaderFields());
  return facts;
}

a11y_recorder::NetworkResponseFacts RecorderNetworkResponseFacts(
    const ResourceResponse& response) {
  a11y_recorder::NetworkResponseFacts facts;
  facts.url = response.CurrentRequestUrl().GetString().Utf8();
  facts.response_url = response.ResponseUrl().GetString().Utf8();
  facts.status_code = response.HttpStatusCode();
  facts.status_text = response.HttpStatusText().Utf8();
  facts.mime_type = response.MimeType().Utf8();
  facts.charset = response.TextEncodingName().Utf8();
  facts.alpn_protocol = response.AlpnNegotiatedProtocol().Utf8();
  facts.connection_info = response.ConnectionInfoString().Utf8();
  const net::IPEndPoint& remote = response.RemoteIPEndpoint();
  if (remote.address().IsValid()) {
    facts.remote_ip = remote.address().ToString();
    facts.remote_port = remote.port();
  }
  facts.connection_id = response.ConnectionID();
  facts.connection_reused = response.ConnectionReused();
  facts.was_cached = response.WasCached();
  facts.fetched_via_service_worker = response.WasFetchedViaServiceWorker();
  facts.service_worker_response_source = RecorderNetworkResponseSourceName(
      response.GetServiceWorkerResponseSource());
  facts.in_prefetch_cache = response.WasInPrefetchCache();
  facts.network_accessed = response.NetworkAccessed();
  facts.from_archive = response.FromArchive();
  facts.cookie_in_request = response.WasCookieInRequest();
  facts.response_type = RecorderNetworkResponseTypeName(response.GetType());
  facts.encoded_data_length = response.EncodedDataLength();
  facts.expected_content_length = response.ExpectedContentLength();
  facts.headers = RecorderNetworkHeaders(response.HttpHeaderFields());
  facts.timing = RecorderNetworkLoadTiming(response.GetResourceLoadTiming());
  return facts;
}

a11y_recorder::NetworkFailureFacts RecorderNetworkFailureFacts(
    const ResourceError& error,
    bool internal) {
  a11y_recorder::NetworkFailureFacts facts;
  facts.url = error.FailingURL().Utf8();
  facts.net_error = error.ErrorCode();
  if (error.ErrorCode() != 0) {
    facts.net_error_name = net::ErrorToShortString(error.ErrorCode());
  }
  facts.cancellation = error.IsCancellation();
  facts.timeout = error.IsTimeout();
  facts.access_check = error.IsAccessCheck();
  facts.blocked_by_response = error.WasBlockedByResponse();
  facts.blocked_by_orb = error.WasBlockedByORB();
  facts.has_copy_in_cache = error.HasCopyInCache();
  facts.cancelled_from_http_error = error.IsCancelledFromHttpError();
  facts.internal = internal;
  if (std::optional<ResourceRequestBlockedReason> reason =
          error.GetResourceRequestBlockedReason()) {
    facts.blocked_reason = RecorderNetworkBlockedReasonName(*reason);
  }
  if (std::optional<network::CorsErrorStatus> cors =
          error.CorsErrorStatus()) {
    facts.cors_error = RecorderNetworkCorsErrorName(cors->cors_error);
    facts.cors_failed_parameter = cors->failed_parameter;
  }
  return facts;
}

}  // namespace

"""
)

# Each observer names the script context its loads belong to. A frame's loads
# belong to its document; a worker's loads belong to the worker global scope,
# which has no document.
BLINK_FRAME_NETWORK_SCOPE_HELPER_MARKER = (
    "a11y_recorder::NetworkScope RecorderFrameNetworkScope("
)
BLINK_FRAME_NETWORK_SCOPE_HELPER = """\
namespace {

a11y_recorder::NetworkScope RecorderFrameNetworkScope(Document& document) {
  a11y_recorder::NetworkScope scope;
  scope.context_kind = "window";
  scope.document_node_id = document.GetDomNodeId();
  scope.document_token = document.Token().ToString();
  return scope;
}

}  // namespace

"""
BLINK_WORKER_NETWORK_SCOPE_HELPER_MARKER = (
    "a11y_recorder::NetworkScope RecorderWorkerNetworkScope("
)
BLINK_WORKER_NETWORK_SCOPE_HELPER = """\
namespace {

a11y_recorder::NetworkScope RecorderWorkerNetworkScope(
    ExecutionContext* context,
    const base::UnguessableToken& worker_token,
    const KURL& global_object_url) {
  a11y_recorder::NetworkScope scope;
  scope.context_kind = "other";
  if (context && context->IsDedicatedWorkerGlobalScope()) {
    scope.context_kind = "dedicated-worker";
  } else if (context && context->IsSharedWorkerGlobalScope()) {
    scope.context_kind = "shared-worker";
  } else if (context && context->IsServiceWorkerGlobalScope()) {
    scope.context_kind = "service-worker";
  } else if (context && context->IsWorkletGlobalScope()) {
    scope.context_kind = "worklet";
  }
  if (!worker_token.is_empty()) {
    scope.worker_token = worker_token.ToString();
  }
  scope.global_object_url = global_object_url.GetString().Utf8();
  return scope;
}

}  // namespace

"""

BLINK_FRAME_NETWORK_SCOPE = "RecorderFrameNetworkScope(*document_)"
BLINK_WORKER_NETWORK_SCOPE = (
    "RecorderWorkerNetworkScope(\n"
    "            worker_fetch_context_->GetExecutionContext(),\n"
    "            devtools_worker_token_,\n"
    "            fetcher_properties_->GetFetchClientSettingsObject()\n"
    "                .GlobalObjectUrl())"
)


def blink_network_request_hook(scope: str, origin_context: str) -> str:
    return f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkNetworkRequest(
        {scope},
        RecorderNetworkRequestFacts(request, resource_type,
                                    options.initiator_info,
                                    render_blocking_behavior),
        !redirect_response.IsNull(),
        RecorderNetworkResponseFacts(redirect_response),
        RecorderCookieCallOrigin({origin_context}));
  }}
"""


def blink_network_response_hook(scope: str) -> str:
    return f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkNetworkResponse(
        {scope},
        identifier, request.GetDevToolsId().Utf8(),
        response_source == ResponseSource::kFromMemoryCache,
        RecorderNetworkResponseFacts(response));
  }}
"""


def blink_network_finished_hook(scope: str) -> str:
    return f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkNetworkFinished(
        {scope},
        identifier, encoded_data_length, decoded_body_length,
        RecorderNetworkMicrosecondsBeforeNow(finish_time));
  }}
"""


def blink_network_failed_hook(scope: str) -> str:
    return f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkNetworkFailed(
        {scope},
        identifier,
        RecorderNetworkFailureFacts(error, is_internal_request.value()));
  }}
"""


def blink_network_memory_cache_definition(observer: str, scope: str) -> str:
    return f"""\
void {observer}::RecordMemoryCacheUseForRecorder(
    const ResourceRequest& request,
    const Resource& resource,
    RenderBlockingBehavior render_blocking_behavior,
    bool is_static_data) {{
  if (!a11y_recorder::IsRecorderActive()) {{
    return;
  }}
  a11y_recorder::RecordBlinkMemoryCacheUse(
      {scope},
      is_static_data,
      RecorderNetworkRequestFacts(request, resource.GetType(),
                                  resource.Options().initiator_info,
                                  render_blocking_behavior),
      RecorderNetworkResponseFacts(resource.GetResponse()));
}}

"""


BLINK_FRAME_NETWORK_REQUEST_HOOK = blink_network_request_hook(
    BLINK_FRAME_NETWORK_SCOPE, "document_->GetExecutionContext()"
)
BLINK_FRAME_NETWORK_RESPONSE_HOOK = blink_network_response_hook(
    BLINK_FRAME_NETWORK_SCOPE
)
BLINK_FRAME_NETWORK_FINISHED_HOOK = blink_network_finished_hook(
    BLINK_FRAME_NETWORK_SCOPE
)
BLINK_FRAME_NETWORK_FAILED_HOOK = blink_network_failed_hook(
    BLINK_FRAME_NETWORK_SCOPE
)
BLINK_FRAME_MEMORY_CACHE_DEFINITION = blink_network_memory_cache_definition(
    "ResourceLoadObserverForFrame", BLINK_FRAME_NETWORK_SCOPE
)
BLINK_WORKER_NETWORK_REQUEST_HOOK = blink_network_request_hook(
    BLINK_WORKER_NETWORK_SCOPE, "worker_fetch_context_->GetExecutionContext()"
)
BLINK_WORKER_NETWORK_RESPONSE_HOOK = blink_network_response_hook(
    BLINK_WORKER_NETWORK_SCOPE
)
BLINK_WORKER_NETWORK_FINISHED_HOOK = blink_network_finished_hook(
    BLINK_WORKER_NETWORK_SCOPE
)
BLINK_WORKER_NETWORK_FAILED_HOOK = blink_network_failed_hook(
    BLINK_WORKER_NETWORK_SCOPE
)
BLINK_WORKER_MEMORY_CACHE_DEFINITION = blink_network_memory_cache_definition(
    "ResourceLoadObserverForWorker", BLINK_WORKER_NETWORK_SCOPE
)

BLINK_MEMORY_CACHE_OBSERVER_DECLARATION = """\
  // Called once for every use of a resource from Blink's memory cache,
  // whether or not DevTools is attached, so that the recorder observes uses
  // that send no request.
  virtual void RecordMemoryCacheUseForRecorder(
      const ResourceRequest&,
      const Resource&,
      RenderBlockingBehavior,
      bool is_static_data) {}

"""
BLINK_MEMORY_CACHE_OVERRIDE_DECLARATION = """\
  void RecordMemoryCacheUseForRecorder(const ResourceRequest&,
                                       const Resource&,
                                       RenderBlockingBehavior,
                                       bool is_static_data) override;
"""
BLINK_MEMORY_CACHE_FETCHER_ANCHOR = """\
  if (!is_static_data) {
    MarkEarlyHintConsumedIfNeeded(request.InspectorId(), resource,
                                  resource->GetResponse());
  }
"""
BLINK_MEMORY_CACHE_FETCHER_HOOK = BLINK_MEMORY_CACHE_FETCHER_ANCHOR + """\
  resource_load_observer_->RecordMemoryCacheUseForRecorder(
      request, *resource, render_blocking_behavior, is_static_data);
"""

# Request identifiers. The network service reports a request's wire headers
# only for a request that carries a DevTools request identifier, and Blink
# assigns one only while DevTools is attached. While the recorder is connected
# these hooks assign the identifier DevTools would assign, so that wire
# headers are reported for every recorded request. A request Blink marks as
# internal is left without one, as DevTools leaves it.
BLINK_FRAME_REQUEST_ID_ANCHOR = """\
  if (!GetResourceFetcherProperties().IsDetached()) {
    probe::SetDevToolsIds(Probe(), request, options.initiator_info);
  }
"""
BLINK_FRAME_REQUEST_ID_HOOK = BLINK_FRAME_REQUEST_ID_ANCHOR + """\
  if (a11y_recorder::IsRecorderActive() && request.GetDevToolsId().IsNull() &&
      options.initiator_info.name != fetch_initiator_type_names::kInternal) {
    request.SetDevToolsId(
        IdentifiersFactory::SubresourceRequestId(request.InspectorId()));
  }
"""
BLINK_WORKER_REQUEST_ID_ANCHOR = """\
  if (!GetResourceFetcherProperties().IsDetached()) {
    probe::SetDevToolsIds(Probe(), out_request, options.initiator_info);
  }
"""
BLINK_WORKER_REQUEST_ID_HOOK = BLINK_WORKER_REQUEST_ID_ANCHOR + """\
  if (a11y_recorder::IsRecorderActive() &&
      out_request.GetDevToolsId().IsNull() &&
      options.initiator_info.name != fetch_initiator_type_names::kInternal) {
    out_request.SetDevToolsId(
        IdentifiersFactory::SubresourceRequestId(out_request.InspectorId()));
  }
"""
BLINK_REQUEST_ID_INCLUDES = (
    BLINK_BRIDGE_INCLUDE,
    '#include "third_party/blink/renderer/core/inspector/identifiers_factory.h"',
    '#include "third_party/blink/renderer/platform/loader/fetch/'
    'fetch_initiator_type_names.h"',
)
CONTENT_NAVIGATION_REQUEST_ID_ANCHOR = """\
  devtools_instrumentation::MaybeAssignResourceRequestId(
      frame_tree_node, request_info.devtools_navigation_token.ToString(),
      *new_request);
"""
CONTENT_NAVIGATION_REQUEST_ID_HOOK = CONTENT_NAVIGATION_REQUEST_ID_ANCHOR + """\
  if (a11y_recorder::IsRecorderActive() && !new_request->devtools_request_id) {
    new_request->devtools_request_id =
        request_info.devtools_navigation_token.ToString();
  }
"""

CONTENT_NETWORK_HEADERS_INCLUDES = (
    CONTENT_NAVIGATION_INCLUDE,
    '#include "content/browser/renderer_host/render_frame_host_impl.h"',
    *CONTENT_COOKIE_ACCESS_INCLUDES,
    '#include "net/cookies/cookie_access_result.h"',
)
CONTENT_NETWORK_HEADERS_HELPER_MARKER = (
    "std::vector<a11y_recorder::NetworkHeader> RecorderNetworkRawHeaders("
)
CONTENT_NETWORK_HEADERS_HELPER = """\
namespace {

// Copies the headers the network service reported. The bridge withholds the
// value of a header that carries a credential.
std::vector<a11y_recorder::NetworkHeader> RecorderNetworkRawHeaders(
    const std::vector<network::mojom::HttpRawHeaderPairPtr>& raw_headers) {
  std::vector<a11y_recorder::NetworkHeader> headers;
  headers.reserve(raw_headers.size());
  for (const auto& raw_header : raw_headers) {
    a11y_recorder::NetworkHeader header;
    header.name = raw_header->key;
    header.value = raw_header->value;
    headers.push_back(std::move(header));
  }
  return headers;
}

a11y_recorder::CookieAccessEntry RecorderNetworkCookieEntry(
    const net::CanonicalCookie& cookie,
    const base::Time& now) {
  a11y_recorder::CookieAccessEntry entry;
  entry.parsed = true;
  entry.name = cookie.Name();
  entry.domain = cookie.Domain();
  entry.path = cookie.Path();
  entry.same_site = net::CookieSameSiteToString(cookie.SameSite());
  entry.secure = cookie.SecureAttribute();
  entry.http_only = cookie.IsHttpOnly();
  entry.host_only = cookie.IsHostCookie();
  entry.partitioned = cookie.IsPartitioned();
  entry.persistent = cookie.IsPersistent();
  entry.expired = cookie.IsExpired(now);
  return entry;
}

// Copies the cookies the network service attached to or excluded from a
// request. No cookie value is read.
std::vector<a11y_recorder::CookieAccessEntry> RecorderNetworkRequestCookies(
    const net::CookieAccessResultList& cookies) {
  std::vector<a11y_recorder::CookieAccessEntry> entries;
  entries.reserve(cookies.size());
  const base::Time now = base::Time::Now();
  for (const auto& item : cookies) {
    a11y_recorder::CookieAccessEntry entry =
        RecorderNetworkCookieEntry(item.cookie, now);
    entry.inclusion_status = item.access_result.status.GetDebugString();
    entries.push_back(std::move(entry));
  }
  return entries;
}

// Copies the cookies a response set or tried to set. A Set-Cookie line the
// network service could not parse contributes only the name read from it.
std::vector<a11y_recorder::CookieAccessEntry> RecorderNetworkResponseCookies(
    const net::CookieAndLineAccessResultList& cookies) {
  std::vector<a11y_recorder::CookieAccessEntry> entries;
  entries.reserve(cookies.size());
  const base::Time now = base::Time::Now();
  for (const auto& item : cookies) {
    a11y_recorder::CookieAccessEntry entry;
    if (item.cookie) {
      entry = RecorderNetworkCookieEntry(*item.cookie, now);
    } else {
      entry.name =
          a11y_recorder::ReadCookieNameFromSetCookieLine(item.cookie_string);
    }
    entry.inclusion_status = item.access_result.status.GetDebugString();
    entries.push_back(std::move(entry));
  }
  return entries;
}

// Reads the frame an observer was made for, and its page. A worker's
// observer has no frame, and a frame that no longer exists is reported as
// none.
void RecorderNetworkFrameIds(FrameTreeNodeId frame_tree_node_id,
                             int* page_frame_tree_node_id,
                             int* recorded_frame_tree_node_id) {
  *page_frame_tree_node_id = -1;
  *recorded_frame_tree_node_id = -1;
  if (frame_tree_node_id.is_null()) {
    return;
  }
  FrameTreeNode* node = FrameTreeNode::GloballyFindByID(frame_tree_node_id);
  if (!node) {
    return;
  }
  *recorded_frame_tree_node_id = frame_tree_node_id.GetUnsafeValue();
  *page_frame_tree_node_id = *recorded_frame_tree_node_id;
  if (RenderFrameHostImpl* current = node->current_frame_host()) {
    *page_frame_tree_node_id =
        current->GetMainFrame()->GetFrameTreeNodeId().GetUnsafeValue();
  }
}

}  // namespace

"""
CONTENT_NETWORK_HEADERS_HELPER_ANCHOR = (
    "NetworkServiceDevToolsObserver::NetworkServiceDevToolsObserver(\n"
)
CONTENT_RAW_REQUEST_ANCHOR = """\
        applied_network_conditions_id) {
  auto* host = GetDevToolsAgentHost();
"""
CONTENT_RAW_REQUEST_HOOK = """\
        applied_network_conditions_id) {
  if (a11y_recorder::IsRecorderActive()) {
    int recorder_page_frame_tree_node_id = -1;
    int recorder_frame_tree_node_id = -1;
    RecorderNetworkFrameIds(frame_tree_node_id_,
                            &recorder_page_frame_tree_node_id,
                            &recorder_frame_tree_node_id);
    a11y_recorder::RecordBrowserNetworkRequestHeaders(
        recorder_page_frame_tree_node_id, recorder_frame_tree_node_id,
        devtools_agent_id_, devtools_request_id,
        timestamp.is_null() ? a11y_recorder::kNetworkTimeUnobserved
                            : (base::TimeTicks::Now() - timestamp)
                                  .InMicroseconds(),
        RecorderNetworkRawHeaders(request_headers),
        RecorderNetworkRequestCookies(request_cookie_list));
  }
  auto* host = GetDevToolsAgentHost();
"""
CONTENT_RAW_RESPONSE_ANCHOR = """\
    const std::optional<net::CookiePartitionKey>& cookie_partition_key) {
  auto* host = GetDevToolsAgentHost();
"""
CONTENT_RAW_RESPONSE_HOOK = """\
    const std::optional<net::CookiePartitionKey>& cookie_partition_key) {
  if (a11y_recorder::IsRecorderActive()) {
    int recorder_page_frame_tree_node_id = -1;
    int recorder_frame_tree_node_id = -1;
    RecorderNetworkFrameIds(frame_tree_node_id_,
                            &recorder_page_frame_tree_node_id,
                            &recorder_frame_tree_node_id);
    a11y_recorder::RecordBrowserNetworkResponseHeaders(
        recorder_page_frame_tree_node_id, recorder_frame_tree_node_id,
        devtools_agent_id_, devtools_request_id, http_status_code,
        RecorderNetworkRawHeaders(response_headers),
        RecorderNetworkResponseCookies(response_cookie_list));
  }
  auto* host = GetDevToolsAgentHost();
"""

CONTENT_NAVIGATION_RESPONSE_INCLUDES = (
    '#include "content/public/browser/navigation_handle_timing.h"',
    '#include "net/base/ip_endpoint.h"',
    '#include "net/base/net_errors.h"',
    '#include "net/http/http_connection_info.h"',
    '#include "net/http/http_request_headers.h"',
    '#include "net/http/http_response_headers.h"',
    '#include "services/network/public/mojom/url_response_head.mojom.h"',
)
CONTENT_NAVIGATION_RESPONSE_HOOK = """\
  if (a11y_recorder::IsRecorderActive()) {
    a11y_recorder::NavigationResponseFacts recorder_response;
    recorder_response.request_id =
        NavigationRequest::From(navigation_handle)
            ->devtools_navigation_token()
            .ToString();
    recorder_response.url = navigation_handle->GetURL().spec();
    recorder_response.method = navigation_handle->GetRequestMethod();
    recorder_response.committed = navigation_handle->HasCommitted();
    recorder_response.error_page =
        navigation_handle->HasCommitted() && navigation_handle->IsErrorPage();
    recorder_response.same_document = navigation_handle->IsSameDocument();
    recorder_response.download = navigation_handle->IsDownload();
    recorder_response.back_forward_cache =
        navigation_handle->IsServedFromBackForwardCache();
    recorder_response.net_error = navigation_handle->GetNetErrorCode();
    if (recorder_response.net_error != net::OK) {
      recorder_response.net_error_name =
          net::ErrorToShortString(recorder_response.net_error);
    }
    for (const GURL& recorder_url : navigation_handle->GetRedirectChain()) {
      recorder_response.redirect_chain.push_back(recorder_url.spec());
    }
    for (const auto& recorder_header :
         navigation_handle->GetRequestHeaders().GetHeaderVector()) {
      a11y_recorder::NetworkHeader recorder_entry;
      recorder_entry.name = recorder_header.key;
      recorder_entry.value = recorder_header.value;
      recorder_response.request_headers.push_back(std::move(recorder_entry));
    }
    if (const net::HttpResponseHeaders* recorder_headers =
            navigation_handle->GetResponseHeaders()) {
      recorder_response.response_present = true;
      recorder_response.status_code = recorder_headers->response_code();
      recorder_response.status_text = recorder_headers->GetStatusText();
      std::string recorder_mime_type;
      if (recorder_headers->GetMimeType(&recorder_mime_type)) {
        recorder_response.mime_type = recorder_mime_type;
      }
      size_t recorder_iterator = 0;
      std::string recorder_name;
      std::string recorder_value;
      while (recorder_headers->EnumerateHeaderLines(
          &recorder_iterator, &recorder_name, &recorder_value)) {
        a11y_recorder::NetworkHeader recorder_entry;
        recorder_entry.name = recorder_name;
        recorder_entry.value = recorder_value;
        recorder_response.response_headers.push_back(
            std::move(recorder_entry));
      }
      recorder_response.was_cached = navigation_handle->WasResponseCached();
      // The response head is read directly rather than through
      // GetSocketAddress, which asserts a navigation state that a failed
      // navigation with a response need not have reached.
      if (const network::mojom::URLResponseHead* recorder_head =
              NavigationRequest::From(navigation_handle)->response()) {
        const net::IPEndPoint& recorder_socket = recorder_head->remote_endpoint;
        if (recorder_socket.address().IsValid()) {
          recorder_response.remote_ip = recorder_socket.address().ToString();
          recorder_response.remote_port = recorder_socket.port();
        }
      }
      recorder_response.connection_info = std::string(
          net::HttpConnectionInfoToString(
              navigation_handle->GetConnectionInfo()));
    }
    const base::TimeTicks recorder_start = navigation_handle->NavigationStart();
    if (!recorder_start.is_null()) {
      const NavigationHandleTiming& recorder_timing =
          navigation_handle->GetNavigationHandleTiming();
      auto recorder_after = [&recorder_start](base::TimeTicks time) {
        return time.is_null() ? a11y_recorder::kNetworkTimeUnobserved
                              : (time - recorder_start).InMicroseconds();
      };
      a11y_recorder::NavigationResponseTiming& recorder_facts =
          recorder_response.timing;
      recorder_facts.present = true;
      recorder_facts.navigation_start_before_record =
          (base::TimeTicks::Now() - recorder_start).InMicroseconds();
      recorder_facts.loader_start =
          recorder_after(recorder_timing.loader_start_time);
      recorder_facts.first_request_start =
          recorder_after(recorder_timing.first_request_start_time);
      recorder_facts.first_response_start =
          recorder_after(recorder_timing.first_response_start_time);
      recorder_facts.first_loader_callback =
          recorder_after(recorder_timing.first_loader_callback_time);
      recorder_facts.final_request_start =
          recorder_after(recorder_timing.final_request_start_time);
      recorder_facts.final_response_start =
          recorder_after(recorder_timing.final_response_start_time);
      recorder_facts.final_non_informational_response_start = recorder_after(
          recorder_timing.final_non_informational_response_start_time);
      recorder_facts.final_loader_callback =
          recorder_after(recorder_timing.final_loader_callback_time);
      recorder_facts.request_failed =
          recorder_after(recorder_timing.request_failed_time);
      recorder_facts.commit_sent =
          recorder_after(recorder_timing.navigation_commit_sent_time);
      recorder_facts.commit_received =
          recorder_after(recorder_timing.navigation_commit_received_time);
      recorder_facts.commit_reply_sent =
          recorder_after(recorder_timing.navigation_commit_reply_sent_time);
      recorder_facts.did_commit =
          recorder_after(recorder_timing.navigation_did_commit_time);
      recorder_facts.final_request_domain_lookup_start = recorder_after(
          recorder_timing.final_request_domain_lookup_start_time);
      recorder_facts.final_request_domain_lookup_end = recorder_after(
          recorder_timing.final_request_domain_lookup_end_time);
      recorder_facts.final_request_connect_start = recorder_after(
          recorder_timing.final_request_connect_start_time);
      recorder_facts.final_request_connect_end = recorder_after(
          recorder_timing.final_request_connect_end_time);
      recorder_facts.final_request_ssl_start = recorder_after(
          recorder_timing.final_request_ssl_start_time);
    }
    a11y_recorder::RecordBrowserNavigationResponse(
        navigation_handle->GetNavigationId(),
        recorder_page_frame_tree_node_id,
        navigation_handle->GetFrameTreeNodeId().GetUnsafeValue(),
        recorder_document_navigation_id, recorder_document_token,
        std::move(recorder_response));
  }
"""


def apply_network_hooks(
    text: str, hooks: tuple[tuple[str, str], ...], path: Path
) -> str:
    """Writes each hook after the opening line of its anchor.

    Each anchor is a function's last signature line and the first line of its
    body. The written text holds the anchor's signature line, the hook, and the
    anchor's body line, so it is its own presence guard.
    """
    for anchor, replacement in hooks:
        text = apply_cookie_hook(text, anchor, replacement, path)
    return text


def network_hook(signature_line: str, hook: str, body_line: str) -> tuple[str, str]:
    return (signature_line + body_line, signature_line + hook + body_line)


def renamed_network_hook(
    old_signature_line: str, new_signature_line: str, hook: str, body_line: str
) -> tuple[str, str]:
    return (old_signature_line + body_line, new_signature_line + hook + body_line)


# Each entry is an anchor and its replacement, in source order.
BLINK_FRAME_NETWORK_HOOKS = (
    network_hook(
        "    const Resource* resource) {\n",
        BLINK_FRAME_NETWORK_REQUEST_HOOK,
        "  LocalFrame* frame = document_->GetFrame();\n",
    ),
    network_hook(
        "    ResponseSource response_source) {\n",
        BLINK_FRAME_NETWORK_RESPONSE_HOOK,
        "  LocalFrame* frame = document_->GetFrame();\n",
    ),
    network_hook(
        "    int64_t decoded_body_length) {\n",
        BLINK_FRAME_NETWORK_FINISHED_HOOK,
        "  LocalFrame* frame = document_->GetFrame();\n",
    ),
    network_hook(
        "    IsInternalRequest is_internal_request) {\n",
        BLINK_FRAME_NETWORK_FAILED_HOOK,
        "  LocalFrame* frame = document_->GetFrame();\n",
    ),
    )
BLINK_WORKER_NETWORK_HOOKS = (
    network_hook(
        "    const Resource* resource) {\n",
        BLINK_WORKER_NETWORK_REQUEST_HOOK,
        "  probe::WillSendRequest(\n",
    ),
    renamed_network_hook(
        "    ResponseSource) {\n",
        "    ResponseSource response_source) {\n",
        BLINK_WORKER_NETWORK_RESPONSE_HOOK,
        "  RecordPrivateNetworkAccessFeature(\n",
    ),
    network_hook(
        "    int64_t decoded_body_length) {\n",
        BLINK_WORKER_NETWORK_FINISHED_HOOK,
        "  probe::DidFinishLoading(probe_, identifier, nullptr, "
        "finish_time,\n",
    ),
    renamed_network_hook(
        "                                                   "
        "IsInternalRequest) {\n",
        "                                                   "
        "IsInternalRequest is_internal_request) {\n",
        BLINK_WORKER_NETWORK_FAILED_HOOK,
        "  probe::DidFailLoading(probe_, identifier, nullptr, error,\n",
    ),
    )


def patch_blink_frame_network_observer(path: Path) -> None:
    text = read_source(path)
    text = add_includes_after(
        text,
        '#include "third_party/blink/renderer/core/loader/'
        'resource_load_observer_for_frame.h"',
        (*BLINK_NETWORK_INCLUDES, BLINK_DOCUMENT_INCLUDE),
        path,
    )
    anchor = "ResourceLoadObserverForFrame::ResourceLoadObserverForFrame(\n"
    for helper, marker in (
        (BLINK_COOKIE_ORIGIN_HELPER, BLINK_COOKIE_ORIGIN_HELPER_MARKER),
        (BLINK_NETWORK_HELPER, BLINK_NETWORK_HELPER_MARKER),
        (
            BLINK_FRAME_NETWORK_SCOPE_HELPER,
            BLINK_FRAME_NETWORK_SCOPE_HELPER_MARKER,
        ),
    ):
        text = insert_before_once(text, anchor, helper, marker, path)
    text = apply_network_hooks(text, BLINK_FRAME_NETWORK_HOOKS, path)
    text = insert_before_once(
        text,
        "bool ResourceLoadObserverForFrame::InterestedInAllRequests() {\n",
        BLINK_FRAME_MEMORY_CACHE_DEFINITION,
        "ResourceLoadObserverForFrame::RecordMemoryCacheUseForRecorder(",
        path,
    )
    write_patched(path, text)


def patch_blink_worker_network_observer(path: Path) -> None:
    text = read_source(path)
    text = add_includes_after(
        text,
        '#include "third_party/blink/renderer/core/loader/'
        'resource_load_observer_for_worker.h"',
        BLINK_NETWORK_INCLUDES,
        path,
    )
    anchor = "ResourceLoadObserverForWorker::ResourceLoadObserverForWorker(\n"
    for helper, marker in (
        (BLINK_COOKIE_ORIGIN_HELPER, BLINK_COOKIE_ORIGIN_HELPER_MARKER),
        (BLINK_NETWORK_HELPER, BLINK_NETWORK_HELPER_MARKER),
        (
            BLINK_WORKER_NETWORK_SCOPE_HELPER,
            BLINK_WORKER_NETWORK_SCOPE_HELPER_MARKER,
        ),
    ):
        text = insert_before_once(text, anchor, helper, marker, path)
    text = apply_network_hooks(text, BLINK_WORKER_NETWORK_HOOKS, path)
    text = insert_before_once(
        text,
        "bool ResourceLoadObserverForWorker::InterestedInAllRequests() {\n",
        BLINK_WORKER_MEMORY_CACHE_DEFINITION,
        "ResourceLoadObserverForWorker::RecordMemoryCacheUseForRecorder(",
        path,
    )
    write_patched(path, text)


def patch_blink_network_observer_header(path: Path) -> None:
    text = read_source(path)
    text = insert_before_once(
        text,
        "  bool InterestedInAllRequests() override;\n",
        BLINK_MEMORY_CACHE_OVERRIDE_DECLARATION,
        "  void RecordMemoryCacheUseForRecorder(",
        path,
    )
    write_patched(path, text)


def patch_blink_resource_load_observer(path: Path) -> None:
    text = read_source(path)
    text = insert_before_once(
        text,
        "  virtual bool InterestedInAllRequests() = 0;\n",
        BLINK_MEMORY_CACHE_OBSERVER_DECLARATION,
        "  virtual void RecordMemoryCacheUseForRecorder(",
        path,
    )
    write_patched(path, text)


def patch_blink_resource_fetcher_memory_cache(path: Path) -> None:
    text = read_source(path)
    text = apply_cookie_hook(
        text,
        BLINK_MEMORY_CACHE_FETCHER_ANCHOR,
        BLINK_MEMORY_CACHE_FETCHER_HOOK,
        path,
    )
    write_patched(path, text)


def patch_blink_fetch_context_request_ids(
    path: Path, own_include: str, anchor: str, hook: str
) -> None:
    text = read_source(path)
    text = add_includes_after(text, own_include, BLINK_REQUEST_ID_INCLUDES, path)
    text = apply_cookie_hook(text, anchor, hook, path)
    write_patched(path, text)


def patch_content_navigation_request_id(path: Path) -> None:
    text = read_source(path)
    text = add_includes_after(
        text,
        '#include "content/browser/loader/navigation_url_loader_impl.h"',
        (CONTENT_NAVIGATION_INCLUDE,),
        path,
    )
    text = apply_cookie_hook(
        text,
        CONTENT_NAVIGATION_REQUEST_ID_ANCHOR,
        CONTENT_NAVIGATION_REQUEST_ID_HOOK,
        path,
    )
    write_patched(path, text)


def patch_content_network_headers(path: Path) -> None:
    text = read_source(path)
    text = add_includes_after(
        text,
        '#include "content/browser/devtools/'
        'network_service_devtools_observer.h"',
        CONTENT_NETWORK_HEADERS_INCLUDES,
        path,
    )
    text = insert_before_once(
        text,
        CONTENT_NETWORK_HEADERS_HELPER_ANCHOR,
        CONTENT_NETWORK_HEADERS_HELPER,
        CONTENT_NETWORK_HEADERS_HELPER_MARKER,
        path,
    )
    text = apply_cookie_hook(
        text, CONTENT_RAW_REQUEST_ANCHOR, CONTENT_RAW_REQUEST_HOOK, path
    )
    text = apply_cookie_hook(
        text, CONTENT_RAW_RESPONSE_ANCHOR, CONTENT_RAW_RESPONSE_HOOK, path
    )
    write_patched(path, text)


def patch_web_contents_navigation_response(path: Path) -> None:
    """Adds the navigation-response record after the completed record.

    The completed hook is written by patch_web_contents_navigation, which runs
    first, so its text is the anchor. The response hook is its own guard.
    """
    text = read_source(path)
    text = add_includes_after(
        text,
        CONTENT_NAVIGATION_INCLUDE,
        CONTENT_NAVIGATION_RESPONSE_INCLUDES,
        path,
    )
    if CONTENT_NAVIGATION_RESPONSE_HOOK not in text:
        text = replace_once(
            text,
            CONTENT_NAVIGATION_COMPLETED_HOOK,
            CONTENT_NAVIGATION_COMPLETED_HOOK + CONTENT_NAVIGATION_RESPONSE_HOOK,
            path,
        )
    write_patched(path, text)


# Realtime network channels. WebSocket, EventSource, and WebTransport traffic
# does not pass through the resource load observers, so each is read where its
# module reports it to DevTools. The hooks sit beside the DevTools probes, so
# each record describes what DevTools would see at the same point.
BLINK_REALTIME_INCLUDES = (
    "#include <algorithm>",
    "#include <string>",
    "#include <vector>",
    BLINK_BRIDGE_INCLUDE,
    *BLINK_COOKIE_ORIGIN_INCLUDES,
    BLINK_EXECUTION_CONTEXT_INCLUDE,
    BLINK_DOCUMENT_INCLUDE,
    '#include \"third_party/blink/renderer/core/frame/local_dom_window.h\"',
    '#include \"third_party/blink/renderer/core/workers/'
    'worker_or_worklet_global_scope.h\"',
    '#include \"base/strings/string_number_conversions.h\"',
    '#include \"net/http/http_version.h\"',
)
BLINK_REALTIME_HELPER_MARKER = (
    "a11y_recorder::NetworkScope RecorderRealtimeNetworkScope("
)
BLINK_REALTIME_HELPER = """\
namespace {

// Names the script context a realtime channel belongs to. A window's channels
// belong to its document; a worker's channels belong to its global scope,
// which has no document.
a11y_recorder::NetworkScope RecorderRealtimeNetworkScope(
    ExecutionContext* context) {
  a11y_recorder::NetworkScope scope;
  scope.context_kind = "other";
  if (!context) {
    return scope;
  }
  if (auto* window = DynamicTo<LocalDOMWindow>(context)) {
    scope.context_kind = "window";
    if (Document* document = window->document()) {
      scope.document_node_id = document->GetDomNodeId();
      scope.document_token = document->Token().ToString();
    }
    return scope;
  }
  if (context->IsDedicatedWorkerGlobalScope()) {
    scope.context_kind = "dedicated-worker";
  } else if (context->IsSharedWorkerGlobalScope()) {
    scope.context_kind = "shared-worker";
  } else if (context->IsServiceWorkerGlobalScope()) {
    scope.context_kind = "service-worker";
  } else if (context->IsWorkletGlobalScope()) {
    scope.context_kind = "worklet";
  }
  if (auto* global = DynamicTo<WorkerOrWorkletGlobalScope>(context)) {
    const base::UnguessableToken& token = global->GetDevToolsToken();
    if (!token.is_empty()) {
      scope.worker_token = token.ToString();
    }
  }
  scope.global_object_url = context->Url().GetString().Utf8();
  return scope;
}

// Reads a version the network service reported. Blink receives a WebSocket
// handshake's version as a Mojo structure and a WebTransport response's
// version as the network stack's own type.
[[maybe_unused]] std::string RecorderRealtimeHttpVersion(
    const net::HttpVersion& version) {
  return base::NumberToString(version.major_value()) + "." +
         base::NumberToString(version.minor_value());
}

template <typename MojoVersion>
[[maybe_unused]] std::string RecorderRealtimeHttpVersion(
    const MojoVersion& version) {
  if (!version) {
    return std::string();
  }
  return base::NumberToString(version->major_value) + "." +
         base::NumberToString(version->minor_value);
}

// Copies header names and values. The bridge withholds credential values.
template <typename Headers>
[[maybe_unused]] std::vector<a11y_recorder::NetworkHeader>
RecorderRealtimeMojoHeaders(const Headers& headers) {
  std::vector<a11y_recorder::NetworkHeader> copied;
  for (const auto& header : headers) {
    copied.push_back({header->name.Utf8(), header->value.Utf8()});
  }
  return copied;
}

// Joins the chunks of a received text message, reading no further than the
// bridge scans for credentials.
[[maybe_unused]] std::string RecorderRealtimeChunksText(
    const Vector<base::span<const uint8_t>>& chunks) {
  constexpr size_t kScanLimit = 65536;
  std::string text;
  for (const base::span<const uint8_t>& chunk : chunks) {
    if (text.size() >= kScanLimit) {
      break;
    }
    const size_t take = std::min(chunk.size(), kScanLimit - text.size());
    text.append(reinterpret_cast<const char*>(chunk.data()), take);
  }
  return text;
}

}  // namespace

"""

BLINK_WEBSOCKET_SCOPE = "RecorderRealtimeNetworkScope(execution_context_.Get())"
BLINK_WEBSOCKET_ORIGIN = "RecorderCookieCallOrigin(execution_context_.Get())"

BLINK_WEBSOCKET_CREATED_ANCHOR = """\
  probe::WillCreateWebSocket(execution_context_, identifier_, url, protocol,
                             &devtools_throttling_token);
"""
BLINK_WEBSOCKET_CREATED_HOOK = BLINK_WEBSOCKET_CREATED_ANCHOR + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebSocketCreated(
        {BLINK_WEBSOCKET_SCOPE}, identifier_,
        url.GetString().Utf8(), protocol.Utf8(),
        {BLINK_WEBSOCKET_ORIGIN});
  }}
"""

BLINK_WEBSOCKET_SEND_TEXT_ANCHOR = """\
  probe::DidSendWebSocketMessage(execution_context_, identifier_,
                                 WebSocketOpCode::kOpCodeText, true,
                                 base::as_byte_span(message));
"""
BLINK_WEBSOCKET_SEND_TEXT_HOOK = BLINK_WEBSOCKET_SEND_TEXT_ANCHOR + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebSocketMessage(
        {BLINK_WEBSOCKET_SCOPE}, identifier_,
        /*sent=*/true, "text", base::checked_cast<int64_t>(message.size()),
        message, {BLINK_WEBSOCKET_ORIGIN});
  }}
"""

BLINK_WEBSOCKET_SEND_BLOB_ANCHOR = """\
  probe::DidSendWebSocketMessage(execution_context_, identifier_,
                                 WebSocketOpCode::kOpCodeBinary, true,
                                 base::byte_span_from_cstring(""));
"""
BLINK_WEBSOCKET_SEND_BLOB_HOOK = BLINK_WEBSOCKET_SEND_BLOB_ANCHOR + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebSocketMessage(
        {BLINK_WEBSOCKET_SCOPE}, identifier_,
        /*sent=*/true, "binary",
        base::checked_cast<int64_t>(blob_data_handle->size()), std::string(),
        {BLINK_WEBSOCKET_ORIGIN});
  }}
"""

BLINK_WEBSOCKET_SEND_BUFFER_ANCHOR = """\
  probe::DidSendWebSocketMessage(
      execution_context_, identifier_, WebSocketOpCode::kOpCodeBinary, true,
      buffer.ByteSpan().subspan(byte_offset, byte_length));
"""
BLINK_WEBSOCKET_SEND_BUFFER_HOOK = BLINK_WEBSOCKET_SEND_BUFFER_ANCHOR + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebSocketMessage(
        {BLINK_WEBSOCKET_SCOPE}, identifier_,
        /*sent=*/true, "binary", base::checked_cast<int64_t>(byte_length),
        std::string(), {BLINK_WEBSOCKET_ORIGIN});
  }}
"""

BLINK_WEBSOCKET_CLOSE_ANCHOR = """\
  DVLOG(1) << this << " Close(" << code << ", " << reason << ")";
"""
BLINK_WEBSOCKET_CLOSE_HOOK = BLINK_WEBSOCKET_CLOSE_ANCHOR + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebSocketCloseRequested(
        {BLINK_WEBSOCKET_SCOPE}, identifier_,
        code == kCloseEventCodeNotSpecified ? -1 : code, reason.Utf8(),
        {BLINK_WEBSOCKET_ORIGIN});
  }}
"""

BLINK_WEBSOCKET_FAIL_ANCHOR = """\
  probe::DidReceiveWebSocketMessageError(execution_context_, identifier_,
                                         reason);
"""
BLINK_WEBSOCKET_FAIL_HOOK = BLINK_WEBSOCKET_FAIL_ANCHOR + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebSocketError(
        {BLINK_WEBSOCKET_SCOPE}, identifier_, reason.Utf8());
  }}
"""

BLINK_WEBSOCKET_DISCONNECT_ANCHOR = """\
    probe::DidCloseWebSocket(execution_context_.Get(), identifier_);
"""
BLINK_WEBSOCKET_DISCONNECT_HOOK = BLINK_WEBSOCKET_DISCONNECT_ANCHOR + f"""\
    if (a11y_recorder::IsRecorderActive()) {{
      a11y_recorder::RecordBlinkWebSocketClosed(
          {BLINK_WEBSOCKET_SCOPE}, identifier_,
          "disconnected", false, 0, std::string());
    }}
"""

BLINK_WEBSOCKET_HANDSHAKE_REQUEST_ANCHOR = """\
  probe::WillSendWebSocketHandshakeRequest(execution_context_, identifier_,
                                           request.get());
"""
BLINK_WEBSOCKET_HANDSHAKE_REQUEST_HOOK = (
    BLINK_WEBSOCKET_HANDSHAKE_REQUEST_ANCHOR
    + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebSocketHandshakeRequest(
        {BLINK_WEBSOCKET_SCOPE}, identifier_,
        request->url.GetString().Utf8(),
        RecorderRealtimeMojoHeaders(request->headers));
  }}
"""
)

BLINK_WEBSOCKET_HANDSHAKE_RESPONSE_ANCHOR = """\
  probe::DidReceiveWebSocketHandshakeResponse(execution_context_, identifier_,
                                              handshake_request_.get(),
                                              response.get());
"""
BLINK_WEBSOCKET_HANDSHAKE_RESPONSE_HOOK = (
    BLINK_WEBSOCKET_HANDSHAKE_RESPONSE_ANCHOR
    + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RealtimeHandshakeResponseFacts recorder_response;
    recorder_response.url = response->url.GetString().Utf8();
    recorder_response.http_version =
        RecorderRealtimeHttpVersion(response->http_version);
    recorder_response.status_code = response->status_code;
    recorder_response.status_text = response->status_text.Utf8();
    if (response->remote_endpoint.address().IsValid()) {{
      recorder_response.remote_ip =
          response->remote_endpoint.address().ToString();
      recorder_response.remote_port = response->remote_endpoint.port();
    }}
    recorder_response.selected_protocol = protocol.Utf8();
    recorder_response.extensions = extensions.Utf8();
    recorder_response.headers = RecorderRealtimeMojoHeaders(response->headers);
    a11y_recorder::RecordBlinkWebSocketHandshakeResponse(
        {BLINK_WEBSOCKET_SCOPE}, identifier_,
        std::move(recorder_response));
  }}
"""
)

BLINK_WEBSOCKET_DROP_ANCHOR = """\
    probe::DidCloseWebSocket(execution_context_, identifier_);
    identifier_ = 0;
"""
BLINK_WEBSOCKET_DROP_HOOK = f"""\
    probe::DidCloseWebSocket(execution_context_, identifier_);
    if (a11y_recorder::IsRecorderActive()) {{
      a11y_recorder::RecordBlinkWebSocketClosed(
          {BLINK_WEBSOCKET_SCOPE}, identifier_,
          "dropped", was_clean, code, reason.Utf8());
    }}
    identifier_ = 0;
"""

BLINK_WEBSOCKET_RECEIVE_ANCHOR = """\
  probe::DidReceiveWebSocketMessage(execution_context_, identifier_, opcode,
                                    false, chunks);
"""
BLINK_WEBSOCKET_RECEIVE_HOOK = BLINK_WEBSOCKET_RECEIVE_ANCHOR + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebSocketMessage(
        {BLINK_WEBSOCKET_SCOPE}, identifier_,
        /*sent=*/false, receiving_message_type_is_text_ ? "text" : "binary",
        base::checked_cast<int64_t>(message_size),
        receiving_message_type_is_text_ ? RecorderRealtimeChunksText(chunks)
                                        : std::string(),
        /*origin=*/{{}});
  }}
"""

# In source order.
BLINK_WEBSOCKET_HOOKS = (
    (BLINK_WEBSOCKET_CREATED_ANCHOR, BLINK_WEBSOCKET_CREATED_HOOK),
    (BLINK_WEBSOCKET_SEND_TEXT_ANCHOR, BLINK_WEBSOCKET_SEND_TEXT_HOOK),
    (BLINK_WEBSOCKET_SEND_BLOB_ANCHOR, BLINK_WEBSOCKET_SEND_BLOB_HOOK),
    (BLINK_WEBSOCKET_SEND_BUFFER_ANCHOR, BLINK_WEBSOCKET_SEND_BUFFER_HOOK),
    (BLINK_WEBSOCKET_CLOSE_ANCHOR, BLINK_WEBSOCKET_CLOSE_HOOK),
    (BLINK_WEBSOCKET_FAIL_ANCHOR, BLINK_WEBSOCKET_FAIL_HOOK),
    (BLINK_WEBSOCKET_DISCONNECT_ANCHOR, BLINK_WEBSOCKET_DISCONNECT_HOOK),
    (
        BLINK_WEBSOCKET_HANDSHAKE_REQUEST_ANCHOR,
        BLINK_WEBSOCKET_HANDSHAKE_REQUEST_HOOK,
    ),
    (
        BLINK_WEBSOCKET_HANDSHAKE_RESPONSE_ANCHOR,
        BLINK_WEBSOCKET_HANDSHAKE_RESPONSE_HOOK,
    ),
    (BLINK_WEBSOCKET_DROP_ANCHOR, BLINK_WEBSOCKET_DROP_HOOK),
    (BLINK_WEBSOCKET_RECEIVE_ANCHOR, BLINK_WEBSOCKET_RECEIVE_HOOK),
)

BLINK_EVENT_SOURCE_MESSAGE_ANCHOR = """\
  probe::WillDispatchEventSourceEvent(GetExecutionContext(),
                                      resource_identifier_, event_type,
                                      last_event_id, data);
"""
BLINK_EVENT_SOURCE_MESSAGE_HOOK = BLINK_EVENT_SOURCE_MESSAGE_ANCHOR + """\
  if (a11y_recorder::IsRecorderActive()) {
    a11y_recorder::RecordBlinkEventSourceMessage(
        RecorderRealtimeNetworkScope(GetExecutionContext()),
        resource_identifier_, url_.GetString().Utf8(), event_type.Utf8(),
        last_event_id.Utf8(), data.Utf8());
  }
"""

BLINK_WEB_TRANSPORT_SCOPE = "RecorderRealtimeNetworkScope(GetExecutionContext())"

BLINK_WEB_TRANSPORT_CLOSE_ANCHOR = """\
    // This session has been closed or errored.
    return;
  }

"""
BLINK_WEB_TRANSPORT_CLOSE_HOOK = BLINK_WEB_TRANSPORT_CLOSE_ANCHOR + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebTransportCloseRequested(
        {BLINK_WEB_TRANSPORT_SCOPE}, inspector_transport_id_,
        close_info != nullptr,
        close_info ? static_cast<int64_t>(close_info->closeCode()) : 0,
        close_info ? close_info->reason().Utf8() : std::string(),
        RecorderCookieCallOrigin(GetExecutionContext()));
  }}

"""

BLINK_WEB_TRANSPORT_ESTABLISHED_ANCHOR = """\
  probe::WebTransportConnectionEstablished(GetExecutionContext(),
                                           inspector_transport_id_);
"""
BLINK_WEB_TRANSPORT_ESTABLISHED_HOOK = (
    BLINK_WEB_TRANSPORT_ESTABLISHED_ANCHOR
    + f"""\
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RealtimeHandshakeResponseFacts recorder_response;
    recorder_response.url = url_.GetString().Utf8();
    if (response_headers) {{
      recorder_response.http_version =
          RecorderRealtimeHttpVersion(response_headers->GetHttpVersion());
      recorder_response.status_code = response_headers->response_code();
      recorder_response.status_text = response_headers->GetStatusText();
      size_t recorder_iter = 0;
      std::string recorder_name;
      std::string recorder_value;
      while (response_headers->EnumerateHeaderLines(
          &recorder_iter, &recorder_name, &recorder_value)) {{
        recorder_response.headers.push_back({{recorder_name, recorder_value}});
      }}
    }}
    recorder_response.selected_protocol =
        selected_application_protocol.Utf8();
    if (max_datagram_size) {{
      recorder_response.max_datagram_size = *max_datagram_size;
    }}
    a11y_recorder::RecordBlinkWebTransportEstablished(
        {BLINK_WEB_TRANSPORT_SCOPE}, inspector_transport_id_,
        std::move(recorder_response));
  }}
"""
)

BLINK_WEB_TRANSPORT_CREATED_ANCHOR = """\
  probe::WebTransportCreated(execution_context, inspector_transport_id_, url_);
"""
BLINK_WEB_TRANSPORT_CREATED_HOOK = BLINK_WEB_TRANSPORT_CREATED_ANCHOR + """\
  if (a11y_recorder::IsRecorderActive()) {
    a11y_recorder::RecordBlinkWebTransportCreated(
        RecorderRealtimeNetworkScope(execution_context),
        inspector_transport_id_, url_.GetString().Utf8(),
        RecorderCookieCallOrigin(execution_context));
  }
"""

BLINK_WEB_TRANSPORT_CLEANUP_ANCHOR = """\
  CHECK_EQ(!info, abruptly);
  cleanup_started_ = true;
"""
BLINK_WEB_TRANSPORT_CLEANUP_HOOK = f"""\
  CHECK_EQ(!info, abruptly);
  if (a11y_recorder::IsRecorderActive()) {{
    a11y_recorder::RecordBlinkWebTransportClosed(
        {BLINK_WEB_TRANSPORT_SCOPE}, inspector_transport_id_,
        abruptly, info ? static_cast<int64_t>(info->closeCode()) : 0,
        info ? info->reason().Utf8() : std::string());
  }}
  cleanup_started_ = true;
"""

# In source order.
BLINK_WEB_TRANSPORT_HOOKS = (
    (BLINK_WEB_TRANSPORT_CLOSE_ANCHOR, BLINK_WEB_TRANSPORT_CLOSE_HOOK),
    (
        BLINK_WEB_TRANSPORT_ESTABLISHED_ANCHOR,
        BLINK_WEB_TRANSPORT_ESTABLISHED_HOOK,
    ),
    (BLINK_WEB_TRANSPORT_CREATED_ANCHOR, BLINK_WEB_TRANSPORT_CREATED_HOOK),
    (BLINK_WEB_TRANSPORT_CLEANUP_ANCHOR, BLINK_WEB_TRANSPORT_CLEANUP_HOOK),
)

BLINK_WEBSOCKETS_BUILD_DEPS = (
    '  deps = [ "//services/network/public/mojom:websocket_mojom" ]\n'
)
BLINK_WEBSOCKETS_BUILD_PATCHED_DEPS = """\
  deps = [
    "//chromium/recorder_bridge",
    "//services/network/public/mojom:websocket_mojom",
  ]
"""
BLINK_EVENT_SOURCE_BUILD_ANCHOR = """\
    "event_source_parser.h",
  ]
}
"""
BLINK_WEB_TRANSPORT_BUILD_ANCHOR = """\
    "web_transport_send_stream.h",
  ]
}
"""
BLINK_MODULE_BRIDGE_DEPS = """\

  deps = [ "//chromium/recorder_bridge" ]
}
"""


def patch_blink_realtime_source(
    path: Path,
    own_include: str,
    helper_anchor: str,
    hooks: tuple[tuple[str, str], ...],
    reads_script_origin: bool = True,
) -> None:
    # An EventSource event has no script call, so that source does not take
    # the origin helper, which would otherwise be an unused function.
    text = read_source(path)
    text = add_includes_after(text, own_include, BLINK_REALTIME_INCLUDES, path)
    helpers = [(BLINK_REALTIME_HELPER, BLINK_REALTIME_HELPER_MARKER)]
    if reads_script_origin:
        helpers.insert(
            0, (BLINK_COOKIE_ORIGIN_HELPER, BLINK_COOKIE_ORIGIN_HELPER_MARKER)
        )
    for helper, marker in helpers:
        text = insert_before_once(text, helper_anchor, helper, marker, path)
    for anchor, hook in hooks:
        text = apply_cookie_hook(text, anchor, hook, path)
    write_patched(path, text)


def patch_blink_websocket_channel(path: Path) -> None:
    patch_blink_realtime_source(
        path,
        '#include "third_party/blink/renderer/modules/websockets/'
        'websocket_channel_impl.h"',
        "bool WebSocketChannelImpl::Connect(\n",
        BLINK_WEBSOCKET_HOOKS,
    )


def patch_blink_event_source(path: Path) -> None:
    patch_blink_realtime_source(
        path,
        '#include "third_party/blink/renderer/modules/eventsource/'
        'event_source.h"',
        "void EventSource::OnMessageEvent(const AtomicString& event_type,\n",
        ((BLINK_EVENT_SOURCE_MESSAGE_ANCHOR, BLINK_EVENT_SOURCE_MESSAGE_HOOK),),
        reads_script_origin=False,
    )


def patch_blink_web_transport(path: Path) -> None:
    patch_blink_realtime_source(
        path,
        '#include "third_party/blink/renderer/modules/webtransport/'
        'web_transport.h"',
        "// RecentlyForgottenStreamIdSet implementation\n",
        BLINK_WEB_TRANSPORT_HOOKS,
    )


def patch_blink_module_build(path: Path, old: str, new: str) -> None:
    text = read_source(path)
    if '"//chromium/recorder_bridge"' in text:
        return
    text = replace_once(text, old, new, path)
    write_patched(path, text)


def patch_blink_realtime_builds(modules: Path) -> None:
    patch_blink_module_build(
        modules / "websockets" / "BUILD.gn",
        BLINK_WEBSOCKETS_BUILD_DEPS,
        BLINK_WEBSOCKETS_BUILD_PATCHED_DEPS,
    )
    patch_blink_module_build(
        modules / "eventsource" / "BUILD.gn",
        BLINK_EVENT_SOURCE_BUILD_ANCHOR,
        BLINK_EVENT_SOURCE_BUILD_ANCHOR[: -len("}\n")] + BLINK_MODULE_BRIDGE_DEPS,
    )
    patch_blink_module_build(
        modules / "webtransport" / "BUILD.gn",
        BLINK_WEB_TRANSPORT_BUILD_ANCHOR,
        BLINK_WEB_TRANSPORT_BUILD_ANCHOR[: -len("}\n")]
        + BLINK_MODULE_BRIDGE_DEPS,
    )


# The network service strips cookie headers from the WebSocket handshake it
# reports to the renderer unless the renderer has raw header access, which
# only DevTools normally grants. In a recording browser it reports those
# headers with each value replaced, so the renderer, and the recorder after
# it, learn which cookies a handshake sent and set without any value leaving
# the network service. Authorization headers stay stripped.
NETWORK_WEBSOCKET_INCLUDES = """\
#if BUILDFLAG(IS_WIN)
#include "base/command_line.h"
#include "chromium/recorder_bridge/cookie_text.h"
#include "chromium/recorder_bridge/recorder_switches.h"
#endif
"""
NETWORK_WEBSOCKET_HELPER_MARKER = "bool RecorderReportsCookieNames() {"
NETWORK_WEBSOCKET_HELPER = """\
#if BUILDFLAG(IS_WIN)
// Windows A11y Recorder: the text that stands in for a withheld cookie value.
constexpr char kRecorderWithheldCookieValue[] = "[withheld]";

// Whether a recording browser started this network service, either as its own
// utility process or inside the browser process.
bool RecorderReportsCookieNames() {
  static const bool reports = [] {
    const base::CommandLine& command_line =
        *base::CommandLine::ForCurrentProcess();
    return command_line.HasSwitch(
               a11y_recorder::kRecordingNetworkServiceSwitch) ||
           command_line.HasSwitch(a11y_recorder::kBootstrapSwitch);
  }();
  return reports;
}

std::string RecorderCookieNameText(const std::string& name) {
  // A nameless cookie is written as its value alone, so it stays nameless
  // when the recorder reads the replaced text back.
  return name.empty()
             ? std::string(kRecorderWithheldCookieValue)
             : base::StrCat({name, "=", kRecorderWithheldCookieValue});
}

// A Cookie request header with every value replaced.
std::string RecorderCookieHeaderNames(std::string_view value) {
  std::vector<std::string> pairs;
  for (const std::string& name :
       a11y_recorder::cookie_text::NamesFromCookieString(value)) {
    pairs.push_back(RecorderCookieNameText(name));
  }
  return base::JoinString(pairs, "; ");
}

// A Set-Cookie line reduced to its cookie name. Its attributes are dropped.
std::string RecorderSetCookieName(std::string_view value) {
  return RecorderCookieNameText(
      a11y_recorder::cookie_text::ParseCookieWrite(value).name);
}
#endif

"""
NETWORK_WEBSOCKET_HELPER_ANCHOR = (
    "mojom::WebSocketHandshakeResponsePtr ToMojo(\n"
)
NETWORK_WEBSOCKET_RESPONSE_ANCHOR = (
    "  while (response->headers->EnumerateHeaderLines(&iter, &name, &value)) {\n"
)
NETWORK_WEBSOCKET_RESPONSE_HOOK = """\
#if BUILDFLAG(IS_WIN)
    if (!has_raw_headers_access && RecorderReportsCookieNames() &&
        base::EqualsCaseInsensitiveASCII(name, "set-cookie")) {
      const std::string names_only = RecorderSetCookieName(value);
      response_to_pass->headers.push_back(
          mojom::HttpHeader::New(name, names_only));
      base::StrAppend(&headers_text, {name, ": ", names_only, "\\r\\n"});
      continue;
    }
#endif
"""
NETWORK_WEBSOCKET_REQUEST_ANCHOR = "  while (it.GetNext()) {\n"
NETWORK_WEBSOCKET_REQUEST_HOOK = """\
#if BUILDFLAG(IS_WIN)
    if (!impl_->has_raw_headers_access_ && RecorderReportsCookieNames() &&
        base::EqualsCaseInsensitiveASCII(it.name(),
                                         net::HttpRequestHeaders::kCookie)) {
      const std::string names_only = RecorderCookieHeaderNames(it.value());
      request_to_pass->headers.push_back(
          mojom::HttpHeader::New(it.name(), names_only));
      headers_text.append(base::StringPrintf(
          "%s: %s\\r\\n", it.name().c_str(), names_only.c_str()));
      continue;
    }
#endif
"""
NETWORK_WEBSOCKET_HOOK_MARKERS = (
    (NETWORK_WEBSOCKET_RESPONSE_ANCHOR, NETWORK_WEBSOCKET_RESPONSE_HOOK,
     "RecorderSetCookieName(value)"),
    (NETWORK_WEBSOCKET_REQUEST_ANCHOR, NETWORK_WEBSOCKET_REQUEST_HOOK,
     "RecorderCookieHeaderNames(it.value())"),
)
# The network service component's deps list, which the recorder dependency is
# appended after. The list starts and ends with these lines.
NETWORK_SERVICE_BUILD_ANCHOR = """\
  deps = [
    "//base",
    "//base:build_time",
"""
NETWORK_SERVICE_BUILD_DEPS_END = """\
    "//url",
  ]
"""
NETWORK_SERVICE_BUILD_DEPS = """\

  if (is_win) {
    deps += [ "//chromium/recorder_bridge:cookie_names" ]
  }
"""


def patch_network_websocket(path: Path) -> None:
    text = read_source(path)
    if '#include "chromium/recorder_bridge/cookie_text.h"' not in text:
        # BUILDFLAG needs the build configuration header first.
        config = '#include "build/build_config.h"\n'
        text = replace_once(
            text, config, config + NETWORK_WEBSOCKET_INCLUDES, path
        )
    text = insert_before_once(
        text,
        NETWORK_WEBSOCKET_HELPER_ANCHOR,
        NETWORK_WEBSOCKET_HELPER,
        NETWORK_WEBSOCKET_HELPER_MARKER,
        path,
    )
    for anchor, hook, marker in NETWORK_WEBSOCKET_HOOK_MARKERS:
        if marker in text:
            continue
        text = replace_once(text, anchor, anchor + hook, path)
    write_patched(path, text)


def patch_network_service_build(path: Path) -> None:
    text = read_source(path)
    if '"//chromium/recorder_bridge:cookie_names"' in text:
        return
    if text.count(NETWORK_SERVICE_BUILD_ANCHOR) != 1:
        raise RuntimeError(
            f"{path}: expected exactly one integration anchor, found "
            f"{text.count(NETWORK_SERVICE_BUILD_ANCHOR)}"
        )
    start = text.index(NETWORK_SERVICE_BUILD_ANCHOR)
    end = text.find(NETWORK_SERVICE_BUILD_DEPS_END, start)
    if end < 0 or "\n  ]\n" in text[start:end]:
        raise RuntimeError(f"{path}: network service deps list end not found")
    end += len(NETWORK_SERVICE_BUILD_DEPS_END)
    text = text[:end] + NETWORK_SERVICE_BUILD_DEPS + text[end:]
    write_patched(path, text)


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
    signatures = parse_bridge_signatures(
        (bridge_source / "browser_bridge.h").read_text(encoding="utf-8")
    )
    verify_hook_templates(signatures)

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
    patch_content_renderer_accessibility(
        source
        / "content"
        / "renderer"
        / "accessibility"
        / "render_accessibility_impl.cc"
    )
    patch_content_renderer_build(
        source / "content" / "renderer" / "BUILD.gn"
    )
    patch_web_contents_navigation(
        source
        / "content"
        / "browser"
        / "web_contents"
        / "web_contents_impl.cc"
    )
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
    patch_blink_element(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "dom"
        / "element.cc"
    )
    patch_blink_character_data(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "dom"
        / "character_data.cc"
    )
    patch_blink_document(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "dom"
        / "document.cc"
    )
    patch_blink_document_cookie(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "dom"
        / "document.cc"
    )
    blink_core = source / "third_party" / "blink" / "renderer" / "core"
    patch_blink_document_focus(blink_core / "dom" / "document.cc")
    patch_blink_element_active_descendant(blink_core / "dom" / "element.cc")
    patch_blink_frame_selection(blink_core / "editing" / "frame_selection.cc")
    forms = blink_core / "html" / "forms"
    patch_blink_input_element(forms / "html_input_element.cc")
    patch_blink_text_field_input_type(forms / "text_field_input_type.cc")
    patch_blink_text_area_element(forms / "html_text_area_element.cc")
    patch_blink_local_frame_view(blink_core / "frame" / "local_frame_view.cc")
    patch_blink_web_frame_widget_header(
        blink_core / "frame" / "web_frame_widget_impl.h"
    )
    patch_blink_web_frame_widget(
        blink_core / "frame" / "web_frame_widget_impl.cc"
    )
    patch_blink_cookie_jar(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "loader"
        / "cookie_jar.cc"
    )
    cookie_store = (
        source / "third_party" / "blink" / "renderer" / "modules"
        / "cookie_store"
    )
    patch_blink_cookie_store(cookie_store / "cookie_store.cc")
    patch_blink_cookie_store_build(cookie_store / "BUILD.gn")
    renderer_host = source / "content" / "browser" / "renderer_host"
    patch_content_frame_cookie_access(
        renderer_host / "render_frame_host_impl.cc"
    )
    patch_content_navigation_cookie_access(
        renderer_host / "navigation_request.cc"
    )
    patch_blink_mutation_observer_header(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "dom"
        / "mutation_observer.h"
    )
    patch_blink_mutation_observer(
        source
        / "third_party"
        / "blink"
        / "renderer"
        / "core"
        / "dom"
        / "mutation_observer.cc"
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
    blink_core_loader = (
        source / "third_party" / "blink" / "renderer" / "core" / "loader"
    )
    blink_platform_fetch = (
        source / "third_party" / "blink" / "renderer" / "platform" / "loader"
        / "fetch"
    )
    patch_blink_frame_network_observer(
        blink_core_loader / "resource_load_observer_for_frame.cc"
    )
    patch_blink_worker_network_observer(
        blink_core_loader / "resource_load_observer_for_worker.cc"
    )
    patch_blink_network_observer_header(
        blink_core_loader / "resource_load_observer_for_frame.h"
    )
    patch_blink_network_observer_header(
        blink_core_loader / "resource_load_observer_for_worker.h"
    )
    patch_blink_resource_load_observer(
        blink_platform_fetch / "resource_load_observer.h"
    )
    patch_blink_resource_fetcher_memory_cache(
        blink_platform_fetch / "resource_fetcher.cc"
    )
    patch_blink_fetch_context_request_ids(
        blink_core_loader / "frame_fetch_context.cc",
        '#include "third_party/blink/renderer/core/loader/'
        'frame_fetch_context.h"',
        BLINK_FRAME_REQUEST_ID_ANCHOR,
        BLINK_FRAME_REQUEST_ID_HOOK,
    )
    patch_blink_fetch_context_request_ids(
        blink_core_loader / "worker_fetch_context.cc",
        '#include "third_party/blink/renderer/core/loader/'
        'worker_fetch_context.h"',
        BLINK_WORKER_REQUEST_ID_ANCHOR,
        BLINK_WORKER_REQUEST_ID_HOOK,
    )
    patch_content_navigation_request_id(
        source / "content" / "browser" / "loader"
        / "navigation_url_loader_impl.cc"
    )
    patch_content_network_headers(
        source / "content" / "browser" / "devtools"
        / "network_service_devtools_observer.cc"
    )
    patch_web_contents_navigation_response(
        source / "content" / "browser" / "web_contents"
        / "web_contents_impl.cc"
    )
    modules = source / "third_party" / "blink" / "renderer" / "modules"
    patch_blink_websocket_channel(
        modules / "websockets" / "websocket_channel_impl.cc"
    )
    patch_blink_event_source(modules / "eventsource" / "event_source.cc")
    patch_blink_web_transport(modules / "webtransport" / "web_transport.cc")
    patch_blink_realtime_builds(modules)
    network = source / "services" / "network"
    patch_network_websocket(network / "websocket.cc")
    patch_network_service_build(network / "BUILD.gn")
    verify_integrated_sources(signatures)
    print(f"Recorder bridge installed in {source}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError) as error:
        print(f"Integration failed: {error}", file=sys.stderr)
        raise SystemExit(1)
