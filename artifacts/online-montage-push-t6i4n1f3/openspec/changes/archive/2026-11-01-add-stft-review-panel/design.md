## Context

ReviewAnalysisBinding already binds immutable recording/configuration identities to the workspace cursor, supports latest-only loads and releases foreground subscriptions. The existing RBP adapter contains no RBP formula and can serve any declared dynamic-window result. STFT already has a validated bounded-window backend and module-owned parser/heatmap.

## Decisions

1. Compose RBP and STFT coordinators at the desktop composition root. Review selects one; switching cancels preparation, detaches the old binding and clears its presentation before attaching the next. No clock/reader is created by a panel.
2. Rename the existing adapter to ReviewWindowAnalysisAdapter, with explicit algorithm id/display name. Preserve identity checks and pass output kind, units, evidence and actual ranges through the shared preview boundary. No old adapter wrapper.
3. Keep STFT parameters and projection in dedicated types. Obtain current version/window/step from the catalog, derive channels from the current registered montage, serialize the selected channel and explicit STFT filter defaults. The selector retains all available outputs, but the submitted montage mappings contain only the selected input and retain its registered role, id and unit; montage id/revision/fingerprint remain unchanged. Display filters do not become scientific filters.
4. Reuse StftPreview validation for Review through a shared window-data parsing method. Append validated windows through the existing heatmap history path; keep the first returned estimate at each already-rendered time and append only later columns. Forward jumps leave real gaps without backfill. A backward window, configuration reset, panel close, unavailable or malformed result clears the history. Successful windows do not publish an intermediate null preview. Absolute center time is backend-relative center plus actual window start, exactly once; power remains backend dB re 1 uV^2/Hz with linear V^2/Hz validation.
5. Expose channel selection in the panel header beside dock/close actions. Keep the output unit alongside the heatmap color scale and show only concise unavailable/failure status; omit the result-evidence expander. Rejected/malformed results never become zeros or an old current window. Preserve Review time-display formatting.

## Alternatives and risks

Embedding the entire standalone catalog/detail workspace would create duplicate recording and cursor ownership. Copying the RBP adapter would duplicate cancellation and identity checks. Both are rejected. No numeric contract/version change is made; bounded STFT edge semantics and strict quality gate remain in effect. History contains visited valid windows rather than a precomputed full recording. Display memory and renderable segments grow with accumulated history; long-duration performance remains a manual acceptance item.

## Migration and storage

Update internal adapter callers/tests together; no external API or file migration. Backend owns scientific caches, and panels release only their own subscription. Reverting the integration removes its menu/panel and keeps standalone algorithms and immutable artifacts intact.

## Validation

Test shared adapter routing/units, STFT absolute axes and quality rejection, channel request snapshots, switch/close cancellation and late completion, plus existing RBP/Review behavior. Build WPF to isolated output, validate OpenSpec strictly, check diff and registered source sizes. Backend code is unchanged, so existing bounded preprocessing tests need not be rerun. Manual acceptance must cover real playback/seek/pause, montage/channel changes, close/reopen, docking and performance; automated checks do not establish this.
