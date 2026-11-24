# Change 03 验证报告

`add-algorithm-definition-engine` adds SQLite schema v4, immutable Definition
versions, a closed graph executor, constrained parameter schemas, restricted
formula parsing and Definition API resources. It does not change
`offline-spectral-v3` or any existing user-facing analysis result path.

Evidence: migration and repository tests prove SemVer/digest duplication is
rejected and publish is idempotent; graph tests cover unknown nodes, cycles,
missing inputs, incompatible units, formula attacks and division by zero; API
tests cover create/version/publish/list/clone and a stable parameter error.

Commands passed on 2026-09-14:

```powershell
openspec validate add-algorithm-definition-engine --strict --no-interactive
cd backend; .\.venv\Scripts\python.exe -m pytest -q  # 133 passed
cd frontend; npx vue-tsc --noEmit; npm test -- --run; npm run build
git diff --check
```

The two Python warnings are third-party FastAPI/Starlette deprecations. This is
engineering verification only, not clinical validation. No real EEG, SQLite,
artifact or personal identity data was committed.
