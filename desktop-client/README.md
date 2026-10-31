# Brain Platform Desktop Client

The desktop client is an independent .NET 10 WPF shell for the local brain
research workstation. It currently checks the existing Python backend at
`http://127.0.0.1:8000/api/health` and shows explicit readiness states.

The maintained acquisition/review architecture is documented in
[`docs/architecture/desktop-acquisition-review.md`](../docs/architecture/desktop-acquisition-review.md).

The acquisition workspace is waveform-first. `SciChart` renders only retained
raw EEG display points; the C# acquisition runtime remains the owner of device
I/O, raw persistence, counter-gap auditing, and the display ring buffer.

It does not produce mock EEG. Its default adapter cannot discover or open a
device. The portable release includes the licensed x64 ANT/eego SDK under
`sdk\eego-SDK.dll`; the adapter uses it automatically when no valid local SDK
path is saved. A manually selected valid x64 path still takes precedence. The
client includes serialized lifecycle coordination, sample-counter gap auditing,
bounded display history, raw chunk persistence, and a bounded analysis bridge.
It never writes the backend SQLite database directly. After a completed raw
recording is closed, the client may register that recording through the
backend HTTP contract and submit an offline official Analysis Run; all
scientific validation, execution, artifacts, and provenance remain backend
owned.

## Official Algorithm Workspace

WPF is the product client for the offline official-algorithm workflow. The
algorithm workspace loads the authoritative catalog from `GET /api/algorithms`,
registers a completed local WPF recording with
`POST /api/recordings/register-wpf`, and submits a traceable static Run through
`POST /api/runs`. It polls the Run until a terminal state and displays the
backend's structured error, scientific version, requested/actual range, and
provenance. PSD/STFT previews come from
`GET /api/runs/{run_id}/structured-preview`; the desktop does not perform FFT,
PSD, dB conversion, interpolation, integration, or quality classification.

The Vue application remains a validation and contract-inspection platform. It
is not the product owner for the WPF acquisition or review workflow, and new
official scientific behavior must be implemented and versioned in the backend
before either client displays it.

For a static PSD Run in WPF, open the algorithm workspace, select a project
and one of its completed recordings, then register the record. The backend
returns the actual channel list and duration. Select PSD, a channel, and the
requested start/end seconds before starting analysis. Each accepted algorithm
uses its own parameter and result adapter. The result plot shows
the bounded backend frequency/PSD preview with its declared units; unavailable
values are left blank rather than drawn as zero.

For dynamic analysis, select “分析方式” in the shared parameter card: “全量分析”
computes the selected interval, while “按需分析” loads the cursor's target window.
All nine accepted modules offer both choices without environment commands;
the initial choice remains full analysis. Review's PSD adapter uses the same
availability policy. Per-module environment overrides remain for maintenance.

## Build And Test

```powershell
cd desktop-client
dotnet build BrainPlatform.Desktop.slnx -c Release
dotnet test BrainPlatform.Desktop.slnx -c Release
dotnet run --project BrainPlatform.Desktop\BrainPlatform.Desktop.csproj
```

Start the backend from the repository root with `backend\start_backend.bat`.
It runs the source backend on port `8000` in its own console. Start the WPF
client separately with `dotnet run --project
desktop-client\BrainPlatform.Desktop\BrainPlatform.Desktop.csproj` or from the
IDE.

## Windows Release Package

