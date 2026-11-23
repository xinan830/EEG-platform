# Design

## State ownership

`SelectedMontageProfile` remains the acquisition/recording selection made before preview. `LiveDisplayMontageProfile` is a separate transient property owned by `AcquisitionWorkspaceViewModel`; it is initialized from the selected profile when preview setup succeeds and is never serialized into the recording configuration.

The runtime adapter options continue to receive the selected profile snapshot once in `StartPreviewCoreAsync`. A display switch calls only `LiveMonitor.ConfigureMontage`, so raw batches, writer input, sample counters, algorithm requests and review provenance are untouched.

## Compatibility and state rules

- A display profile must be available for acquisition and have the same `ChannelConfigurationFingerprint` as the selected recording profile.
- `CanSwitchLiveDisplayMontage` is true only in `Previewing`.
- Invalid or late binding writes are rejected and the binding is refreshed to the previous value.
- The data information panel labels both profiles explicitly.

## UI

Add one compact ComboBox beside the existing data and algorithm controls. Its tooltip states that it affects only the live waveform display. Disable it during recording, pause and all non-preview states.

## Verification

Test the display projection change independently of runtime storage, then run the isolated desktop build and focused tests because a desktop process may hold the normal Debug output files.
