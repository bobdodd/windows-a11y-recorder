// Checks the Windows animation settings value without a Chromium build. The
// test suite in chromium/test_integrate.py compiles and runs this file when a
// C++ compiler is available, since it depends on the standard library alone.

#include "chromium/recorder_bridge/animation_settings.h"

#include <cstdio>
#include <map>
#include <optional>
#include <string>

namespace {

int failures = 0;

void Expect(bool condition, const char* description) {
  if (!condition) {
    std::fprintf(stderr, "FAILED: %s\n", description);
    ++failures;
  }
}

using a11y_recorder::WindowsAnimationSetting;

std::map<std::string, std::optional<bool>> Read(
    std::optional<bool> client_area,
    std::optional<bool> ui_effects,
    std::optional<bool> menu_animation,
    std::optional<bool> menu_fade,
    std::optional<bool> combo_box) {
  std::map<std::string, std::optional<bool>> fields;
  a11y_recorder::ReadWindowsAnimationSettings(
      [&](WindowsAnimationSetting setting) -> std::optional<bool> {
        switch (setting) {
          case WindowsAnimationSetting::kClientAreaAnimation:
            return client_area;
          case WindowsAnimationSetting::kUiEffects:
            return ui_effects;
          case WindowsAnimationSetting::kMenuAnimation:
            return menu_animation;
          case WindowsAnimationSetting::kMenuFade:
            return menu_fade;
          case WindowsAnimationSetting::kComboBoxAnimation:
            return combo_box;
        }
        return std::nullopt;
      },
      [&](std::string_view name, std::optional<bool> value) {
        Expect(fields.emplace(std::string(name), value).second,
               "each field is set once");
      });
  return fields;
}

void TestEachSettingIsItsOwnField() {
  auto fields = Read(true, false, true, false, true);
  Expect(fields.size() == 5, "five fields are set");
  Expect(fields["clientAreaAnimation"] == true, "client area animation");
  Expect(fields["uiEffects"] == false, "UI effects");
  Expect(fields["menuAnimation"] == true, "menu animation");
  Expect(fields["menuFade"] == false, "menu fade");
  Expect(fields["comboBoxAnimation"] == true, "combo box animation");
}

void TestAFailedReadIsNull() {
  auto fields = Read(std::nullopt, true, std::nullopt, true, true);
  Expect(fields.size() == 5, "a failed read still sets its field");
  Expect(!fields["clientAreaAnimation"].has_value(),
         "a failed read of client area animation is null");
  Expect(!fields["menuAnimation"].has_value(),
         "a failed read of menu animation is null");
  Expect(fields["uiEffects"] == true, "the other reads are kept");
}

}  // namespace

int main() {
  TestEachSettingIsItsOwnField();
  TestAFailedReadIsNull();
  if (failures == 0) {
    std::printf("animation settings tests passed\n");
  }
  return failures == 0 ? 0 : 1;
}
