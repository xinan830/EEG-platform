# Design: Scientific Backend Boundaries

## Current-state facts

The repository currently has a working Runtime and a working set of scientific
paths. The following are active responsibilities, not merely obsolete code:

| Current area | Observed responsibility | Target ownership |
| --- | --- | --- |
| `algorithm_runtime/` | registry, Runtime contracts, parameter schema, generic dispatch, dynamic-frame scheduling | retain as execution platform |
| `algorithms/` | official module manifests/configs/runners; a user-definition Runtime adapter | official modules only; user adapter retired |
| `eeg_core/spectral.py` | filtering, Welch PSD, band integration, STFT-like spectrogram math | `scientific/primitives` and `scientific/preprocessing` |
| `eeg_core/quality.py` | spectral quality checks | `scientific/quality` |
| `eeg_core/primitives/` | typed safe graph primitives for the historical definition engine | retained only as legacy/read-only until its governance migration |
| `eeg_core/official_algorithms/` | active calculators, catalog compatibility, validation references | active calculators migrate to `algorithms`; validation references move or remain explicitly test-only |
| `services/spectral_analysis.py` | recording-backed orchestration, cache use, response envelope; imports science directly | orchestration only |
| `services/*_repository.py` | SQLite repositories | `persistence/repositories` |

`eeg_core` therefore cannot be mass-moved to `legacy` at the start. Its files
must be classified first as active scientific authority, explicit compatibility
adapter, test/reference implementation, or retired code.

## Target ownership

```text
app/
  api/                    request parsing, response/error mapping
  bootstrap/              composition root for concrete built-in registration
  models/                 passive cross-boundary contracts only
  algorithm_runtime/      registry, module contract, parameter validation,
                          static/dynamic dispatch and scheduling only
  scientific/
    contracts/            typed signal, range, unit, quality, output contracts
    preprocessing/        reference, filter, notch, detrend, artifact mask
    primitives/           PSD, band power, peak frequency, STFT
    quality/              integrity, signal-quality, shared reasons/metrics
  algorithms/
    <official-id>/        manifest, config, runner, algorithm-specific science,
                          algorithm-specific validity rules
  services/               recording use cases, queue, cache coordination,
                          Run/artifact workflow; no scientific formula
  persistence/
    migrations/           SQLite schema migration
    repositories/         SQL access only
  legacy/                 compatibility facades and explicitly retired paths
```

The target does not require every concept to be a directory immediately. For
example, a small `scientific/contracts.py` may remain one file until it exceeds
the project size policy. The authoritative responsibility matters more than
the nesting depth.

## Allowed dependency direction

```text
api -> services -> algorithm_runtime
api -> services -> scientific       (primitive-only use cases)
bootstrap -> algorithm_runtime      (registry/runtime wiring)
bootstrap -> algorithms             (concrete built-in modules)
algorithms -> scientific
api -> services -> persistence
services -> models
algorithm_runtime -> models
algorithms -> models
scientific -> models
persistence -> models
legacy -> target modules only
tests/scripts -> any production module as required for verification
```

`algorithm_runtime` MUST NOT import concrete algorithm packages. The
`bootstrap` composition root is the only production boundary allowed to import
both `algorithm_runtime` and concrete modules under `algorithms`; it registers
those modules through the Runtime's public registration contract. This keeps
the generic Runtime reusable and prevents a hidden built-in catalog inside it.

Services have two intentionally different call paths:

- An official algorithm Run uses `services -> algorithm_runtime -> algorithms ->
  scientific`.
- A use case that needs only a reusable primitive uses
  `services -> scientific` and MUST NOT manufacture an algorithm Run or bypass
  the primitive contract with private formula code.

Forbidden production dependencies:

- `scientific` MUST NOT import FastAPI, HTTP request objects, service objects,
  SQLite connections, artifact stores, or UI/display state.
- `algorithms` MUST NOT import `services` or `api`.
- `algorithm_runtime` MUST NOT import a concrete algorithm package, including
  through a built-in catalog or convenience re-export.
- `services` MUST NOT implement or duplicate FFT, Welch, filtering, band
  integration, peak detection, or algorithm-specific formulas.
- `persistence` MUST NOT import scientific code, Runtime modules, or FastAPI.
- active production code MUST NOT import retired `legacy` implementations.

