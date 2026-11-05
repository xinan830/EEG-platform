# PSD and Theta/Beta Change Validation

Date: 2026-11-05

## Scope

- `psd-band-topomap`: canonical PSD band-mean-density fields for Review topomaps, preserving `V^2/Hz` and converting once for display.
- `standardize-official-theta-beta`: standard and IAF-relative frequency modes, complete dynamic windows, and WPF/Review mode selection.
- Main OpenSpec requirements were synchronized from both changes before archive.

## Validation

- `openspec validate --specs`: 83 passed, 0 failed.
- `openspec validate psd-band-topomap --strict`: passed.
- `openspec validate standardize-official-theta-beta --strict`: passed.
- Focused backend algorithm and PSD/topomap regressions: 105 passed.
- `backend/tests/test_theta_beta_v2.py`: 7 passed.
- Focused WPF PSD Review and Theta/Beta tests: 20 passed using isolated output because the running desktop process locks the default executable.

The .NET build reported existing NU1701 compatibility warnings and one existing xUnit collection-size analyzer warning. No full repository test suite or live WPF playback acceptance was run.
