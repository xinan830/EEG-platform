# Algorithm runtime reset progress

Date: 2026-09-16

## Completed in this checkpoint

- Added typed runtime contracts, structured failures, deterministic windows, and
  duplicate/unknown algorithm guards.
- Added canonical IAPF and `official-theta-beta-v2` modules. Both preserve the
  selected raw channel; Theta/Beta no longer requires global Fz/Pz/Oz mapping.
- Routed new official Runs through the runtime and preserved Run/Artifact
  traceability.
- Added unified `GET /api/algorithms` and connected the frontend official
  catalog loader to it.
- Added baseline/call-graph audit documenting remaining mapping consumers.

## Verification

- Backend: `218 passed, 2 warnings`.
- Frontend Vitest: `106 passed`.
- Frontend `vue-tsc --noEmit`: passed when using the repository's installed
  dependency directory.
- Frontend production build: passed; existing bundle-size warning remains.
- OpenSpec change validation: passed before implementation; the change is not
  archived because legacy mapping and user-definition runtime cutover remain.

## Remaining cutover gate

Global `ChannelMapping` still has active consumers in legacy analysis,
playback, offline analysis, and the waveform view. It must not be deleted until
those paths either move to explicit per-run channel inputs or are deliberately
removed with historical-read tests. Current official IAPF and Theta/Beta do not
read this mapping.
