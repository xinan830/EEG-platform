# Verification — 2026-11-27

## Scope and result
Startup now disables filter editing using the existing preparation state, guards the setters and drains older filter updates before applying the validated snapshot. The settings-save regression checks the actual bridge settings, saved low-pass value, UI value and success text. Other inputs are rechecked after device configuration and persistence, before stream opening. No new dependency, scientific formula, raw recording format, time/unit/channel convention or backend behavior is introduced.

## Evidence
- Before the implementation, the new settings-wait filter guard test failed, and changing sampling rate during settings persistence incorrectly completed startup instead of reporting a changed-input error.
- Focused Release tests: 14 passed (workspace and channel collaborators plus pending-filter ownership).
- Broader Release tests, with no exclusions: 514 passed, 1 failed, 0 skipped (515 total). The only failure remains `LiveMonitoringViewModelTests.ConfigureChannels_DoesNotAutoDisplayUnnamedPhysicalInputs`, line 273: expected Fp1, actual empty. The same failure was observed before this fix; neither that test nor LiveMonitoringViewModel is changed here.
- Backend gate: 60 passed, 1827 deselected, 2 existing deprecation warnings.
- File-size policy, `git diff --check` and `openspec validate fix-acquisition-startup-snapshot --strict`: passed. The acquisition root is 565 lines, collaborators are 355/348/330 lines, and viewer.js is 602 lines.

## Commands
```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AcquisitionWorkspaceCollaboratorTests|FullyQualifiedName~ChannelDraftCollaboratorTests|FullyQualifiedName~LiveDisplayStartupFilterTests'
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Acquisition|FullyQualifiedName~Channels|FullyQualifiedName~ChannelConfiguration|FullyQualifiedName~Montage|FullyQualifiedName~Electrode'
backend/.venv/Scripts/python.exe -m pytest -c backend/pyproject.toml backend/tests -m gate -q
backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py
git diff --check -- desktop-client docs openspec/changes/fix-acquisition-startup-snapshot
openspec validate fix-acquisition-startup-snapshot --strict
```

## Limits
No full desktop/backend suite or real-device acquisition acceptance. Settings persistence may finish before a changed non-filter input is detected; the validated snapshot can be stored, but no stream is opened. Existing unrelated workspace edits remain in place. No commit or push was requested.
