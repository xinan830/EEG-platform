## Why

The current application is an offline web workstation. A Windows desktop shell is needed before live ANT/eego acquisition can be introduced, but hardware integration must not destabilize the validated Python research engine or imply that live acquisition already exists.

## What Changes

- Add an independent x64 WPF desktop-client solution under `desktop-client/`.
- Provide a local backend connection status using the existing read-only `GET /api/health` endpoint.
- Provide explicit device, acquisition, and scientific-engine states, initially showing that no live device adapter is installed.
- Add a non-SDK acquisition core with a serialized stream lifecycle, typed device/channel/batch contracts, bounded display history, sample-counter continuity audit, local raw chunk persistence, and a bounded future-analysis bridge.
- Establish typed desktop service boundaries for backend health checks and future device ownership without adding a device SDK reference.
- Add desktop build instructions and preserve the existing Vue frontend and FastAPI interfaces.

## Capabilities

### New Capabilities
- `desktop/wpf-shell`: A local Windows shell that reports backend and acquisition readiness without performing scientific computation or device acquisition.

### Modified Capabilities

None.

## Impact

Adds a standalone .NET 10 WPF project and desktop documentation. No backend API, database, artifact, algorithm, or existing web frontend behavior changes. The default adapter still cannot acquire EEG; no vendor SDK is referenced.
