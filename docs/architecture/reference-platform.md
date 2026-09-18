# Reference Windows Test Platform

## Purpose

This document identifies the primary computer for prototype performance, stability, compatibility, and observer-effect measurements. Results from this system establish the first baseline; they do not establish minimum production hardware requirements.

## Operating-system baseline

- **Minimum product release:** Windows 10 x64
- **Initial Windows 10 compatibility baseline:** Windows 10 version 22H2, build 19045
- **Reference-machine edition:** Windows 10 Pro
- **Reference-machine version:** 22H2
- **Reference-machine build at inventory:** 19045.6466

Earlier Windows 10 versions are outside the initial acceptance matrix. Windows 11 remains a required test platform for capabilities that are unavailable on the Windows 10 baseline.

## Hardware inventory

Inventory captured on September 16, 2026.

| Component | Reference value |
| --- | --- |
| Computer | `DESKTOP-7LV04L7` |
| System manufacturer | Gigabyte Technology Co., Ltd. |
| System product | B450M DS3H |
| Mainboard | Gigabyte Technology Co., Ltd. B450M DS3H-CF |
| BIOS | F50 |
| Processor | AMD Ryzen 9 3900X 12-Core Processor |
| Logical processors | 24 |
| Physical memory | 34,304,741,376 bytes, approximately 32 GiB |
| Graphics adapter | NVIDIA GeForce RTX 2060 |
| Graphics driver | 32.0.15.6094 |
| Solid-state storage | Seagate BarraCuda 120 SSD ZA1000CM10003 |
| Additional storage | ST2000DM001-1ER164 |
| Primary display | 1920 by 1080 |

The inventory mechanism could not read volume capacity and free-space values under its restricted account. Each performance run must record the selected archive volume, available free space, and whether the archive is written to the SSD or hard disk.

## Compatibility implications

### Process-specific audio

The reference machine runs Windows build 19045. Microsoft documents process-specific WASAPI loopback as requiring build 20348 or later ([Microsoft process-loopback requirements](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params)). Process-specific screen-reader audio is therefore unavailable through that API on this Windows 10 reference configuration.

Windows 10 tests must:

- Declare process-specific audio unavailable before recording starts.
- Offer whole-system loopback only when the user consented to it.
- Keep microphone and system playback in separate tracks.
- Record that playback audio is not isolated to the screen-reader process.
- Avoid presenting system-loopback results as process-specific evidence.

Process-specific audio must be tested separately on a compatible Windows 11 system.

### .NET support

The reference machine runs Windows 10 Pro rather than a Windows 10 Enterprise LTSC edition. Current .NET 10 support documentation lists maintained Windows 10 Enterprise LTSC variants, not Windows 10 Pro 22H2 ([official .NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)).

The project may choose to support this reference configuration, but it must treat that as project-owned compatibility. The first runtime spike must verify application launch, WPF accessibility, Win32 and WinRT interop, self-contained deployment, media encoding, and long-session stability on this exact machine.

If .NET 10 does not operate reliably on the reference configuration, the runtime decision must be reopened rather than quietly dropping Windows 10 support.

## Benchmark controls

Every benchmark result must record:

- Inventory timestamp.
- Windows edition, version, build, and update revision.
- BIOS version.
- Processor and logical processor count.
- Installed and available memory.
- Graphics adapter and driver.
- Display count, resolution, refresh rate, scaling, and HDR state.
- Audio input and output devices and formats.
- Selected archive drive, media type, and free space.
- Power plan and whether the system is on battery or external power.
- Active screen reader, version, synthesizer, and speech settings.
- Browser, tested application, and version.
- Capture channels and their exact settings.
- Baseline measurements with recording disabled.

Hardware or driver changes do not invalidate the machine as the reference platform, but they create a new dated benchmark configuration.

## Initial performance scenario

The standard workload on this computer is:

- One 1920 by 1080 display captured at 30 frames per second.
- H.264 video encoding.
- Participant microphone when consented.
- Whole-system loopback.
- Process-specific audio only on a compatible Windows 11 comparison system.
- Raw keyboard and mouse input.
- Foreground window and process state.
- UI Automation events and bounded snapshots.
- A 60-minute validation session.
- A four-hour maximum-duration test after the 60-minute gate passes.

The provisional resource budgets in the [prototype architecture](prototype-architecture.md) apply to this workload and reference computer. They must be revised from measured results rather than relaxed automatically when a collector exceeds them.
