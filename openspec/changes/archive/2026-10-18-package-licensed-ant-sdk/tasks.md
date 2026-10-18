## Implementation

- [x] Copy the licensed x64 SDK during portable release packaging.
- [x] Resolve bundled SDK as a fallback in WPF.
- [x] Document driver and SDK behavior.
- [x] Run build, package smoke test, strict OpenSpec validation and diff check.
  Evidence: `build_release.ps1` completed on 2026-10-18; the backend health
  smoke test passed; the portable output contains `BrainPlatform.Desktop.exe`,
  `backend\brain_backend.exe`, and `sdk\eego-SDK.dll`. Strict OpenSpec
  validation and `git diff --check` passed. The publish emitted existing
  NU1701/NETSDK1057 compatibility warnings for legacy OpenTK/SkiaSharp packages
  and the preview .NET SDK; no build errors occurred.
