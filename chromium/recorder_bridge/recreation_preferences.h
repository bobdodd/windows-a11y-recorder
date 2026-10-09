#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_PREFERENCES_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_PREFERENCES_H_

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

// The recorded preferences a recreation gives its page ("Stage 3: the
// recreation" in docs/architecture/accessibility-preferences.md), read from
// the data-a11y-recorded-preferences attribute of the recreated document's
// root element, which the recorder writes. The text is a list of entries
// separated by "; ", each a key and its words separated by single spaces:
//
// - "field", a page field's name as the web-preferences-sent record names
//   it, a type letter, and the value: "b" and "true" or "false", "i" and a
//   whole number, "n" and a number, or "t" and the text, percent-encoded
//   (every byte other than a letter, a digit, "-", ".", "_", and "~" written
//   as "%" and two hexadecimal digits), so that it holds no space or ";";
// - "zoom", then the zoom level, as Chromium's zoom levels are written;
// - "color", a map name ("light", "dark", or "forcedColors"), a color's
//   RendererColorId name, such as "kColorCssSystemWindow", and the color as
//   eight hexadecimal digits, alpha, red, green, and blue, as SkColor holds
//   them.
//
// Numbers are read by the number parser given, so that this uses the C++
// standard library alone and can be exercised outside a Chromium build; the
// bridge gives base::StringToDouble. Text that does not follow this form, a
// repeated field, zoom, or color, or a number that is not finite gives no
// values at all.

// The 67 RendererColorId values of ui/color/color_id.mojom, by name, in the
// enumeration's order. X is called once with each name.
#define A11Y_RECORDER_RENDERER_COLOR_IDS(X)              \
  X(kColorCssSystemActiveText)                           \
  X(kColorCssSystemBtnFace)                              \
  X(kColorCssSystemBtnText)                              \
  X(kColorCssSystemField)                                \
  X(kColorCssSystemFieldText)                            \
  X(kColorCssSystemGrayText)                             \
  X(kColorCssSystemHighlight)                            \
  X(kColorCssSystemHighlightText)                        \
  X(kColorCssSystemHotlight)                             \
  X(kColorCssSystemLinkText)                             \
  X(kColorCssSystemMenuHilight)                          \
  X(kColorCssSystemScrollbar)                            \
  X(kColorCssSystemVisitedText)                          \
  X(kColorCssSystemWindow)                               \
  X(kColorCssSystemWindowText)                           \
  X(kColorMenuBackground)                                \
  X(kColorMenuItemBackgroundSelected)                    \
  X(kColorMenuSeparator)                                 \
  X(kColorOverlayScrollbarFill)                          \
  X(kColorOverlayScrollbarFillHovered)                   \
  X(kColorOverlayScrollbarStroke)                        \
  X(kColorOverlayScrollbarStrokeHovered)                 \
  X(kColorWebNativeControlAccent)                        \
  X(kColorWebNativeControlAccentDisabled)                \
  X(kColorWebNativeControlAccentHovered)                 \
  X(kColorWebNativeControlAccentPressed)                 \
  X(kColorWebNativeControlAutoCompleteBackground)        \
  X(kColorWebNativeControlBorder)                        \
  X(kColorWebNativeControlBorderDisabled)                \
  X(kColorWebNativeControlBorderHovered)                 \
  X(kColorWebNativeControlBorderPressed)                 \
  X(kColorWebNativeControlButtonBorder)                  \
  X(kColorWebNativeControlButtonBorderDisabled)          \
  X(kColorWebNativeControlButtonBorderHovered)           \
  X(kColorWebNativeControlButtonBorderPressed)           \
  X(kColorWebNativeControlButtonFill)                    \
  X(kColorWebNativeControlButtonFillDisabled)            \
  X(kColorWebNativeControlButtonFillHovered)             \
  X(kColorWebNativeControlButtonFillPressed)             \
  X(kColorWebNativeControlCheckboxBackground)            \
  X(kColorWebNativeControlCheckboxBackgroundDisabled)    \
  X(kColorWebNativeControlFill)                          \
  X(kColorWebNativeControlFillDisabled)                  \
  X(kColorWebNativeControlFillHovered)                   \
  X(kColorWebNativeControlFillPressed)                   \
  X(kColorWebNativeControlLightenLayer)                  \
  X(kColorWebNativeControlProgressValue)                 \
  X(kColorWebNativeControlScrollbarArrowBackgroundDisabled) \
  X(kColorWebNativeControlScrollbarArrowBackgroundHovered)  \
  X(kColorWebNativeControlScrollbarArrowBackgroundPressed)  \
  X(kColorWebNativeControlScrollbarArrowForeground)      \
  X(kColorWebNativeControlScrollbarArrowForegroundDisabled) \
  X(kColorWebNativeControlScrollbarArrowForegroundHovered)  \
  X(kColorWebNativeControlScrollbarArrowForegroundPressed)  \
  X(kColorWebNativeControlScrollbarCorner)               \
  X(kColorWebNativeControlScrollbarThumb)                \
  X(kColorWebNativeControlScrollbarThumbHovered)         \
  X(kColorWebNativeControlScrollbarThumbOverlayMinimalMode) \
  X(kColorWebNativeControlScrollbarThumbPressed)         \
  X(kColorWebNativeControlScrollbarTrack)                \
  X(kColorWebNativeControlSlider)                        \
  X(kColorWebNativeControlSliderBorder)                  \
  X(kColorWebNativeControlSliderBorderHovered)           \
  X(kColorWebNativeControlSliderBorderPressed)           \
  X(kColorWebNativeControlSliderDisabled)                \
  X(kColorWebNativeControlSliderHovered)                 \
  X(kColorWebNativeControlSliderPressed)

