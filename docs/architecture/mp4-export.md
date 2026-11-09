# MP4 export of a recording

Proposed and agreed 2026-11-07. Built 2026-11-07. Captions and
annotations come later and are not part of this design (see "Later").

## Purpose

The owner wants to make an MP4 video from a recording, with the
microphone, the system sound and, where it was recorded, the screen
reader's speech as options.

## Decisions

| Question | Owner's answer (2026-11-07) | What it means |
| --- | --- | --- |
| 1. System sound with a separate screen reader track | "option a" | The system sound leaves out NVDA, which is recorded on its own track. The player has a third volume slider for it. |
| 2. Sound tracks in the file | "option b" | The MP4 has one track of the chosen sounds mixed, then each chosen sound as its own extra track. |
| 3. Part of the recording | "Whole recording - editing is not part of this tool." | The export is always the whole recording. There is no start or end to choose. |

## What a recording holds

- **Picture.** Desktop frames, saved as PNG files at a fixed rate, 5 a
  second by default (`DesktopFrameCollector`), with the pointer drawn in.
  Each frame has its time on the session clock, its position on the
  virtual screen and, from 2026-10-07, the full screen magnification and
  color effect in effect (`SessionVideoFrame`).
- **Sound.** Up to three WAV files, `audio/microphone.wav`,
  `audio/system.wav` and, from this change, `audio/screen-reader.wav`,
  each with the session time its stream started (`SessionAudioTrack`).
  The collector records an `audio-buffer` event for every buffer, with its
  byte offset in the file, its length and the session time it arrived.

## Recording the screen reader on its own

