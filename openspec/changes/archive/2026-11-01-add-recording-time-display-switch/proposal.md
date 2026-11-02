# Recording Time Display Switch

## Why

Review currently labels recording-relative X coordinates as acquisition clock
time, while algorithm pages show seconds from the recording origin. Users need
a common display choice without changing the scientific time coordinate.

## What Changes

- Add shared `记录时间 / 采集时间` display controls and formatting in WPF.
- Share the session preference between review and algorithm details.
- Format time axes, navigation labels, cursor and result ranges from the same
  recording origin. Show both time representations at the playback cursor.
- Carry the existing manifest start timestamp through the local recording row;
  never substitute file creation time or parse a localized display string.

## Impact And Non-Goals

No backend request, raw file, numerical result, sample rounding, gap, filter,
channel or quality change. Range inputs remain recording-relative seconds.
No clock-time input, external-clock synchronization or persistent preference.
Old local rows without start metadata retain relative time; migration is
additive and rollback removes presentation only.
