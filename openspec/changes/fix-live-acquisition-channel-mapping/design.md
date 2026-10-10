# Live mapping ownership

## Evidence and cause

`AcquisitionWorkspaceViewModel.OnDeviceSessionPropertyChanged` handles both
Snapshot and SelectedDevice notifications by calling ApplySelectedDevice.
DeviceSessionManager publishes those notifications on runtime transitions even
when physical device facts are unchanged. ApplySelectedDevice calls
ChannelMappingViewModel.LoadDevice, which clears the active configuration and
loads legacy/default labels. WaveformDisplayFrameBuilder then resolves montage
labels against those overwritten live source rows.

Synthetic input CH2=20 microvolts and CH3=40 microvolts reproduces a missing
F88 output and an F7 output incorrectly reading CH2 after a status notification.
The control projection of identical bytes using the selected snapshot reads
CH3 correctly. This is not evidence of SDK channel reordering or corrupt raw
data, and hardware acceptance remains separate.

## Decision

Cache only the last device descriptor applied to acquisition setup, not a
second selected-device authority. Reuse DeviceSessionDiscoveryComparer to
compare complete device facts. Equivalent descriptors do not reset setup;
actual device/capability changes and null transitions still do. ChannelMapping
remains configured once from the selected montage snapshot before stream open.

Changing DeviceSessionManager notifications globally would affect unrelated
subscribers. Changing montage arithmetic or exchanging raw columns would mask
the cause and damage provenance. Neither is part of this repair.

## Testing

Regression tests distinguish CH3=F88 from CH5=F3, and relocate F7 from CH2 to
CH3 without duplicate labels. Verify labels, native indexes, stream metadata,
and numeric live trace values through preview/record/pause/resume/stop. Test
real descriptor changes, disconnect and new discovery separately. All samples
are synthetic and settings writes are forbidden in the test store.

## File-size review

The existing acquisition workspace exceeds 1000 lines. This repair adds one
descriptor cache and a small guard only; no additional state machine or DSP is
introduced. Register a split plan at the current module path: extract device
setup application/range selection and then recording-event workflows into
their existing ownership boundaries in a separate maintenance change. Do not
move unrelated lifecycle code while correcting signal identity.
