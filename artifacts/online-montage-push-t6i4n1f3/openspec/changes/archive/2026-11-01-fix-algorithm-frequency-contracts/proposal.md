# Fix algorithm frequency validation and snapshots

## Why
Real-recording audit confirmed accepted peak/ratio bands outside the frozen
1-30 Hz spectrum, silent partial peak searches, and execution snapshots that
confuse a search band with preprocessing or ignore explicit notch settings.

## What Changes
- Declare and enforce the existing 1-30 Hz domain for peak frequency and band
  ratio before loading or queueing; no scientific range expansion.
- Reject a peak result when its input axis does not cover the requested band.
- Make snapshot filter settings explicit module inputs, rather than guessing
  from identically named configuration fields.
- Persist actual PSD/STFT notch selections including disabled and 60 Hz.
- Change affected implementation identities, not scientific versions; keep
  historical results readable and preserve the pre-fix audit report.

## Impact
Backend algorithm modules, parameter schemas, provenance, diagnostic scripts
and regression tests. No formula, preprocessing, quality threshold, WPF layout,
raw data or historical Run mutation. Existing out-of-domain requests now fail
validation explicitly instead of returning incomplete or unavailable results.
