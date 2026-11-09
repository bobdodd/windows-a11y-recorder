namespace Recorder.Contracts;

/// <summary>
/// The records of the Android sound collector, slice 4 of
/// docs/architecture/android-implementation-plan.md: the phone's whole audio
/// output, as scrcpy's server captures it on the phone.
///
/// The records have the desktop audio collector's shapes, on their own
/// channel and with the stream named <see cref="StreamName"/>, so the
/// player and the MP4 export read the phone's sound as they read the
/// desktop's. The sound is stored, as received, in <see cref="WavePath"/>.
/// Each packet of it has an <c>audio-buffer</c> record giving where its
/// bytes are in that file and, as on the desktop, a session time for its
/// last sample in <c>callbackMonotonicNanoseconds</c>, from which its first
/// sample's time is estimated by its length. On the phone that time is the
/// phone time the server gave the packet, placed on the session clock by
/// the companion collector's clock fit, and the packet's own record also
/// gives that phone time and the time the recorder read it.
/// </summary>
public static class AndroidSoundRecords
{
    public const string Channel = "audio.device";

    /// <summary>The stream's name in its records, beside the desktop's microphone, system and screen-reader.</summary>
    public const string StreamName = "device";

    public const string StreamStartedEventType = "audio-stream-started";
    public const string StreamStoppedEventType = "audio-stream-stopped";
    public const string BufferEventType = "audio-buffer";
    public const string ErrorEventType = "audio-stream-error";

    /// <summary>The sound as received, relative to the recording's folder.</summary>
    public const string WavePath = "audio/device.wav";

    /// <summary>
    /// The packet's time is the phone time the server took from Android's
    /// audio timestamp, taken to be the time of the packet's last sample.
    /// </summary>
    public const string PhoneAudioTimeFlag = "phone-audio-time";

    /// <summary>The desktop's flag for a first sample's time worked out from the buffer's length.</summary>
    public const string FirstSampleEstimatedFlag = "first-sample-time-estimated";
}

/// <summary>The sound's sample format, named as the desktop's audio records name it.</summary>
public sealed record AndroidSoundFormat(
    string Encoding,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    int BlockAlign,
    int AverageBytesPerSecond);

/// <summary>
/// The sound's start, at its first sample, or its end, with how much was
/// received and how loud it was over the whole recording.
/// </summary>
/// <param name="Device">The phone and how its sound was captured.</param>
/// <param name="EndpointVolumeScalar">Always null: the phone's volume is not read.</param>
/// <param name="PeakAmplitude">The loudest sample, as a fraction of full scale; null before any sound.</param>
public sealed record AndroidSoundStream(
    string Stream,
    string Path,
    string Device,
    double? EndpointVolumeScalar,
    AndroidSoundFormat Format,
    long DataBytes,
    long BuffersObserved,
    long BuffersDropped,
    double? PeakAmplitude,
    double? PeakDbfs,
    double? RmsAmplitude,
    double? RmsDbfs);

/// <summary>One packet of sound, stored in the WAV file's data at its byte offset.</summary>
/// <param name="CallbackMonotonicNanoseconds">The session time of the packet's last sample, as the desktop's callback time is.</param>
/// <param name="PresentationMicroseconds">The phone time the server gave the packet, in microseconds of the phone's CLOCK_MONOTONIC.</param>
/// <param name="ReceivedAtNanoseconds">The session time the recorder had read the whole packet.</param>
public sealed record AndroidSoundBuffer(
    string Stream,
    string Path,
    long BufferSequence,
    long DataByteOffset,
    int ByteLength,
    int SampleFrames,
    long DurationNanoseconds,
    long EstimatedFirstSampleMonotonicNanoseconds,
    long CallbackMonotonicNanoseconds,
    long PresentationMicroseconds,
    long ReceivedAtNanoseconds);

/// <summary>Why the phone's sound is missing from part or all of a recording.</summary>
public sealed record AndroidSoundError(string Stream, string? ErrorType, string? Message);
