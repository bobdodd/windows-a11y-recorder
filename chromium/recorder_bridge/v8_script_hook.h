#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_V8_SCRIPT_HOOK_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_V8_SCRIPT_HOOK_H_

#include "v8/include/v8-forward.h"
#include "v8/include/v8-local-handle.h"

// Protocol 0.54 (slice 4h): V8 reports each script it compiles to this hook,
// which Blink sets for the main thread. The hook's setter and getter are
// defined in V8 (v8/src/debug/debug.cc, written there by integrate.py), so
// that V8 needs no dependency on the recorder bridge and Blink needs no change
// to V8's exported interface. They link in a build that is not a component
// build, which the instrumented Chromium is (out/A11yRecorder/args.gn).
// See docs/architecture/page-recreation.md, "Slice 4h".
namespace a11y_recorder {

// V8's Script::CompilationKind, as a number (v8/src/objects/script.h).
inline constexpr int kV8CompilationKindHost = 0;
inline constexpr int kV8CompilationKindDirectEval = 1;
inline constexpr int kV8CompilationKindIndirectEval = 2;
inline constexpr int kV8CompilationKindFunctionConstructor = 3;
inline constexpr int kV8CompilationKindWrapped = 4;

// A normal V8 script as V8 holds it when it is instantiated or fails to
// compile. A text that is not a string is an empty handle. Offsets are V8's,
// zero-based. The eval-from script ID is zero when the script is not eval
// code or V8 did not keep the calling function.
struct V8ScriptFacts {
  int script_id = 0;
  v8::Local<v8::String> source;
  v8::Local<v8::String> name;
  v8::Local<v8::String> source_url;
  v8::Local<v8::String> source_mapping_url;
  int line_offset = 0;
  int column_offset = 0;
  bool is_module = false;
  int compilation_kind = kV8CompilationKindHost;
  int eval_from_script_id = 0;
  bool compile_error = false;
};

using V8ScriptHook = void (*)(v8::Isolate* isolate, const V8ScriptFacts& facts);

// Defined in V8. The hook is process-wide and is called on the thread that
// compiled the script, so it must check which isolate and thread it is on.
void SetV8ScriptHook(V8ScriptHook hook);
V8ScriptHook GetV8ScriptHook();

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_V8_SCRIPT_HOOK_H_