The screen reader can only be separated while recording; it cannot be
taken out of `system.wav` afterwards. Windows can capture one process and
the processes it started on their own, or everything except them, through
process loopback
([Microsoft, PROCESS_LOOPBACK_MODE](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode)),
from Windows build 20348
([Microsoft, AUDIOCLIENT_ACTIVATION_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-audioclient_activation_type)).
The capture follows Microsoft's application loopback sample
([LoopbackCapture.cpp](https://raw.githubusercontent.com/microsoft/Windows-classic-samples/main/Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp)):
`ActivateAudioInterfaceAsync` on the virtual device
`VAD\Process_Loopback`, with the process and mode in a `VT_BLOB`
activation parameter (`ProcessLoopbackCapture`).

When system sound is chosen for a recording:

- **NVDA running, build 20348 or later.** Two captures are opened on the
  NVDA process found when the recording starts (`nvda.exe` in the
  recorder's Windows session; the earliest started if more than one):
  - `audio.system`, `audio/system.wav`: every program's sound except
    NVDA's process tree. Its device is recorded as "Every program's sound
    except NVDA (process N)".
  - `audio.screen-reader`, `audio/screen-reader.wav`: NVDA's process tree
    only. Its device is recorded as "NVDA (process N) and the programs it
    started".
- **NVDA not running.** The system sound is the speakers' loopback, as
  before, and there is no screen reader track.
- **NVDA running, but Windows older than build 20348, or the captures
  fail.** The system sound is the speakers' loopback, NVDA included, and
  the collector reports the limitation
  `screen-reader-unavailable:requires-windows-build-20348` or
  `screen-reader-unavailable:<error type>:<HRESULT>`.

The process is fixed when the recording starts. If NVDA is restarted
during a recording, the new process is not followed: its speech is then
in neither track, because the system track still leaves out only the
first process's tree, and the old process is gone. Restart NVDA before
starting a recording, not during one.

Both captures ask Windows for 48 kHz, 16-bit stereo and let Windows
convert to it, with a 200 ms buffer, event driven.

The new stream uses the existing audio event types and tables. The
channel `audio.screen-reader` is added to the payload validator, the audio
payload schema's stream names and the evidence catalog, and is stored in
the same audio stream, buffer and error tables as the other two. No
payload property was added: what the stream holds is in its device
string, so no evidence table needed a migration.

Windows 10 22H2 is build 19045, below 20348, so on Windows 10 the screen
reader cannot be separated and recordings behave as before.

## The player

- A third volume slider, "Screen reader", beside the microphone and
  system sound sliders. The screen reader stream is drawn on the
  timeline's sound lane with the system sound, and has its own entry in
  the channel filter.
- A "Save as MP4..." button, enabled when a recording with frames is
  open, opens the export window.

## Export

- The export window says what will be saved: the whole recording, its
  length, the picture size and frame rate, and which picture.
- **Picture.** The player's Whole screen setting at the time the window
  opens decides the picture: the participant's view (the part of the
  screen Magnifier showed, in the colors it showed) or the whole screen
  with the participant's view outlined. Frames are drawn with the
  player's own code (`MainWindow.FrameImage`), so the video and the player
  agree.
- **Sound.** A check box for each sound the recording has, each checked
  by default and labelled with the level it is mixed at, taken from the
  player's sliders, which go from -36 to 0 dB. A recording
  with system sound but no screen reader track says that any speech is in
  the system sound.
- **File.** The default is the recording folder's name with `.mp4`, in
  the Windows Videos folder, with Browse to choose another.
- **Saving.** Save starts the export on a thread of its own, with a
  progress bar and a status line that screen readers announce at each
  tenth of the way. Stop cancels it. The file is written as
  `<name>.mp4.partial` and renamed only when complete, so a stopped or
  failed export leaves no MP4.

### Timing of the picture

Windows' H.264 encoder times its output by the frame rate it is given,
not by each sample's own duration. This was found on 2026-11-07 on the
owner's PC: a test export with a 0.5 s black start and a frame held for
1.2 s came out with every sample 0.2 s long, the picture ending 0.1 s
early and every frame after the start shown too soon. The export
therefore has a constant frame rate:

- The recorded rate is the frames' usual (median) spacing, to the nearest
  whole frame a second; 5 for the recorder's default.
- The export's rate is the smallest multiple of the recorded rate of at
  least 20 a second, up to 120: 20 for 5 recorded, 21 for 7, 30 for 30.
- Each recorded frame is shown from the video frame nearest its time, so
  within half a video frame (25 ms at 20 a second), until the next
  recorded frame. The last is shown until the end of the recording, and
  the picture is black before the first.
- A video frame that repeats the one before is not drawn again, and costs
  the encoder next to nothing.

The same test after the change: 70 video frames of 50 ms, 10 of black,
then each recorded frame on 4, ending at 3.5 s, the recording's length.

A recording of an Android phone is exported at 60 frames a second,
fixed. The phone sends a picture only when its screen changes, up to
120 a second while it moves and none while it is still, so the spacing
of its pictures has no usual value. Each picture is shown from the
video frame nearest its time, within 8.3 ms. Of pictures less than a
sixtieth of a second apart, only the last before each video frame is
shown; the recording keeps them all, and the export window says so. In
the recording of the Android plan's test 23, 131 pictures in 18.7
seconds, 89 are shown at 60 a second and 128 would be at 120. Why 120
is not used is under "Encoding"
([Android implementation plan](android-implementation-plan.md),
"Built, step 3c").

### Placing the sound

Windows' loopback capture gives no data while nothing is playing: "If no
audio is playing whatsoever, then the `DataAvailable` event won't fire"
([NAudio](https://naudio.github.io/NAudio/docs/WasapiLoopbackCapture.html)).
Reading such a file straight through will "omit the wait and abut the
two 'audio is playing' portions"
([Matthew van Eerde](https://matthewvaneerde.wordpress.com/2008/12/16/sample-wasapi-loopback-capture-record-what-you-hear/)).
The export places each stream's data by its `audio-buffer` records
(`AudioPlacement`):

- Each buffer is taken to start at its arrival time less its duration.
- Data that follows on within 50 ms of where the last buffer ended is
  placed straight after it, so small timing jitter does not cut the sound.
- A buffer more than 50 ms after that point starts a new run at its
  recorded time, with silence before it.
- A buffer more than 50 ms before that point (the data has run ahead of
  its times, as a device clock faster than the recorder's makes it) has
  its overlapping part left out.
- Records beyond the data the file holds are ignored.
- A stream without buffer records, as from a recording whose records
  could not be read, is placed as one run from its start, as the player
  plays it.

The records are read in bulk: from the recording file
(`RecordingFileTimeline.ReadAudioBuffers`) or from the database
(`DatabaseAudioBufferSource`).

### Tracks

- Each chosen sound is converted to 48 kHz stereo: one channel copied to
  both sides, more than two folded down, and other rates resampled.
- **Track 1:** the chosen sounds mixed at the player's levels.
- **Then:** each chosen sound at its recorded level, in the order
  microphone, system sound, screen reader.
- All are AAC at 192 kbit/s.
- Windows' MP4 writer marks every track as enabled, and a player that
  plays every enabled track, as QuickTime does, would play the mix and
  each own track at once. After writing, each sound track's header is
  marked in place (`Mp4TrackGroups`): all sound tracks in alternate group
  1, an "identifies a collection of movie tracks that contain alternate
  data for one another"
  ([Apple, track header atom](https://developer.apple.com/documentation/quicktime-file-format/track_header_atom)),
  and only the mixed track enabled. FFmpeg writes the same marks: sound
  tracks share one group and one of each kind is enabled
  ([FFmpeg, movenc.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavformat/movenc.c)).

### Encoding

- Windows' own Media Foundation writes the file: its H.264 video encoder,
  AAC audio encoder and MP4 file sink
  ([Microsoft, MPEG-4 file sink](https://learn.microsoft.com/en-us/windows/win32/medfound/mpeg-4-file-sink);
  [H.264 video encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-encoder);
  [AAC encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/aac-encoder)).
  Nothing is added to the installer.
- H.264 High profile, unconstrained variable bit rate, at an average of
  0.2 bits per pixel per recorded frame, from 1 to 20 Mbit/s: about
  2 Mbit/s for 1920 by 1080 at 5 recorded frames a second.
- Up to three attempts, each starting the file again: with the graphics
  card's encoder allowed; with Windows' software encoder; and with the
  software encoder at its default settings. The result names the attempt
  used and the errors of any before it.
- Windows' H.264 decoder plays up to 4096 by 2304 pixels
  ([Microsoft, H.264 video decoder](https://www.learn.microsoft.com/en-us/windows/desktop/medfound/h-264-video-decoder)).
  The export is the size of the largest frame, made smaller to fit within
  that with its shape kept, and even in both directions. Smaller frames
  are centred on black.
- A recording of an Android phone changes shape when the phone is
  turned, so its export is as wide as its widest picture and as tall as
  its tallest, made smaller to fit within 4096 by 2304 in the same way.
  Each picture is drawn as large as fits, centred on black: 1034 by 2304
  for a phone held upright throughout, 2304 by 2304 for one turned on
  its side.
- The encoder is given no H.264 level and chooses its own. On
  2026-11-09, on the development PC, a phone recording at 2304 by 2304
  was saved at level 5.2 at 60 frames a second and at level 6.0 at 120.
  Windows decoded both, but Microsoft documents its decoder only up to
  level 5.1
  ([Microsoft, H.264 video decoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-decoder)),
  and that other players play level 6.0 is not checked. The owner
  decided on 2026-11-09 to keep the phone's rate at 60.
- Windows N editions lack Media Foundation until the Media Feature Pack is
  installed
  ([Microsoft](https://support.microsoft.com/en-us/windows/experience/platform-variants/media-feature-pack-for-windows-n)).
  The export then says "Windows Media Foundation is not installed. On an
  N edition of Windows, install the Media Feature Pack."

## Checks made

- Unit tests (`Mp4ExportTests`): sound placement (straight-through runs,
  silence inserted, overlap left out, records beyond the file), the
  picture schedule (black start, constant frame length, nearest video
  frame, same-time frames, last frame), sizes and frame rates, bit rate,
  the mix (levels, mono to stereo, resampling, silence), buffer records
  read back from a recording file, the screen reader events accepted by
  the validator, and the track marks.
- On the owner's PC (Windows 10 build 19045), 2026-11-07, with a test
  program using the export code outside the app:
  - Generated frames and sound: H.264 High 1366 by 768 at 20 frames a
    second, 3.5 s; three AAC tracks; the picture the right way up; each
    sound where its records put it; the mix with the system sound at
    -6 dB; only the mixed track marked default. Written with the graphics
    card's encoder allowed, in 0.8 s.
  - A 20.7 s recording with microphone and system sound: 1920 by 1080,
    three sound tracks, written in 7.9 s.
- Not checked yet: the export window in the app, a recording with a
  screen reader track (needs Windows build 20348 or later, for example
  Windows 11), and an N edition. See
  [the validation plan](../validation/mp4-export-plan.md).

## Known limits

- **The player and silences.** The player reads each WAV file as one
  continuous stream from its start time (`SessionAudioPlayer.Seek`), so
  after a silence in the system sound it may play sound early. The export
  does not have this problem. Changing the player is outside this
  feature.
- **NVDA restarted during a recording** is not followed (see "Recording
  the screen reader on its own").
- The export uses the levels and Whole screen setting as they are when the
  window opens.

## Later

Captions and annotations can be added to the same export: captions as an
MP4 text track or burned into the picture, and annotations drawn onto the
frames before encoding. Neither is designed here.
