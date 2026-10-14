# Preserve review waveform across source chunks

## Why
The review chart inserts NaN X and Y between every source chunk, including
contiguous ten-second filter chunks. X is recording time, not a gap flag.
The reported REF trace disappears over part of a scrolling viewport although
the immutable source and cached filtered values are finite.

## What changes
- Verify the reported recording and reproduce chart input/rendering separately.
- Keep chart X finite and ordered; only real counter gaps or invalid source
  values break a trace with NaN Y.
- Keep bounded temporal min/max display decimation and preserve real gaps.
- Add regression coverage for source chunk joins and centered scrolling.

## Scope and compatibility
Only review waveform display projection changes. No raw file, filter contract,
montage mapping, scientific formula, analysis Run or license is changed.
No cache purge or migration is needed because filtered source arrays are unchanged.
This defect repair is isolated from the in-progress dynamic-analysis work.