namespace a11y_recorder {

// The names of the color maps, as the color-maps-sent record and the
// attribute name them.
inline constexpr char kLightColorMap[] = "light";
inline constexpr char kDarkColorMap[] = "dark";
inline constexpr char kForcedColorsColorMap[] = "forcedColors";

// The name of a RendererColorId value, or nullptr for one not listed above.
// Id is color::mojom::RendererColorId; it is a template parameter so that
// this header needs no Chromium header.
template <typename Id>
const char* RendererColorName(Id id) {
#define A11Y_RECORDER_COLOR_NAME_CASE(name) \
  if (id == Id::name) {                     \
    return #name;                           \
  }
  A11Y_RECORDER_RENDERER_COLOR_IDS(A11Y_RECORDER_COLOR_NAME_CASE)
#undef A11Y_RECORDER_COLOR_NAME_CASE
  return nullptr;
}

// The RendererColorId value of a name listed above, or none.
template <typename Id>
std::optional<Id> RendererColorIdNamed(std::string_view name) {
#define A11Y_RECORDER_COLOR_ID_CASE(id_name) \
  if (name == #id_name) {                    \
    return Id::id_name;                      \
  }
  A11Y_RECORDER_RENDERER_COLOR_IDS(A11Y_RECORDER_COLOR_ID_CASE)
#undef A11Y_RECORDER_COLOR_ID_CASE
  return std::nullopt;
}

// A recorded page field's value.
struct RecreationPreferenceValue {
  enum class Kind { kBoolean, kInteger, kNumber, kText };

  Kind kind = Kind::kBoolean;
  bool boolean = false;
  int64_t integer = 0;
  double number = 0;
  std::string text;

  bool operator==(const RecreationPreferenceValue&) const = default;
};

struct RecreationPreferences {
  // By the field's name in the web-preferences-sent record.
  std::map<std::string, RecreationPreferenceValue> fields;
  std::optional<double> zoom_level;
  // By map name, then by color name, each color as SkColor holds it.
  std::map<std::string, std::map<std::string, uint32_t>> color_maps;