`models` is a passive shared-contract boundary. It may contain stable
cross-boundary identifiers and transport-neutral value types, but it MUST NOT
become a catch-all for scientific or Runtime policy. Scientific contracts live
under `scientific/contracts`; Runtime contracts live under
`algorithm_runtime/contracts`; API-only request/response models live under
`api`; persistence-only row/record models live under `persistence`.

## Scientific authority rules

1. A scientific capability has one authoritative implementation. Compatibility
   modules delegate to it and contain no competing formula.
2. A public algorithm entry is one official module under `algorithms/<id>`.
   Its Runtime manifest declares ID, scientific version, implementation
   identity, input/output schema, quality contract, and parameter schema.
3. Reusable signal mathematics lives under `scientific`, not inside a service
   or one official algorithm.
4. Mathematical legality belongs in generic scientific contracts. Official
   semantic constraints belong in the official algorithm/preset.
5. Static and dynamic scheduling belong in `algorithm_runtime`, not in each
   algorithm or primitive.
6. Display filters, paper speed, timebase, sensitivity, and chart rendering do
   not belong to the analysis boundary.

## Scientific authority manifest

The migration SHALL maintain a machine-readable `ScientificAuthority` manifest
for every named primitive and official algorithm. Each entry records its
canonical import path, owner, scientific version, and implementation identity.
An authoritative implementation has `authority=true`. A compatibility adapter
has `authority=false` and a required `delegate_to` target. A validation-only
implementation has `executable=false` and `reference_only=true`; it MUST NOT
be registered with Runtime or callable from an active service path. The
manifest is the source used by duplicate-authority and architecture tests.

## User-defined algorithm retirement

The platform will stop exposing new user-defined executable algorithms. The
retirement sequence is:

1. Keep the existing catalog identity visible, but mark it
   `status=retired`, `executable=false`, `creatable=false`, and
   `editable=false`. New creation and execution requests return a stable
   unavailable response rather than deleting or silently hiding the entry.
2. Preserve read-only definition and Run inspection endpoints.
3. Ensure historical Run/result/artifact read paths never import the retired
   `UserDefinitionAlgorithm` executor.
4. Move the executor and graph engine behind `legacy` only after architecture
   tests prove no active execution caller remains.
5. Remove retired executable code only in a later approved removal change.

## Strangler migration sequence

1. **Inventory and guards**: classify every `eeg_core` file and add import
   architecture tests. Freeze new scientific additions to old locations.
2. **Contracts and quality**: migrate typed scientific contracts and shared
   integrity/signal-quality code first. Keep old imports as delegating facades.
3. **Spectral primitives**: migrate preprocessing, Welch PSD, band power, and
   STFT one capability at a time. Use golden values and independent references.
4. **Official algorithms**: migrate IAPF, RBP, FAA, Theta/Beta one module at a
   time to call only `scientific` APIs. Resolve catalog compatibility metadata
   from Runtime manifests rather than a parallel registry.
5. **Service cleanup**: make spectral/Run services use interfaces and typed
   primitive outputs; remove direct formula imports.
6. **Persistence cleanup**: move repositories into `persistence/repositories`
   without changing SQL tables or migration history.
7. **Legacy retirement**: remove adapters only after all callers, tests, and
   historical-read guarantees are proven.

No step may combine a formula/contract alteration with a structural relocation.
If a numerical discrepancy is found, stop the migration of that capability,
compare old/new against an independent reference, and resolve it in a separate
scientific change.

## Verification strategy

- Dependency architecture tests scan forbidden imports and verify one allowed
  composition root registers official modules.
- Golden regression tests compare PSD, band power, IAPF, RBP, FAA, and
  Theta/Beta outputs before and after each migrated capability.
- Baselines are frozen fixtures with named comparison rules: exact equality for
  discrete/identifier/unit/coordinate/provenance fields, and explicit per
  output `rtol`/`atol` for floating-point values and arrays. A migration cannot
  pass on an unspecified "existing tolerance".
- Independent SciPy/MNE/reference checks remain separate from production
  implementations.
- API compatibility tests verify existing endpoint field meanings until a
  separately versioned API migration.
- Historical Run tests prove results/artifacts remain readable without retired
  executable imports.
- Each phase runs full backend tests, `openspec validate --strict`,
  `git diff --check`, and applicable file-size-policy validation.
