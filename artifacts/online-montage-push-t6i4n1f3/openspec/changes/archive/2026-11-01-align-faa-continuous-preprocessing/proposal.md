# Align FAA Continuous Preprocessing

## Why

FAA provenance declares continuous preprocessing before Epoching but dynamic
execution filters each trailing window independently. Removing these repeated
filter boundaries changes numerical results and requires a new scientific identity.

## Scope

Use the whole recording's selected raw pair, zero-phase notch then bandpass,
slice requested windows, then per-2-second paired-Epoch mean removal and FFT.
Preserve threshold, minimum clean Epochs, Alpha band, FFT and log-difference.
Publish official-faa-v2 / faa-runtime-v2 and Definition 2.0.0. Historical v1 Run
summaries and artifacts remain readable; v1 is not silently re-executed as v2.
