## Context

See proposal.md for the observed failure. The version-2 reference index records discovered recording paths even when their manifest currently yields no montage IDs. The manifest remains the source for verified identities; the index retains identities already established for that path.

## Goals / Non-Goals

**Goals:** Permit a previously empty path entry to gain identities confirmed by the current, internally consistent manifest, so startup verification can recover.

**Non-Goals:** Change the raw manifest, infer missing IDs, weaken mismatch handling for non-empty entries, or change reference deletion policy.

## Decisions

- Treat an empty indexed set as absence of an established identity, not as an identity that must remain empty. During registration, replace that empty set with manifest-derived IDs.
- Keep exact set equality for an established non-empty set. An actual change or conflict remains a verification error.
- Do not change index schema or add a user-facing repair action; the ordinary successful scan performs the reconciliation and persists through the existing atomic save path.
- Alternatives considered: deleting the reference index would discard historical protections; permanently ignoring the path would weaken verification; requiring manual repair would leave startup blocked despite a valid manifest.

## Risks / Trade-offs

- A formerly empty path may now protect a montage profile that was not previously locked → this matches the manifest's recorded provenance and makes deletion protection conservative.
- A non-empty changed identity remains unrecoverable automatically → this is intentional because the system cannot determine whether the index or recording provenance is authoritative.

## Migration Plan

No schema migration is needed. The next successful reference scan reconciles and persists empty entries. Rollback restores the prior strict empty-versus-non-empty comparison; it does not alter raw recordings.
