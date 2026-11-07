using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Recorder.Analysis.Setup;

/// <summary>A graphics card as Windows or NVIDIA's driver reports it.</summary>
/// <param name="Source">"nvidia-smi" or "Windows", where the figures came from.</param>
public sealed record GraphicsCard(string Name, long MemoryBytes, string? Driver, string Source);

/// <summary>
/// What the setup window shows of this PC: its graphics cards with their
/// memory, its memory, and whether NVIDIA's driver is installed. Reported
/// by the PC, not estimated; see docs/architecture/windows-installer.md,
/// "Measured, not guessed".
/// </summary>
public sealed record PcHardware(IReadOnlyList<GraphicsCard> GraphicsCards, long MemoryBytes, bool NvidiaDriver)
{
    /// <summary>The most memory on one graphics card, 0 with none.</summary>
    public long LargestGraphicsMemory => GraphicsCards.Count == 0 ? 0 : GraphicsCards.Max(card => card.MemoryBytes);

    public static PcHardware Read()
    {
        var cards = NvidiaCards();
        var nvidia = cards.Count > 0;
        foreach (var card in WindowsCards())
        {
            // A card nvidia-smi reported is not listed twice.
            if (!cards.Any(known => string.Equals(known.Name, card.Name, StringComparison.OrdinalIgnoreCase)))
            {
                cards.Add(card);
            }
        }

        return new PcHardware(cards, InstalledMemory(), nvidia);
    }

    /// <summary>
    /// nvidia-smi's answer to
    /// <c>--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits</c>,
    /// one card a line, memory in MiB.
    /// </summary>
    public static IReadOnlyList<GraphicsCard> ParseNvidiaSmi(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var cards = new List<GraphicsCard>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length >= 2 &&
                long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mebibytes) &&
                fields[0].Length > 0)
            {
                cards.Add(new GraphicsCard(fields[0], mebibytes * 1024 * 1024, fields.Length > 2 && fields[2].Length > 0 ? fields[2] : null, "nvidia-smi"));
            }
        }

        return cards;
    }

    private static List<GraphicsCard> NvidiaCards()
    {
        var path = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return [];
            }

            return process.ExitCode == 0 ? [.. ParseNvidiaSmi(output.Result)] : [];
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return [];
        }
    }

    // The display adapters' class key. HardwareInformation.qwMemorySize is
    // the card's memory in bytes as a 64-bit value; the older 32-bit value,
    // like WMI's AdapterRAM, stops at 4 GB.
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private static IEnumerable<GraphicsCard> WindowsCards()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        RegistryKey? root;
        try
        {
            root = Registry.LocalMachine.OpenSubKey(DisplayClass);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            yield break;
        }

        if (root is null)
        {
            yield break;
        }

        using (root)
        {
            foreach (var name in root.GetSubKeyNames().Where(name => name.Length == 4 && name.All(char.IsAsciiDigit)))
            {
                GraphicsCard? card = null;
                try
                {
                    using var key = root.OpenSubKey(name);
                    var description = key?.GetValue("DriverDesc") as string;
                    var memory = key?.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long value => value,
                        byte[] { Length: 8 } bytes => BitConverter.ToInt64(bytes),
                        _ => 0L
                    };
                    if (!string.IsNullOrWhiteSpace(description) && memory > 0)
                    {
                        card = new GraphicsCard(description, memory, key?.GetValue("DriverVersion") as string, "Windows");
                    }
                }
                catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                }

                if (card is not null)
                {
                    yield return card;
                }
            }
        }
    }

    private static long InstalledMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                return (long)status.TotalPhysical;
            }
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    /// <summary>The free space on the drive that holds a folder, 0 when it cannot be read.</summary>
    public static long FreeSpace(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            return root is null ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}

/// <summary>
/// How a model file compares with this PC: a comparison of sizes, not a
/// measurement. llama.cpp decides the split between the graphics card and
/// the PC's memory when it loads the model (its "fit" setting, on unless a
/// profile sets the layers itself).
/// </summary>
public static class FitComparison
{
    /// <summary>Memory left for Windows, the recorder and llama.cpp's own buffers: 4 GB.</summary>
    public const long Reserve = 4L * 1024 * 1024 * 1024;

    public static string Describe(long fileBytes, PcHardware hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        var file = ByteSize.Gigabytes(fileBytes);
        var graphics = hardware.LargestGraphicsMemory;
        if (fileBytes > graphics + hardware.MemoryBytes - Reserve)
        {
            return $"{file}: larger than this PC's graphics memory and memory together, less 4 GB for Windows, so it cannot be loaded here.";
        }

        if (!hardware.NvidiaDriver)
        {
            return $"{file}: no NVIDIA driver was found, so it would run on the processor only, many times slower.";
        }

        if (fileBytes <= graphics - (2L * 1024 * 1024 * 1024))
        {
            return $"{file}: smaller than the graphics card's {ByteSize.Gigabytes(graphics)}, so it can all go on the card, with room for the context.";
        }

        return $"{file}: larger than the graphics card's {ByteSize.Gigabytes(graphics)} less room for the context, so part of it runs from the PC's memory, which is slower.";
    }

    /// <summary>True when the file is larger than graphics memory and memory together, less the reserve.</summary>
    public static bool TooLarge(long fileBytes, PcHardware hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        return fileBytes > hardware.LargestGraphicsMemory + hardware.MemoryBytes - Reserve;
    }
}
