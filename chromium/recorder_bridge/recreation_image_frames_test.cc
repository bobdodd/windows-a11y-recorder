// Checks the frames a recreation holds its animated images at, without a
// Chromium build. The test suite in chromium/test_integrate.py compiles and
// runs this file when a C++ compiler is available.

#include "chromium/recorder_bridge/recreation_image_frames.h"

#include <cstdio>
#include <cstdlib>
#include <mutex>
#include <thread>

namespace {

using Frames =
    a11y_recorder::HeldImageFrames<std::mutex, std::lock_guard<std::mutex>>;

void Check(bool condition, const char* what) {
  if (!condition) {
    std::fprintf(stderr, "failed: %s\n", what);
    std::exit(1);
  }
}

}  // namespace

int main() {
  {
    using a11y_recorder::ParseRecreationImageFrame;
    Check(ParseRecreationImageFrame("0") == 0u, "frame 0 is read");
    Check(ParseRecreationImageFrame("7") == 7u, "frame 7 is read");
    Check(ParseRecreationImageFrame("120") == 120u, "several digits are read");
    Check(!ParseRecreationImageFrame("").has_value(), "no header is no frame");
    Check(!ParseRecreationImageFrame("-1").has_value(), "a sign is refused");
    Check(!ParseRecreationImageFrame(" 3").has_value(), "a space is refused");
    Check(!ParseRecreationImageFrame("3.0").has_value(), "a point is refused");
    Check(!ParseRecreationImageFrame("99999999999999999999999").has_value(),
          "an index past size_t is refused");
  }
  {
    Frames frames;
    Check(!frames.Find(0).has_value(), "an image not held has no frame");
    frames.Hold(0, 3);
    Check(frames.Find(0) == 3u, "paint image 0 is an image's and is held");
    frames.Hold(0, 5);
    Check(frames.Find(0) == 5u, "a later hold replaces the frame");
    frames.Hold(1, 0);
    Check(frames.Find(1) == 0u, "the first frame is held as 0");
    Check(frames.Find(0) == 5u, "each paint image has its own frame");
    frames.Hold(-2, 4);
    Check(!frames.Find(-2).has_value(), "an invalid ID is not held");
  }
  {
    // Held on one thread, as Blink's main thread does, and read on
    // another, as the compositor thread does.
    Frames frames;
    std::thread holder([&frames] {
      for (int64_t id = 0; id < 1000; ++id) {
        frames.Hold(id, static_cast<std::size_t>(id % 8));
      }
    });
    std::size_t seen = 0;
    std::thread reader([&frames, &seen] {
      for (int pass = 0; pass < 50; ++pass) {
        for (int64_t id = 0; id < 1000; ++id) {
          if (const auto frame = frames.Find(id)) {
            Check(*frame == static_cast<std::size_t>(id % 8),
                  "a frame read on another thread is the one held");
            ++seen;
          }
        }
      }
    });
    holder.join();
    reader.join();
    for (int64_t id = 0; id < 1000; ++id) {
      Check(frames.Find(id) == static_cast<std::size_t>(id % 8),
            "every frame held is read after the holder ends");
    }
    (void)seen;
  }
  std::puts("recreation image frame tests passed");
  return 0;
}
