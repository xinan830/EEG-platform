# Tasks

- [x] Add runnable Brainbeat manifest, config, compute, and runner.
- [x] Register Brainbeat in the backend built-in registry and official catalog.
- [x] Add backend boundary, numerical, quality, and dynamic-window tests.
- [x] Add WPF Brainbeat handler, detail state, and dedicated detail view.
- [x] Add WPF parameter serialization for the explicit secondary channel.
- [x] Run backend tests, WPF build/tests, OpenSpec strict validation, and diff check.
- [x] Manually verify one static and one dynamic Brainbeat Run in WPF.

Manual acceptance: the user confirmed all outstanding manual checks passed
on 2026-10-18. This is user-reported acceptance, not an agent-observed GUI or
hardware test; no additional recording, duration, or measurement is inferred.

## ADDED Requirements

### Requirement: Runnable independent-window Brainbeat
The backend MUST calculate Brainbeat independently for each static or dynamic
window as relative Fz Theta power divided by relative Pz Alpha power using the
window's IAPF. It MUST NOT use the historical realtime EMA state.

### Requirement: Explicit source channels
The Run MUST persist distinct explicit Fz and Pz source channels and reject a
missing or identical pair.

### Requirement: Unavailable semantics
If source quality or IAPF fails, the result MUST remain null and include a
structured failure reason and quality evidence.
