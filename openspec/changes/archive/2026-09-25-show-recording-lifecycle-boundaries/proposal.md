# Show Recording Lifecycle Boundaries

## Why

Recording start, pause, resume, and stop must remain traceable to persisted
samples. Without explicit boundaries, a pause gap or review marker can be
misread as continuous EEG data.

## What Changes

Persist recording lifecycle boundaries beside the raw recording audit log and
render them in acquisition and review views. Preserve sample-counter gaps as
blank regions; do not fabricate samples. Use a light review grid so boundary
markers remain legible.
