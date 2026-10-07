#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_IMAGE_FRAMES_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_IMAGE_FRAMES_H_

#include <cstddef>
#include <cstdint>
#include <limits>
#include <optional>
#include <string_view>
#include <unordered_map>

// The frames a recreation holds its animated images at ("Sub-step 2a
// design: animated images held" in docs/architecture/page-recreation.md),
// by paint image ID. Blink's main thread holds an image at the frame the
// recorder's response names, and the compositor thread reads it, so each
// call takes the lock. The lock and its guard are parameters, base::Lock and
// base::AutoLock in the bridge, so that this uses the C++ standard library
// alone and can be exercised outside a Chromium build.
namespace a11y_recorder {

// The frame index of the recorder's X-A11y-Recorder-Image-Frame header:
// decimal digits alone, as the recorder writes it, or none.
inline std::optional<std::size_t> ParseRecreationImageFrame(
    std::string_view text) {
  if (text.empty()) {
    return std::nullopt;
  }
  std::size_t index = 0;
  for (const char digit : text) {
    if (digit < '0' || digit > '9') {
      return std::nullopt;
    }
    const std::size_t value = static_cast<std::size_t>(digit - '0');
    if (index > (std::numeric_limits<std::size_t>::max() - value) / 10) {
      return std::nullopt;
    }
    index = index * 10 + value;
  }
  return index;
}

template <typename Lock, typename Guard>
class HeldImageFrames {
 public:
  // Holds the paint image at the frame index. A paint image ID is a
  // sequence number from 0 (PaintImage::GetNextId); a negative ID is not an
  // image's, and is not held.
  void Hold(int64_t paint_image_id, std::size_t frame_index) {
    if (paint_image_id < 0) {
      return;
    }
    Guard guard(lock_);
    frames_[paint_image_id] = frame_index;
  }

  // The frame index the paint image is held at, or none.
  std::optional<std::size_t> Find(int64_t paint_image_id) const {
    Guard guard(lock_);
    const auto found = frames_.find(paint_image_id);
    if (found == frames_.end()) {
      return std::nullopt;
    }
    return found->second;
  }

 private:
  mutable Lock lock_;
  std::unordered_map<int64_t, std::size_t> frames_;
};

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECREATION_IMAGE_FRAMES_H_
