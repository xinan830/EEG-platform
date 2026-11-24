# Design

## Context

See `proposal.md` for motivation and `specs/` for user-visible behavior. The backend already stores uploaded EDF/BDF files by recording identity and reads them through MNE, while the desktop project list and review reader currently recognize only local WPF recording directories. Desktop projects have stable local IDs, but they are not currently synchronized with the backend Project/Subject/Session hierarchy.

## Goals / Non-Goals

**Goals:**
- Reuse the current EDF/BDF parser, immutable recording identity, review presentation, and offline algorithm execution.
- Keep file transfer and sample access bounded and cancellable.
- Make every imported recording discoverable in its desktop project and traceable through a project Session.

**Non-Goals:**
- Copy signal bytes into both the project folder and backend recording store.
- Change acquisition recording formats, scientific formulas, algorithm result contracts, or add format conversion.

## Decisions

1. **Use one canonical source file in the backend-managed recording store.** The current import/access services already own EDF/BDF files there. The project Session stores the recording ID and project metadata only; a second copy in the project folder would double disk use and create two mutable-path failure modes. Desktop UI continues to present the recording under the selected project.
2. **Use the desktop project's stable ID as its backend Project ID.** Add an idempotent ensure/sync operation so existing desktop projects can participate without a separate ID mapping table. A subject local code is selected or created, and one Session is created for each imported recording.
3. **Stage, inspect, then publish.** Stream upload blocks into a temporary file in the recording store while computing size and SHA-256. Validate format and metadata with MNE before publishing the final source and recording row. Commit recording metadata and Session association together; compensate filesystem publication on database failure, and remove staging data on cancellation, disconnect, or any error.
4. **Keep the standalone import contract compatible.** Add a project-scoped import operation for desktop use instead of changing existing standalone request semantics. Existing callers keep their response and behavior; the new operation returns a structured recording/session result.
5. **Add an EDF/BDF review access adapter, not a second review UI.** Expose bounded raw sample-range reads from the backend recording access layer in V/float64 and source channel order. Adapt those reads to the review source contract, keeping the existing filtering, frame construction, chart, event/time presentation, and algorithm panels. Do not feed display-filtered µV data back through review filtering.
6. **Merge imported Session rows with local WPF rows in the project catalog.** Imported rows carry backend recording and Session identities; WPF rows retain their local directory identity and access path. Review and deletion actions dispatch by source kind. Moving a project to trash removes only local association metadata; it does not silently delete globally identified source files.

## Risks / Trade-offs

- [A desktop project may have no backend Project row yet] → Resolve it idempotently using the stable desktop ID before import; reject mismatched subject/session ownership.
- [A process can stop between moving a staged file and committing SQLite] → Publish only after validation, delete the final file on ordinary transaction failure, and reconcile unreferenced imported files during startup without touching referenced or legacy sources.
- [EDF/BDF may contain discontinuities, mixed channel types, unsupported units, or duplicate normalized labels] → Preserve source order and raw labels, validate channel metadata explicitly, expose discontinuities as breaks, and return structured errors instead of silently coercing data.
- [Review is currently coupled to WPF manifest metadata in several caches] → Introduce the smallest source-neutral metadata boundary needed by the adapter and include EDF identity, source hash, rate, channel schema, and filter contract in derived-cache keys.
- [Large files can exhaust disk even with bounded RAM] → Propagate cancellation, handle disk-full errors with staging cleanup, and expose transfer progress; a fixed format-specific size ceiling is not introduced without a product requirement.

## Migration Plan

No existing recording file or row is rewritten. Keep existing imported recording rows and standalone import clients readable. If a persisted desktop/backend project identity field is required, add it as an additive idempotent SQLite migration and backfill nothing by guessing; desktop projects synchronize on first use. Rollback removes the new desktop entry points and adapter while leaving registered source recordings and existing project Session records intact.

## Testing Strategy

- Backend: streaming/cancellation cleanup, size/hash integrity, metadata validation, structured failures, idempotent project resolution, subject/session ownership, and atomic recording/session publication.
- Desktop: selected project/subject request payloads, cancellation and failure state, project list merge, review source routing, cache identity, and return-to-project behavior.
- Contract: EDF/BDF sample windows preserve absolute file-relative seconds, source channel order, V/float64 internally, µV only at the public display boundary, and declared discontinuities without fabricated samples.
- Run focused backend and desktop tests plus OpenSpec validation; inspect changed file sizes and register evaluation for any business file exceeding 400 lines.
