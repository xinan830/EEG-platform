# Add WPF static Theta/Beta workflow

## Why

The backend exposes the official individualized Theta/Beta ratio, but WPF
does not yet expose it in the static algorithm workflow.

## Scope

Support one registered channel and an explicit static time range. The backend
owns the IAPF dependency and all spectral calculations; dynamic Theta/Beta
remains outside this change.
