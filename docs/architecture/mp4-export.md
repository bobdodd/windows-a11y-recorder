# MP4 export of a recording

Proposed 2026-11-07. Not agreed or built.

## Purpose

The owner wants to make an MP4 video from a recording, with the
microphone, the system sound and, where it was recorded, the screen
reader's speech as options. Captions and annotations come later and are
not part of this design, but it leaves room for them (see "Later").

## What a recording holds today

- **Picture.** Desktop frames, saved as PNG files at a fixed rate, 5 a
  second by default (`DesktopFrameCollector`, `{sequence:D10}.png`), with
  the pointer drawn in (`IsCursorCaptureEnabled = true`). Each frame has
  its time on the session clock, its position on the virtual screen and,
  from 2026-10-07, the full screen magnification and color effect in
  effect (`SessionVideoFrame`).
- **Sound.** Up to two WAV files, `audio/microphone.wav` and
  `audio/system.wav`, each with the session time its stream started
  (`SessionAudioTrack`). The collector also records an `audio-buffer`
  event for every buffer, with its byte offset in the file, its length and
  the session time it arrived (`AudioCollector.CreateBufferPayload`).
- **Screen reader.** NVDA's speech is not recorded on its own. It is part
  of the system sound, as is every other program's sound
  ([screen reader activity](screen-reader-activity.md), item 6).
  NVDA's sound is measured only as peak levels per audio session, to
  find when it was making sound.

## Two findings that shape the design

1. **The system sound file has no silence in it.** Windows' loopback
   capture gives no data at all while nothing is playing: "If no audio is
   playing whatsoever, then the `DataAvailable` event won't fire"
   ([NAudio](https://naudio.github.io/NAudio/docs/WasapiLoopbackCapture.html)).
   Ignoring this means the recording will "omit the wait and abut the two
   'audio is playing' portions"
   ([Matthew van Eerde](https://matthewvaneerde.wordpress.com/2008/12/16/sample-wasapi-loopback-capture-record-what-you-hear/)).
   `system.wav` is therefore shorter than the recording whenever the PC
   was silent, and reading it from its start time puts every sound after
   the first silence too early. The export places each buffer at the time
   its `audio-buffer` event records and fills the gaps with silence. The
   player reads the file as one continuous stream from its start time
   (`SessionAudioPlayer.Seek`), so it may play sound early after a
   silence. That is not checked on a recording yet and is outside this
   design.
2. **The screen reader can only be separated while recording.** It cannot
   be taken out of `system.wav` afterwards. Windows can capture one
   process and its children on their own, or everything except them,
   through process loopback
   ([Microsoft, PROCESS_LOOPBACK_MODE](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode)).
   Microsoft gives Windows 10 build 20348 as the minimum
   ([Microsoft, AUDIOCLIENT_ACTIVATION_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-audioclient_activation_type)).
   The NAudio 2.2.1 package the recorder already uses contains the
   `ProcessLoopbackMode` and `AudioClientProcessLoopbackParams` types.
   So "where available" means recordings made after this change, on
   Windows 10 build 20348 or later, with NVDA running.

## Proposal

### Export

- A "Save as MP4" command in the player, for the open recording. It asks
  where to save, with the recording's name as the file name, and which
  sounds to include: microphone, system sound and screen reader. Each is
  offered only if the recording has it.
- The picture follows the player's view setting: the participant's view
  (magnification and color effect applied, as the player shows by
  default) or the whole screen as captured. The participant's view is
  drawn with the same code the player uses (`MagnifiedView`,
  `ColorEffect`), so the video and the player agree.
- Each recorded frame becomes one video frame, shown from its recorded
  time until the next frame's. Nothing is invented between frames.
- The export runs in the background with progress, can be cancelled, and
  writes to a temporary file that is renamed only when complete, so a
  cancelled or failed export leaves no partial MP4.
- The first version exports the whole recording.

### Encoding

- Windows' own Media Foundation writes the file: its H.264 video encoder,
  AAC audio encoder and MP4 file sink
  ([Microsoft, MPEG-4 file sink](https://learn.microsoft.com/en-us/windows/win32/medfound/mpeg-4-file-sink);
  [H.264 video encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-encoder);
  [AAC encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/aac-encoder)).
  Nothing is added to the installer and no codec licence notices are
  needed. Windows uses a graphics card's hardware encoder when one is
  available.
- Windows N editions lack Media Foundation until the Media Feature Pack is
  installed
  ([Microsoft](https://support.microsoft.com/en-us/windows/experience/platform-variants/media-feature-pack-for-windows-n)).
  The export then says so and names the pack.
- Windows' H.264 decoder plays up to 4096 by 2304 pixels
  ([Microsoft, H.264 video decoder](https://www.learn.microsoft.com/en-us/windows/desktop/medfound/h-264-video-decoder)).
  A larger picture, such as two monitors side by side, is scaled down to
  fit, keeping its shape, so Windows' own player can open the file.

### Sound

- The chosen sounds are mixed into one AAC track, at the levels set on the
  player's sliders, so the file plays the same way in any player.
- Each source is placed by its buffer records, with silence where nothing
  was captured, as in finding 1.
- Older recordings offer microphone and system sound. Their system sound
  includes the screen reader, and the dialog says so.

### Recording the screen reader on its own

For new recordings, a third audio stream, `audio/screen-reader.wav`,
captures NVDA's process tree on its own through process loopback. The
open question is what the system sound holds then (question 1).

## Questions for the owner

1. **System sound with a separate screen reader track.**
   - (a) Recommended: the system sound leaves out NVDA. Any combination of
     the three sounds then exports correctly, including system sound
     without speech. The player then needs a third slider, or NVDA would
     no longer be heard there.
   - (b) The system sound stays as it is, with NVDA in it, and the screen
     reader track is extra. Nothing changes for the player, but "system
     sound without the screen reader" cannot be exported, and choosing both
     plays the speech twice, so the dialog would offer one or the other.
2. **Sound tracks in the file.**
   - (a) Recommended: one mixed track only.
   - (b) The mixed track, plus each source as its own extra track for
     editing. Many players play only the first track.
3. **Part of the recording.** The whole recording only in the first
   version (recommended), or also from the player's current position to a
   chosen end?

## Later

Captions and annotations can be added to the same export: captions as an
MP4 text track or burned into the picture, and annotations drawn onto the
frames before encoding. Neither is designed here.
