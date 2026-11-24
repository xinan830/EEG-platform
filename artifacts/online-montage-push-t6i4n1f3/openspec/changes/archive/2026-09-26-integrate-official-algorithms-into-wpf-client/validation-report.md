# Validation Report

Date: 2026-09-26

## Automated checks

- Backend: `python -m pytest -q` -> 289 passed.
- WPF: `dotnet test BrainPlatform.Desktop.slnx -c Release --no-restore -p:UseAppHost=false` -> 262 passed.
- WPF Release build: 0 warnings, 0 errors.
- WPF cold startup: application remained running without the reported XAML resource exception; process was then stopped deliberately.
- OpenSpec: strict validation passed.
- Formatting: `git diff --check` passed.

## Covered contract paths

- WPF recording registration request and response decoding.
- Authoritative algorithm catalog decoding.
- Bounded Run polling through a terminal state.
- Structured backend error decoding.
- Null result/provenance preservation for quality-gated Runs.
- Backend-owned PSD/STFT preview axes, units, arrays, and window states.

## Manual WPF acceptance

The user ran static PSD from a completed local WPF recording in the desktop
algorithm list. The screenshot showed registration and Run completion, Run ID
`af3c739fc3d54ea6966b096579f22d77`, channel `Fp1`, requested and actual
range `0–63.318 s`, frequency axis in `Hz`, and PSD values in `V^2/Hz`.
A read-only backend query independently confirmed `completed`, scientific
version `offline-spectral-v3`, matching ranges, and a structured result.

This is an end-to-end workflow acceptance, not an independent numerical PSD
validation or a statement that the physical EEG signal passed a separate
hardware/scientific acceptance protocol.
