## Why

Official EEG metrics predate the typed executor. They must be migrated through
shadow comparison, not replaced by a graph merely because it validates.

## What Changes

Add immutable official-definition metadata and backend-only shadow reports for
RBP, Theta/Beta, FAA, BrainBeat and IAPF. Every report stores old/new values,
maximum errors, pass rate, configuration digest and environment. No public
result switches until each metric has synthetic and local-data evidence.
