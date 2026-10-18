# Fix Long Recording PSD Preview

## Why

The PSD desktop loader requests only 20,000 numeric cells. A completed
149.982-second dynamic PSD with 146 rows contains 29,689 numeric cells, so
the result endpoint rejects the preview and the desktop loses both the
spectrum, band shares, and playback windows despite successful computation.

## What Changes

- Use the existing endpoint's bounded 1,000,000-cell budget for PSD, matching
  the STFT desktop loader. Do not remove backend memory protection.
- Verify static and dynamic backend-authored band shares, seeking and rejected
  windows, including a regression exceeding the previous 20,000-cell budget.
- Update obsolete STFT test calls to the current STFT-owned loading method.

## Impact

Presentation loading only; no scientific version, calculation, filter,
quality threshold, artifact, or Run identity changes. Larger results remain
bounded; arbitrary-duration preview paging is outside this fix.
