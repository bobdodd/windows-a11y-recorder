using System.IO.Compression;

namespace Recorder.Collectors.Android;

/// <summary>
/// Unpacks a downloaded Platform-Tools archive into the person's
/// platform-tools folder. The archive is unpacked beside the folder first
/// and checked: every entry inside platform-tools/, adb.exe present, and
/// source.properties naming the pinned revision. Only then is the old
/// folder moved aside and replaced, so a failed install leaves the previous
/// one working.
/// </summary>
public static class PlatformToolsInstaller
{
    private const string Root = "platform-tools/";

    /// <summary>The files the recorder runs; an archive without them is refused.</summary>
    public static readonly IReadOnlyList<string> RequiredFiles = ["adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll", "source.properties"];

    public static void Install(string archivePath, string platformToolsFolder, string expectedRevision)
    {
        ArgumentException.ThrowIfNullOrEmpty(archivePath);
        ArgumentException.ThrowIfNullOrEmpty(platformToolsFolder);
        var target = Path.GetFullPath(platformToolsFolder);
        var parent = Path.GetDirectoryName(target) ?? throw new IOException($"{target} has no parent folder.");
        Directory.CreateDirectory(parent);
        var staging = target + ".new";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(Root, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"The Platform-Tools archive has {entry.FullName}, outside its platform-tools folder.");
                    }

                    var relative = name[Root.Length..];
                    if (relative.Length == 0 || name.EndsWith('/'))
                    {
                        continue;
                    }

                    var path = Path.GetFullPath(Path.Combine(staging, relative));
                    if (!path.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"The Platform-Tools archive has {entry.FullName}, which points outside its folder.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    entry.ExtractToFile(path, overwrite: true);
                }
            }

            foreach (var file in RequiredFiles)
            {
                if (!File.Exists(Path.Combine(staging, file)))
                {
                    throw new InvalidDataException($"The Platform-Tools archive has no {file}.");
                }
            }

            var revision = ReadRevision(staging);
            if (revision != expectedRevision)
            {
                throw new InvalidDataException($"The Platform-Tools archive is revision {revision ?? "unknown"}, not {expectedRevision}.");
            }

            // The earlier install is moved aside whole, not deleted file by
            // file, so a running adb.exe, which Windows will not let go,
            // stops the install before anything is lost.
            var previous = target + ".old";
            if (Directory.Exists(previous))
            {
                Directory.Delete(previous, recursive: true);
            }

            if (Directory.Exists(target))
            {
                try
                {
                    Directory.Move(target, previous);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new IOException(
                        $"The earlier Platform-Tools in {target} is in use, probably by a running adb. Close programs using adb, " +
                        "or run \"adb kill-server\", then install again.", exception);
                }
            }

            Directory.Move(staging, target);
            if (Directory.Exists(previous))
            {
                try
                {
                    Directory.Delete(previous, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Left for the next install to remove.
                }
            }
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>The Pkg.Revision in a Platform-Tools folder's source.properties, or null.</summary>
    public static string? ReadRevision(string platformToolsFolder)
    {
        var file = Path.Combine(platformToolsFolder, "source.properties");
        if (!File.Exists(file))
        {
            return null;
        }

        foreach (var line in File.ReadLines(file))
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && line[..separator].Trim() == "Pkg.Revision")
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return null;
    }
}
