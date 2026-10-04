#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_COMPOSITOR_VALUES_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_COMPOSITOR_VALUES_H_

#include <array>
#include <cmath>
#include <cstddef>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

// The compositor values a recreation imposes on an element ("Sub-step 2b-i
// design: compositor values imposed" in docs/architecture/page-recreation.md),
// read from the element's data-a11y-recorded-compositor attribute, which the
// recorder writes. The text is a list of entries separated by "; ", each a
// key and its numbers separated by single spaces:
//
// - "translate-transform", "rotate-transform", "scale-transform", or
//   "primary-transform", then the 16 entries of the transform node's local
//   matrix, row by row;
// - "opacity", then one number;
// - "filter" or "backdrop-filter", then its operations separated by ", ",
//   each a type name and its numbers, as the compositor-frame record holds
//   them. A key with no operations is an empty list.
//
// Numbers are the recording's own text, read by the number parser given, so
// that this uses the C++ standard library alone and can be exercised outside
// a Chromium build; the bridge gives base::StringToDouble. Text that does not
// follow this form, a repeated key, or a number that is not finite gives no
// values at all.
namespace a11y_recorder {

struct RecreationFilterOperation {
  std::string type;
  std::vector<double> numbers;

  bool operator==(const RecreationFilterOperation&) const = default;
};

struct RecreationCompositorValues {
  // By namespace name, as the compositor-animation-started record names it.
  std::map<std::string, std::array<double, 16>> transforms;
  // The opacity as recorded, its text and its number.
  std::optional<std::string> opacity_text;
  std::optional<double> opacity;
  std::optional<std::vector<RecreationFilterOperation>> filter;
  std::optional<std::vector<RecreationFilterOperation>> backdrop_filter;

  bool empty() const {
    return transforms.empty() && !opacity && !filter && !backdrop_filter;
  }
};

namespace recreation_compositor_values_internal {

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

inline bool IsTransformKey(std::string_view key) {
  return key == "translate-transform" || key == "rotate-transform" ||
         key == "scale-transform" || key == "primary-transform";
}

inline bool IsTypeName(std::string_view name) {
  if (name.empty()) {
    return false;
  }
  for (const char character : name) {
    if (!((character >= 'a' && character <= 'z') || character == '-')) {
      return false;
    }
  }
  return true;
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

template <typename ParseNumber>
std::optional<std::vector<RecreationFilterOperation>> Filters(
    std::string_view text,
    ParseNumber& parse) {
  std::vector<RecreationFilterOperation> operations;
  if (text.empty()) {
    return operations;
  }
  for (const std::string_view part : Split(text, ", ")) {
    const std::vector<std::string_view> words = Split(part, " ");
    if (!IsTypeName(words[0])) {
      return std::nullopt;
    }
    RecreationFilterOperation operation;
    operation.type = std::string(words[0]);
    for (std::size_t index = 1; index < words.size(); ++index) {
      const std::optional<double> number = Number(words[index], parse);
      if (!number) {
        return std::nullopt;
      }
      operation.numbers.push_back(*number);
    }
    operations.push_back(std::move(operation));
  }
  return operations;
}

}  // namespace recreation_compositor_values_internal

// The values of the attribute's text, or none when the text is empty or
// does not follow the form above. ParseNumber is called as
// parse(std::string_view, double*) and returns whether the whole text was a
// number.
template <typename ParseNumber>
RecreationCompositorValues ParseRecreationCompositorValues(
    std::string_view text,
    ParseNumber parse) {
  namespace internal = recreation_compositor_values_internal;
  RecreationCompositorValues values;
  if (text.empty()) {
    return values;
  }
  for (const std::string_view entry : internal::Split(text, "; ")) {
    const std::size_t space = entry.find(' ');
    const std::string_view key = entry.substr(0, space);
    const std::string_view rest =
        space == std::string_view::npos ? std::string_view()
                                        : entry.substr(space + 1);
    if (space != std::string_view::npos && rest.empty()) {
      return {};
    }
    if (internal::IsTransformKey(key)) {
      const std::vector<std::string_view> words = internal::Split(rest, " ");
      if (rest.empty() || words.size() != 16 ||
          values.transforms.count(std::string(key))) {
        return {};
      }
      std::array<double, 16> matrix{};
      for (std::size_t index = 0; index < 16; ++index) {
        const std::optional<double> number =
            internal::Number(words[index], parse);
        if (!number) {
          return {};
        }
        matrix[index] = *number;
      }
      values.transforms.emplace(std::string(key), matrix);
    } else if (key == "opacity") {
      const std::optional<double> number = internal::Number(rest, parse);
      if (!number || values.opacity) {
        return {};
      }
      values.opacity = *number;
      values.opacity_text = std::string(rest);
    } else if (key == "filter" || key == "backdrop-filter") {
      auto& target = key == "filter" ? values.filter : values.backdrop_filter;
      if (target) {
        return {};
      }
      target = internal::Filters(rest, parse);
      if (!target) {
        return {};
      }
    } else {
      return {};
    }
  }
  return values;
}

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_COMPOSITOR_VALUES_H_
