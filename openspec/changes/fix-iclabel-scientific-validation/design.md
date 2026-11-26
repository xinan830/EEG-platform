# Context
See proposal.md. Scientific classification shares fitted ICA with review/application; current logging rounds Infomax changes before the stopping check.

# Decisions
- Require one explicit, complete mean-reference pool equal to unique fitted scalp sites; verify the fitted samples are numerically CAR. Mark reference metadata on an unchanged copy instead of subtracting another mean. Partial pools retain ICA but disable ICLabel.
- Capture full-precision logging arguments for MNE's weight-change and small-angle stopping rules, including restarts. Unknown evidence fails closed. Do not replace the ICA algorithm or monkey-patch MNE.
- ICA does not require correlation references. Explicit invalid selections still fail validation; SSP/EOG reference requirements remain unchanged.
- Missing model assets and known ONNX runtime failures return unavailable with a stable reason. Invalid scientific outputs and programming errors still raise.
- Check the exact runtime model path on every metadata request. Cache successful SHA-256 only by resolved path, size and modification/change timestamps; missing files are not cached. This permits restoration/replacement without restart.
- Apply actual recommendations to deterministic synthetic inputs. Check selected indices and posterior alpha/central beta retention independently of predicted labels, with a forced-removal control. The existing rank fixture contains independent broadband sensor noise and is not evidence of physiological clean-EEG false-positive rates.
- Bump fit/application identities because recommendations change. Keep existing service cache ownership and persistence. No new cache or fallback model.

# Risks and Verification
MNE log structure is dependency-specific: exercise real cap termination, precise threshold, small-angle stops and unknown evidence. Validate full/partial CAR, no-reference API, runtime fault injection and restored output tolerance. Lock updates must preserve existing dependency versions where possible. Scientific clean-signal accuracy and hardware/UI validation remain separate from regression tests.

# Migration / Rollback
No recording or database migration. Existing review fingerprints become expired and require review. Roll back code and lock together; do not reinterpret old component indices. Registered scientific modules may stay above 400 lines when responsibility remains cohesive.
