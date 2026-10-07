using Recorder.Database.RecordingFiles;

// Writes the events of a finished recording file as newline-delimited JSON,
// for the Windows validation scripts. See RecordingFileEventExport.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: RecordingEventExport RECORDING_FILE OUTPUT_FILE");
    return 2;
}

try
{
    using var reader = RecordingFileReader.Open(args[0]);
    var output = Path.GetFullPath(args[1]);
    var temporary = output + ".partial";
    long count;
    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
    {
        count = RecordingFileEventExport.Write(reader, stream);
    }
    File.Move(temporary, output, overwrite: true);
    Console.WriteLine($"EVENTS_EXPORTED={count}");
    return 0;
}
catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
