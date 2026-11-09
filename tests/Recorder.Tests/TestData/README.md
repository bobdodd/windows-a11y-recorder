# Test data

`android-screen-sample.h264` is an H.264 stream the size of the Pixel 10 Pro XL's screen, 1080 by 2404, in the High profile, limited range, BT.709, as the phone's own stream is (docs/validation/android-recording-plan.md, results of part 3, step 3a). It is made with FFmpeg and holds no picture from a phone: six pictures of one colour (sRGB C03020), then six of another (sRGB 2060A0), at 30 a second, with no B-frames.

```
ffmpeg -f lavfi -i "color=c=0xC03020:s=1080x2404:r=30:d=0.2" -f lavfi -i "color=c=0x2060A0:s=1080x2404:r=30:d=0.2" -filter_complex "[0][1]concat=n=2:v=1,scale=out_color_matrix=bt709:out_range=tv,format=yuv420p" -c:v libx264 -profile:v high -preset veryfast -bf 0 -g 60 -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv -x264-params "aud=1" android-screen-sample.h264
```

`android-screen-sample.lengths` gives each access unit's length in bytes, one a line, from `ffprobe -show_packets -show_entries packet=size -of csv=p=0`. The first holds the parameter sets with the key frame.
