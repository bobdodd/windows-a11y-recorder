#!/usr/bin/env python3
"""Installs the recorder bridge and startup hook into a Chromium checkout."""

from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path


BRIDGE_DEP = '"//chromium/recorder_bridge",'
BRIDGE_INCLUDE = '#include "chromium/recorder_bridge/browser_bridge.h"'
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
LEGACY_CHILD_LAUNCHER_HOOK = """\
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
CHILD_LAUNCHER_HOOK = f"""\
#if BUILDFLAG(IS_WIN)
{LEGACY_CHILD_LAUNCHER_HOOK}#endif
"""


def replace_once(text: str, old: str, new: str, path: Path) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(
            f"{path}: expected exactly one integration anchor, found {count}"
        )
    return text.replace(old, new, 1)


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
    text = text.replace(LEGACY_CHILD_LAUNCHER_HOOK, "")
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_child_launcher(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
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
    print(f"Recorder bridge installed in {source}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError) as error:
        print(f"Integration failed: {error}", file=sys.stderr)
        raise SystemExit(1)
