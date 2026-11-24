# Harden Analysis Run Execution

## Why

Offline dynamic Runs expose no measured work progress, observe cancellation only
after full execution, and allow identical active work to enter the queue. A
single worker also lets batch work block interactive analysis. Full-recording
raw and preprocessing arrays impose unbounded resident memory.

## Scope

- Persist processed/total windows and truthful execution stages.
- Cooperative cancellation between windows and preprocessing blocks.
- Atomic active-configuration deduplication, separate from explicit request keys.
- At most two workers, one interactive lane and one batch lane; bound native
  numerical threads across both lanes.
- Read recordings by sample blocks and store continuous preprocessing in owned
  temporary memory maps without changing the existing zero-phase filter.
- Bind shared WPF progress and cancellation; preserve playback state.

## Non-goals

No EEG formula change, acquisition rendering change, removal of licence markings,
or hard interruption of native numerical calls. FAA science is a separate change.
