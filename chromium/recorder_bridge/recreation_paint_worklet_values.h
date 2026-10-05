#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_PAINT_WORKLET_VALUES_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_PAINT_WORKLET_VALUES_H_

#include <array>
#include <cmath>
#include <cstddef>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

// The native paint worklet values a recreation imposes on an element
// ("Sub-step 2c design: paint worklet colors and clip paths imposed" in
// docs/architecture/page-recreation.md), read from the element's
// data-a11y-recorded-paint-worklet attribute, which the recorder writes. The
// text is a list of entries separated by "; ", each a key and its words
// separated by single spaces:
//
// - "background-color", then the four floats of the color painted, red,
//   green, blue, and alpha;
// - "clip-path", then the recorded origin of the element's border box in its
//   transform space, x and y, then the path's fill type ("winding",
//   "even-odd", "inverse-winding", or "inverse-even-odd"), then each verb
//   ("move", "line", "quad", "conic", "cubic", or "close") followed by its
//   points' coordinates, one, one, two, two, three, or no points, and for a
//   conic its weight.
//
// Numbers are the recording's own text, read by the number parser given, so
// that this uses the C++ standard library alone and can be exercised outside
// a Chromium build; the bridge gives base::StringToDouble. Text that does not
// follow this form, a repeated key, or a number that is not finite gives no
// values at all.
namespace a11y_recorder {

enum class RecreationPathVerb { kMove, kLine, kQuad, kConic, kCubic, kClose };

struct RecreationPathSegment {
  RecreationPathVerb verb = RecreationPathVerb::kMove;
  // The points' coordinates, x and y of each.
  std::vector<double> points;
  // A conic's weight.
  double weight = 0;

  bool operator==(const RecreationPathSegment&) const = default;
};

struct RecreationClipPath {
  double origin_x = 0;
  double origin_y = 0;
  std::string fill_type;
  std::vector<RecreationPathSegment> segments;
};

struct RecreationPaintWorkletValues {
  // The background color as recorded, the text of its four numbers and the
  // numbers.
  std::optional<std::array<std::string, 4>> background_color_text;
  std::optional<std::array<double, 4>> background_color;
  std::optional<RecreationClipPath> clip_path;

  bool empty() const { return !background_color && !clip_path; }
};

namespace recreation_paint_worklet_values_internal {

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

// The points a verb takes and whether it takes a weight, or none for a word
// that is not a verb.
inline std::optional<std::pair<RecreationPathVerb, std::size_t>> Verb(
    std::string_view word) {
  if (word == "move") {
    return std::pair(RecreationPathVerb::kMove, std::size_t{1});
  }
  if (word == "line") {
    return std::pair(RecreationPathVerb::kLine, std::size_t{1});
  }
  if (word == "quad") {
    return std::pair(RecreationPathVerb::kQuad, std::size_t{2});
  }
  if (word == "conic") {
    return std::pair(RecreationPathVerb::kConic, std::size_t{2});
  }
  if (word == "cubic") {
    return std::pair(RecreationPathVerb::kCubic, std::size_t{3});
  }
  if (word == "close") {
    return std::pair(RecreationPathVerb::kClose, std::size_t{0});
  }
  return std::nullopt;
}

inline bool IsFillType(std::string_view word) {
  return word == "winding" || word == "even-odd" ||
         word == "inverse-winding" || word == "inverse-even-odd";
}

template <typename ParseNumber>
std::optional<RecreationClipPath> ClipPath(
    const std::vector<std::string_view>& words,
    ParseNumber& parse) {
  // The origin, the fill type, and at least one verb.
  if (words.size() < 4 || !IsFillType(words[2])) {
    return std::nullopt;
  }
  RecreationClipPath path;
  const std::optional<double> x = Number(words[0], parse);
  const std::optional<double> y = Number(words[1], parse);
  if (!x || !y) {
    return std::nullopt;
  }
  path.origin_x = *x;
  path.origin_y = *y;
  path.fill_type = std::string(words[2]);
  std::size_t index = 3;
  while (index < words.size()) {
    const auto verb = Verb(words[index++]);
    if (!verb) {
      return std::nullopt;
    }
    RecreationPathSegment segment;
    segment.verb = verb->first;
    const std::size_t numbers =
        2 * verb->second + (verb->first == RecreationPathVerb::kConic ? 1 : 0);
    if (words.size() - index < numbers) {
      return std::nullopt;
    }
    for (std::size_t count = 0; count < numbers; ++count) {
      const std::optional<double> number = Number(words[index++], parse);
      if (!number) {
        return std::nullopt;
      }
      if (verb->first == RecreationPathVerb::kConic && count == numbers - 1) {
        segment.weight = *number;
      } else {
        segment.points.push_back(*number);
      }
    }
    path.segments.push_back(std::move(segment));
  }
  return path;
}

}  // namespace recreation_paint_worklet_values_internal

// The values of the attribute's text, or none when the text is empty or
// does not follow the form above. ParseNumber is called as
// parse(std::string_view, double*) and returns whether the whole text was a
// number.
template <typename ParseNumber>
RecreationPaintWorkletValues ParseRecreationPaintWorkletValues(
    std::string_view text,
    ParseNumber parse) {
  namespace internal = recreation_paint_worklet_values_internal;
  RecreationPaintWorkletValues values;
  if (text.empty()) {
    return values;
  }
  for (const std::string_view entry : internal::Split(text, "; ")) {
    const std::vector<std::string_view> words = internal::Split(entry, " ");
    const std::vector<std::string_view> rest(words.begin() + 1, words.end());
    if (words[0] == "background-color") {
      if (values.background_color || rest.size() != 4) {
        return {};
      }
      std::array<double, 4> color{};
      std::array<std::string, 4> color_text;
      for (std::size_t index = 0; index < 4; ++index) {
        const std::optional<double> number =
            internal::Number(rest[index], parse);
        if (!number) {
          return {};
        }
        color[index] = *number;
        color_text[index] = std::string(rest[index]);
      }
      values.background_color = color;
      values.background_color_text = std::move(color_text);
    } else if (words[0] == "clip-path") {
      if (values.clip_path) {
        return {};
      }
      values.clip_path = internal::ClipPath(rest, parse);
      if (!values.clip_path) {
        return {};
      }
    } else {
      return {};
    }
  }
  return values;
}

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_PAINT_WORKLET_VALUES_H_
