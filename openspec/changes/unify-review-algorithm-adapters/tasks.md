## 1. Contracts and Baseline

- [ ] 1.1 Add desktop contract tests capturing current PSD playback, drag, release, cancellation and stale-response behavior before changing orchestration.
- [ ] 1.2 Define a typed immutable review analysis snapshot for recording identity, cursor sample, analysis montage/reference, complete algorithm config, preprocessing, and policy/version identity.
- [ ] 1.3 Define the algorithm-neutral review adapter and presentation contracts, including an explicit unsupported-capability result.

## 2. Trigger and Target Coordination

- [ ] 2.1 Separate playback advancement, drag movement and drag completion events in review coordination; keep drag navigation immediate while suppressing analysis requests during movement.
- [ ] 2.2 Submit the latest eligible cursor target immediately on drag release and deduplicate it against matching in-flight or ready work.
- [ ] 2.3 Preserve playback refresh semantics through backend-planned window identity, ensure playback is never blocked by analysis, and display queued/computing/insufficient/missed target states accurately.
- [ ] 2.4 Reject stale responses by recording, immutable configuration snapshot, adapter/algorithm identity, target revision and backend window identity; detach only this consumer's subscription on replacement or close.

## 3. PSD Adapter and Regression

- [ ] 3.1 Move PSD request construction and typed result parsing/presentation behind the PSD review adapter without changing scientific parameters, preprocessing, units or returned values.
- [ ] 3.2 Verify display montage/filter changes do not alter scientific identity unless explicitly represented by the PSD scientific contract; verify analysis montage/filter changes do.
- [ ] 3.3 Run focused tests for playback, drag release, same-target reuse, backward seek, stale completion ordering, panel close and recording switch.

## 4. Algorithm Adoption and Validation

- [ ] 4.1 Inventory each algorithm's backend on-demand window capability, complete request config, continuous preprocessing contract, output shape, quality and provenance requirements.
- [ ] 4.2 Register each additional algorithm only after its backend window path and algorithm-owned presenter pass scientific-equivalence and review contract acceptance; keep unsupported algorithms explicitly unavailable.
- [ ] 4.3 Perform WPF manual acceptance of playback and drag-release behavior for each registered adapter, including missing data, quality rejection, execution failure and rapid target replacement.

## 5. Documentation and Quality Gates

- [ ] 5.1 Update current review architecture documentation and adapter onboarding instructions to match the accepted contracts and ownership boundaries.
- [ ] 5.2 Review affected file sizes and update `docs/code-size-policy.json` for any changed business file above 400 lines; document justified split boundaries.
- [ ] 5.3 Run applicable WPF build/tests, any affected backend tests, `openspec validate --strict --change unify-review-algorithm-adapters`, and `git diff --check`.
