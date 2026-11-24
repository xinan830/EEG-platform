## Why

The first desktop acquisition integration is ANT/eego. The common acquisition
pipeline already has an adapter interface, but the configured runtime still
creates the ANT adapter directly. Adding a second vendor in that form would
spread vendor decisions through runtime lifecycle code and the future setup UI.

## What Changes

- Add an explicit registry of installed acquisition-device drivers.
- Define a generic driver configuration envelope whose vendor settings are
  interpreted only by the selected driver.
- Register ANT/eego as the first driver and remove ANT references from the
  configured acquisition runtime.
- Require every driver adapter to expose asynchronous disposal.

## Compatibility Impact

ANT/eego remains the installed default and its existing setup fields continue
to create the same `AntEegoAdapterOptions`. Raw recording, filtering,
time-axis, and analysis contracts are unchanged.

## Non-Goals

- This does not claim support for another vendor or implement a plugin market.
- This does not invent a generic settings UI for unknown SDK parameters.
- This does not relax the hardware sample-counter requirement.
