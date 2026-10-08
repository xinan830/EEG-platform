# Execution Boundaries

Processed windows include quality-rejected results but exclude a window that
throws an unhandled exception. Cancellation is cooperative, not instantaneous
thread termination. No incomplete result is published as a completed artifact.

Explicit idempotency keys retain their original Run across terminal states;
changing scientific configuration under the same key is invalid. Without such
a key, identical active configuration/cache identity and project/batch context
is merged inside a SQLite write transaction. Terminal Runs do not prevent retry.
Repeated retry requests also merge with identical active work; the existing Run's
original parent lineage is retained instead of creating another execution.

Static interactive Runs use the interactive lane; dynamic Runs and project batch
Runs use the background/batch lane. Each lane has one worker. A static Run can
complete while a dynamic or batch Run is busy; two dynamic Runs remain serialized.
The pool never exceeds two workers; threadpoolctl limits native
BLAS/OpenMP threads for the lifetime of the pool. This bounds concurrency, not
all output-array memory: large result artifacts still scale with result size.

Continuous preprocessing retains forward/backward SciPy filter state between
blocks and uses the same odd extension and initial conditions as filtfilt.
Mapped raw/intermediate files belong to one reader or preprocessing cache entry;
they are closed before temporary-directory cleanup. LRU entries have a byte
budget for resident arrays (128 MiB) as well as an entry budget (two). Disk maps
are not counted as resident arrays; OS page caching, output arrays and temporary
disk capacity remain additional costs. Cache eviction drops its reference, not
the live consumer's lease. NaN gaps remain NaN and are not synthesized.

Cancellation accepted before the terminal-state transaction wins over completion.
A newly written but unpublished artifact is discarded if cancellation wins during
artifact compression. A native call already in progress must return before the
next cooperative check; cancellation is not a hard interrupt.

Central edits: Run contracts/repository/migration, queue and worker, recording
reader/preprocessing boundary, WPF shared execution context/card/coordinator.
Existing large catalog ViewModel is reduced by moving Run orchestration into
its own partial file; no chart or algorithm-specific matrix ownership moves.
