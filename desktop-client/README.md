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
device. An optional ANT/eego adapter can load a locally installed vendor DLL
from an explicit x64 path; it is never bundled or selected by default. The
client includes serialized lifecycle coordination, sample-counter gap auditing,
bounded display history, raw chunk persistence, and a bounded analysis bridge.
It does not create AnalysisRuns or access the backend SQLite database.

## Build And Test

```powershell
cd desktop-client
dotnet build BrainPlatform.Desktop.slnx -c Release
dotnet test BrainPlatform.Desktop.slnx -c Release
dotnet run --project BrainPlatform.Desktop\BrainPlatform.Desktop.csproj
```

Start the existing Python backend first with `start_brain_platform.bat` or the
backend development command. The startup script and current web client use port
`8000`; earlier README text naming ports `8001` or `5174` was obsolete.

## Acquisition Core Boundaries

- The hardware adapter must return the actual opened-stream channel list,
  sampling rate, and sample-counter channel. None are guessed from channel
  order or a default montage.
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

The ANT adapter uses the vendor-published C ABI in a user-installed x64
`eego-SDK.dll`. Obtain the DLL, driver, and device manual from ANT under the
applicable license. Do not copy any vendor DLL, SDK header, driver, or example
project into this repository or the desktop build output.

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
