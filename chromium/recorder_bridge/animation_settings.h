#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_ANIMATION_SETTINGS_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_ANIMATION_SETTINGS_H_

#include <array>
#include <optional>
#include <string_view>

// The Windows animation settings recorded with a popup window as it is shown
// (protocol 0.47). Which of them governs a desktop compositor transition of
// the window is not documented, so all are recorded and none is
// interpreted. It uses the C++ standard library alone, so it can be
// exercised outside a Chromium build; the bridge supplies the reads.
namespace a11y_recorder {

enum class WindowsAnimationSetting {
  kClientAreaAnimation,  // SPI_GETCLIENTAREAANIMATION
  kUiEffects,            // SPI_GETUIEFFECTS
  kMenuAnimation,        // SPI_GETMENUANIMATION
  kMenuFade,             // SPI_GETMENUFADE
  kComboBoxAnimation,    // SPI_GETCOMBOBOXANIMATION
};

struct WindowsAnimationSettingField {
  WindowsAnimationSetting setting;
  std::string_view name;
};

// The settings in the order and with the names of the record's fields.
inline constexpr std::array<WindowsAnimationSettingField, 5>
    kWindowsAnimationSettingFields = {{
        {WindowsAnimationSetting::kClientAreaAnimation, "clientAreaAnimation"},
        {WindowsAnimationSetting::kUiEffects, "uiEffects"},
        {WindowsAnimationSetting::kMenuAnimation, "menuAnimation"},
        {WindowsAnimationSetting::kMenuFade, "menuFade"},
        {WindowsAnimationSetting::kComboBoxAnimation, "comboBoxAnimation"},
    }};

// Reads each setting with `read`, which returns the setting, or nothing when
// the call fails, and passes each field's name and value to `set`; a failed
// read is passed as nothing and recorded as null.
template <typename Read, typename Set>
void ReadWindowsAnimationSettings(Read read, Set set) {
  for (const auto& field : kWindowsAnimationSettingFields) {
    const std::optional<bool> value = read(field.setting);
    set(field.name, value);
  }
}

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_ANIMATION_SETTINGS_H_