  bool empty() const {
    return fields.empty() && !zoom_level && color_maps.empty();
  }

  const RecreationPreferenceValue* Find(std::string_view name) const {
    const auto found = fields.find(std::string(name));
    return found == fields.end() ? nullptr : &found->second;
  }

  std::optional<bool> Boolean(std::string_view name) const {
    const RecreationPreferenceValue* value = Find(name);
    if (!value || value->kind != RecreationPreferenceValue::Kind::kBoolean) {
      return std::nullopt;
    }
    return value->boolean;
  }

  std::optional<int64_t> Integer(std::string_view name) const {
    const RecreationPreferenceValue* value = Find(name);
    if (!value || value->kind != RecreationPreferenceValue::Kind::kInteger) {
      return std::nullopt;
    }
    return value->integer;
  }

  std::optional<double> Number(std::string_view name) const {
    const RecreationPreferenceValue* value = Find(name);
    if (!value || value->kind != RecreationPreferenceValue::Kind::kNumber) {
      return std::nullopt;
    }
    return value->number;
  }

  std::optional<std::string> Text(std::string_view name) const {
    const RecreationPreferenceValue* value = Find(name);
    if (!value || value->kind != RecreationPreferenceValue::Kind::kText) {
      return std::nullopt;
    }
    return value->text;
  }
};

namespace recreation_preferences_internal {

inline std::vector<std::string_view> Split(std::string_view text,
                                           std::string_view separator) {
  std::vector<std::string_view> parts;
  std::size_t start = 0;
  while (true) {
    const std::size_t end = text.find(separator, start);
    if (end == std::string_view::npos) {
      parts.push_back(text.substr(start));
      return parts;
    }
    parts.push_back(text.substr(start, end - start));
    start = end + separator.size();
  }
}

inline bool IsLetter(char character) {
  return (character >= 'a' && character <= 'z') ||
         (character >= 'A' && character <= 'Z');
}

inline bool IsName(std::string_view name) {
  if (name.empty()) {
    return false;
  }
  for (const char character : name) {
    if (!IsLetter(character)) {
      return false;
    }
  }
  return true;
}

inline int HexDigit(char character) {
  if (character >= '0' && character <= '9') {
    return character - '0';
  }
  if (character >= 'a' && character <= 'f') {
    return character - 'a' + 10;
  }
  if (character >= 'A' && character <= 'F') {
    return character - 'A' + 10;
  }
  return -1;
}

inline bool IsUnreserved(char character) {
  return IsLetter(character) || (character >= '0' && character <= '9') ||
         character == '-' || character == '.' || character == '_' ||
         character == '~';
}

inline std::optional<std::string> Decode(std::string_view text) {
  std::string decoded;
  for (std::size_t index = 0; index < text.size(); ++index) {
    const char character = text[index];
    if (character == '%') {
      if (index + 2 >= text.size()) {
        return std::nullopt;
      }
      const int high = HexDigit(text[index + 1]);
      const int low = HexDigit(text[index + 2]);
      if (high < 0 || low < 0) {
        return std::nullopt;
      }
      decoded.push_back(static_cast<char>(high * 16 + low));
      index += 2;
    } else if (IsUnreserved(character)) {
      decoded.push_back(character);
    } else {
      return std::nullopt;
    }
  }
  return decoded;
}

inline std::optional<uint32_t> Color(std::string_view text) {
  if (text.size() != 8) {
    return std::nullopt;
  }
  uint32_t color = 0;
  for (const char character : text) {
    const int digit = HexDigit(character);
    if (digit < 0) {
      return std::nullopt;
    }
    color = color * 16 + static_cast<uint32_t>(digit);
  }
  return color;
}

template <typename ParseNumber>
std::optional<double> Number(std::string_view text, ParseNumber& parse) {
  if (text.empty()) {
    return std::nullopt;
  }
  double value = 0;
  if (!parse(text, &value) || !std::isfinite(value)) {
    return std::nullopt;
  }
  return value;
}

inline bool IsMapName(std::string_view name) {
  return name == kLightColorMap || name == kDarkColorMap ||
         name == kForcedColorsColorMap;
}

}  // namespace recreation_preferences_internal

// Percent-encodes a text value as the attribute holds it.
inline std::string EncodeRecreationPreferenceText(std::string_view text) {
  // Each digit is worked out rather than looked up in a table, as
  // Chromium's -Wunsafe-buffer-usage refuses indexing a C array (build of
  // 2026-10-09).
  const auto digit = [](unsigned value) {
    return static_cast<char>(value < 10 ? '0' + value : 'A' + (value - 10));
  };
  std::string encoded;
  for (const char character : text) {
    if (recreation_preferences_internal::IsUnreserved(character)) {
      encoded.push_back(character);
    } else {
      const auto byte = static_cast<unsigned char>(character);
      encoded.push_back('%');
      encoded.push_back(digit(byte / 16u));
      encoded.push_back(digit(byte % 16u));
    }
  }
  return encoded;
}

// The values of the attribute's text, or none when the text is empty or
// does not follow the form above. ParseNumber is called as
// parse(std::string_view, double*) and returns whether the whole text was a
// number.
template <typename ParseNumber>
RecreationPreferences ParseRecreationPreferences(std::string_view text,
                                                 ParseNumber parse) {
  namespace internal = recreation_preferences_internal;
  RecreationPreferences values;
  if (text.empty()) {
    return values;
  }
  for (const std::string_view entry : internal::Split(text, "; ")) {
    const std::vector<std::string_view> words = internal::Split(entry, " ");
    const std::string_view key = words[0];
    if (key == "field") {
      if (words.size() != 4 || !internal::IsName(words[1]) ||
          values.fields.count(std::string(words[1]))) {
        return {};
      }
      RecreationPreferenceValue value;
      const std::string_view type = words[2];
      const std::string_view word = words[3];
      if (type == "b") {
        if (word != "true" && word != "false") {
          return {};
        }
        value.kind = RecreationPreferenceValue::Kind::kBoolean;
        value.boolean = word == "true";
      } else if (type == "i") {
        const std::optional<double> number = internal::Number(word, parse);
        if (!number || std::floor(*number) != *number ||
            std::fabs(*number) > 9007199254740992.0) {
          return {};
        }
        value.kind = RecreationPreferenceValue::Kind::kInteger;
        value.integer = static_cast<int64_t>(*number);
      } else if (type == "n") {
        const std::optional<double> number = internal::Number(word, parse);
        if (!number) {
          return {};
        }
        value.kind = RecreationPreferenceValue::Kind::kNumber;
        value.number = *number;
      } else if (type == "t") {
        std::optional<std::string> decoded = internal::Decode(word);
        if (!decoded) {
          return {};
        }
        value.kind = RecreationPreferenceValue::Kind::kText;
        value.text = std::move(*decoded);
      } else {
        return {};
      }
      values.fields.emplace(std::string(words[1]), std::move(value));
    } else if (key == "zoom") {
      if (words.size() != 2 || values.zoom_level) {
        return {};
      }
      const std::optional<double> number = internal::Number(words[1], parse);
      if (!number) {
        return {};
      }
      values.zoom_level = *number;
    } else if (key == "color") {
      if (words.size() != 4 || !internal::IsMapName(words[1]) ||
          !internal::IsName(words[2])) {
        return {};
      }
      const std::optional<uint32_t> color = internal::Color(words[3]);
      auto& map = values.color_maps[std::string(words[1])];
      if (!color || map.count(std::string(words[2]))) {
        return {};
      }
      map.emplace(std::string(words[2]), *color);
    } else {
      return {};
    }
  }
  return values;
}

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_PREFERENCES_H_