Build a portable desktop release from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\build_release.ps1
```

The output contains `BrainPlatform.Desktop.exe` and
`backend\brain_backend.exe`. Launch the WPF executable; it starts the backend,
waits for `/api/health`, and stops only the backend it started. Scientific data
is stored in `%LOCALAPPDATA%\BrainPlatform\storage`, so installation does not
require write access beside the executable. The package is portable for now;
an installer can be added after manual acceptance.

## Acquisition Core Boundaries

- The hardware adapter must return the actual opened-stream channel list,
  sampling rate, and sample-counter channel. None are guessed from channel
  order or a default montage. Referenced montage profiles are immutable versions;
  after a recording references one, the editor is read-only and “复制为新配置”
  creates the next version. Review defaults to the acquisition snapshot and
  permits only same-channel-ID, scientifically compatible display montages.
  Preset deletion is blocked by existing recordings, including recoverable
  `.trash/recordings`. Permanent removal of the last discoverable reference allows
  deleting the preset (even archived); the historical ID remains non-editable and
  cannot be reused. There is no automatic recording purge. Reopen the montage list
  to refresh permissions. Reference-index migration requires the new build;
  unreadable metadata or unavailable recording disks block verification. See
  [reference-release acceptance](../docs/validation/2026-10-10-montage-reference-release-report.md).
- Incoming batches are sample-major `float64` values. Each channel declares
  its unit: EEG is `V`, sample counter is `count`, and trigger is `code`. The
  raw writer persists batches before display buffering or analysis dispatch. It
  retains PC receive UTC ticks for transport diagnostics; scientific sample
  time remains the verified sample counter divided by sampling rate.
- Raw data is written in bounded `samples-XXXXXX.bin` chunks, with
  `manifest.json` and `audit.jsonl`. A display-buffer eviction is not a raw
  data loss; a counter gap is explicitly recorded instead of filled.
- The analysis bridge is intentionally a local bounded contract. A later
  Python service owns filtering, PSD, algorithms, scientific artifacts, and
  SQLite provenance. The desktop process must not write those records.

## ANT/eego Installation And Hardware Acceptance

The portable release carries the licensed vendor-published x64
`sdk\eego-SDK.dll`. The USB device driver is separate and still must be
installed on the target machine according to ANT instructions.

Configure all of the following before selecting the adapter:

- the absolute local x64 `eego-SDK.dll` path;
- an explicit reference range in V;
- an explicit bipolar range in V.

The adapter discovers actual device IDs, serials, sampling rates, ranges, and
native channel types. It never guesses electrode labels, channel count, sample
rate, ranges, trigger position, or counter position. The selected ranges and
the SDK version are written to the raw recording manifest.

Before using collected data for research, perform and retain a hardware
acceptance record on the target amplifier:

1. Confirm x64 load, device discovery, serial, firmware, and supported rates.
2. Open a stream and inspect its returned channel index/type order and units.
3. Confirm counter start, within-batch continuity, cross-batch continuity,
   overflow behavior, and reset behavior after reconnect.
4. Run sustained collection at the intended channel count and rate; inspect
   `audit.jsonl` for gaps, failures, and analysis-dispatch drops.
5. Exercise stop, USB disconnect, reconnect, process restart, and the required
   EEG-to-impedance transition. The current adapter implements EEG only.
6. Independently parse a completed raw recording using its manifest and verify
   that sample-major data and per-channel units are interpreted correctly.

Passing the desktop unit tests verifies adapter contracts only. It is not a
claim that a physical device or the combined acquisition system is validated.

## SciChart License

The waveform renderer uses SciChart WPF. A SciChart “trial expired” page is a
licensing state from SciChart, not an EEG acquisition, device, or algorithm
error. Replace the application startup license when the trial expires.

## Minimal Acquisition Workflow

The current acquisition page is intentionally operational rather than final UI.
It starts with no device, channel, waveform, impedance, or analysis result.
The usable order is:

1. On startup, a saved ANT SDK configuration automatically checks for a
   connected amplifier. The **Amplifier** tab lets an operator re-check the
   device when necessary.
2. The **Channel settings** tab shows the actual recognized EEG inputs and
   their configured labels. It does not invent labels from screen position.
3. The **导联** tab states the active raw hardware-reference path. It does
   not imply a software rereference or filter is active.
4. The operator selects a returned sampling rate. The client restores the last
   successful hardware range pair for the physical device, or derives a valid
   pair from the device-reported capabilities; ranges are not daily UI inputs.
5. Select **Start recording**. The main workspace renders the actual raw
   stream while the coordinator writes raw chunks.
6. Select **Stop recording**. Review `manifest.json` and `audit.jsonl` in the
   recording directory before considering any analysis handoff.

Changing the SDK file invalidates the connection test. Closing the desktop
application disposes an active acquisition runtime, which stops recording and
releases the stream.

## Deferred Hardware Work

Hardware acceptance must still verify the actual device lifecycle, channel
mapping, sample counter behavior, gap/drop audit, and raw chunk format before
adding backend ingestion APIs or displaying scientific analysis.
