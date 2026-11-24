## Decision

Create an x64 `net10.0-windows` WPF shell in `desktop-client/BrainPlatform.Desktop`. The shell calls the existing localhost `GET /api/health` endpoint with a bounded timeout and displays the result. It does not start the Python process, reference the ANT/eego SDK, invent EEG samples, or read/write the backend SQLite database.

The desktop state is divided into three independent facts:

- backend availability: whether the local scientific engine answers the health contract;
- device availability: whether an installed acquisition adapter reports a physical device;
- acquisition state: a finite local UI state, initially `NotConfigured`.

This avoids treating a healthy backend as a connected amplifier, or a device UI state as evidence of a scientific result.

## Architecture

```text
WPF views
  -> DesktopViewModel
  -> BackendHealthClient (HTTP only)
  -> existing FastAPI GET /api/health

AcquisitionCoordinator
  -> IAcquisitionDeviceAdapter (unavailable by default; ANT later)
  -> sample-counter continuity audit
  -> local raw chunk writer (sample-major float64/V)
  -> bounded display Ring Buffer
  -> bounded analysis bridge (no scientific SQLite ownership)
```

The device adapter is represented by a local interface and unavailable implementation. The acquisition core writes received data before display buffering or analysis handoff, and records a sample-counter gap instead of manufacturing samples. The first real adapter change must separately validate vendor wrapper support, actual stream channel types, sample counter semantics, trigger behavior, drop/gap behavior, raw format, throughput, and stop/reconnect lifecycle.

## Alternatives

- Embed the existing Vue UI in a desktop webview: deferred. It does not establish the device and acquisition ownership boundary needed for hardware work.
- Reference ANT/eego DLLs now: rejected. The wrapper capability and real-device behavior have not been accepted, and third-party SDK material must not be copied or committed without a licensing decision.
- Add live ingestion endpoints now: rejected. The desktop now has typed batch, time, gap, raw-persistence, and back-pressure contracts, but no real hardware contract or validated Python live-ingestion API.

## Migration And Rollback

The project is additive and uses only the existing health endpoint. Deleting `desktop-client/` rolls back the shell without changing existing desktop data, APIs, or scientific results.

## Verification

- Build the WPF project in Release configuration.
- Unit-test backend health response mapping and unavailable device state.
- Verify the shell reports a reachable local backend and a failed or unavailable backend without crashing.
- Validate the OpenSpec change before archive.
