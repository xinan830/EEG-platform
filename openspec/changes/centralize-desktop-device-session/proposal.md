## Why

Desktop device discovery, selected-device state, and stream state currently
live primarily in the acquisition page view model. Settings pages therefore
depend on that view model indirectly and could otherwise grow their own device
state. The workstation needs one hardware-fact source before channel profiles,
montages, projects, and live-monitoring workflows expand.

## What Changes

- Add a desktop `DeviceSessionManager` that owns discovered device descriptors,
  the selected physical device, a capability fingerprint, and a read-only
  connection/stream snapshot.
- Route device discovery and driver reconfiguration through that manager.
- Make acquisition and channel-configuration view models subscribe to the
  same session rather than retain separate selected-device truth.
- Expose a device as connected only after successful discovery; map runtime
  stream and fault transitions into the same snapshot.
- **BREAKING (internal desktop composition):** replace direct selected-device
  ownership by `AcquisitionWorkspaceViewModel` with session ownership.

## Capabilities

### New Capabilities
- `desktop/device-session`: Single-source device identity, capability, and
  connection/stream state for the WPF workstation.

### Modified Capabilities
- `desktop/acquisition-core`: Discovery and runtime transitions update the
  desktop device session without changing raw EEG persistence or time rules.

## Impact

Desktop C# composition, acquisition setup bindings, channel-configuration
setup flow, and desktop tests change. Vendor SDK boundaries, raw EEG values,
recording format, backend APIs, and scientific algorithms do not change.

## Scientific-Contract Impact

None. This change only centralizes hardware identity and lifecycle metadata;
it does not alter raw sample values, units, timing, filtering, or analysis.

## Migration Strategy

Existing saved channel profiles remain valid. They are evaluated against the
session-provided physical capability signature after discovery. No recording
or raw file is rewritten.

## Non-Goals

- Separate real-time streaming from recording persistence.
- Add project or montage persistence.
- Add polling that claims immediate idle USB-disconnect detection when the
  SDK provides no such notification.
- Add support for a second vendor driver.
