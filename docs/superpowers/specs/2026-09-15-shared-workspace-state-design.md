# Shared Workspace State Design

## Purpose

Make shared research-workspace facts have one owner while keeping component-only
visual interactions local. This reduces stale algorithm lists, mismatched Run
status, and old-recording results returning after a new recording is opened.

This work does not change EEG mathematics, units, analysis time semantics,
backend APIs, or the rule that scientific values are computed only in the
backend.

## Boundary

### Shared business facts

The App-level workspace composes the following state domains:

1. **Recording context** — recording identity, duration, sampling rate,
   source/display channels and normalized channel metadata.
2. **Viewer context** — playback position, page start, playback state and
   display-only montage state.
3. **Analysis-time context** — selected waveform range and committed static
   analysis range. Dynamic analysis windows remain algorithm configuration, not
   a replacement for these ranges.
4. **Algorithm catalog context** — user definitions, installed official
   catalog entries, latest published version identity, loading/error state and
   a single `refresh()` operation after create, publish, clone or delete.
5. **Run-result context** — UI index of a Run's identity, status, result
   summary and error. It never owns waveform arrays, PSD arrays or artifact
   matrices; those remain backend-backed data.
6. **Recording epoch** — a monotonically advancing identity used to invalidate
   UI state and ignore stale asynchronous responses when a recording changes.

### Component-local UI state

The following stay in the nearest component because they are not research
facts and must not affect another view:

- modal and menu visibility;
- form drafts before save;
- hover/cursor/zoom/scroll position;
- expanded/collapsed raw PSD and other details;
- a request's local spinner where no other view consumes its progress.

## Data flow

```text
Recording import / new session
  -> recording epoch advances
  -> shared recording/viewer/time/algorithm-run state resets
  -> algorithm catalog remains reusable but is refreshed on explicit mutation

Definition create / publish / clone / delete
  -> backend mutation succeeds
  -> AlgorithmCatalogContext.refresh()
  -> algorithm library and waveform-algorithm selector consume the same list

Run created / polled
  -> RunResultContext indexes status + summary by run id and definition id
  -> main result card, debug workbench and result drawer read the same entry
  -> raw numerical evidence stays in backend response/artifact, never recomputed
```

## Design decisions

### Algorithm catalog is the next implementation slice

`AlgorithmDefinitionWorkbench` and `AlgorithmDisplayWorkspace` currently load
definition lists and version lists separately. `algorithmDefinitionsEpoch` is a
manual refresh signal in `App.vue`. Replace it with an `AlgorithmCatalogContext`
that owns the fetch, cache and refresh lifecycle. Official catalog failure must
not hide user definitions.

This is first because it is bounded, fixes visible stale-list behavior, and
creates a clean interface used by later Run/result consolidation.

### Run results are shared metadata, not a front-end science cache

The existing algorithm-workspace result state and debug workbench already share
the latest completed Run. Extend this only after catalog centralization so the
results drawer can use the same Run index. The shared record contains IDs,
statuses, summaries and errors. Large arrays continue to be fetched by purpose
specific backend endpoints.

### Current analysis channel is a default, not a forced global switch

A shared `currentAnalysisChannel` may supply the initial choice for PSD,
spectrogram and algorithm dialogs. Each surface can temporarily override it.
Changing a selector must never rewrite a completed Run or silently alter a
different chart's already-requested analysis.

### Recording epochs protect asynchronous work

All recording-scoped async work carries the current recording epoch or request
identity. A response from an old recording is discarded. Changing files closes
the algorithm debug workbench and clears Run results; it must not delete the
algorithm catalog itself.

## Error handling

- Catalog errors are explicit and scoped: official-catalog failure does not
  remove user definitions; user-definition failure is shown where selection is
  unavailable.
- A stale request produces no toast and writes no state.
- A failed or gate-failed Run remains inspectable as its own structured state;
  it is never converted to a zero result.
- Missing channels remain a backend-defined unavailable reason, not a front-end
  positional channel guess.

## Test strategy

Each implementation slice uses test-first behavior tests:

1. one catalog refresh updates every catalog consumer and removes deleted
   definitions from selection;
2. catalog failure preserves the independent official/user catalog as applicable;
3. a Run status transition updates the main chart/debug/result surface from one
   shared record;
4. recording-epoch change rejects a late old-recording response and resets
   recording-scoped Run/debug state;
5. changing the default analysis channel does not modify previously completed
   result evidence.

Full frontend Vitest, TypeScript production build, and `git diff --check` are
required before each implementation commit.

## Out of scope

- global storage of PSD, spectrogram or raw EEG matrices;
- mathematical or algorithm-contract changes;
- changing viewer reference into analysis reference;
- a generic global store for every control;
- multi-user synchronization or server-side collaboration.
