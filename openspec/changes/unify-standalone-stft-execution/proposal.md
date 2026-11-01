## Why

Standalone STFT already uses the platform Run/window services, but static, full dynamic and on-demand adapters independently load, convert and validate spectrograms. Full dynamic can retain finite values in a rejected outer window while on-demand discards them; malformed matrices and cancellation also take different paths. Consolidate module execution rather than add another scheduler.

## What Changes

- Introduce one typed STFT frame outcome for source preparation, coordinate/matrix validation, unit conversion, quality, evidence and provenance.
- Keep static/full/window adapters as result-envelope mappings using existing execution admission, cache, cancellation and publication.
- Preserve static per-inner-row partial output; make both dynamic strategies suppress the entire rejected outer matrix consistently, with structured failure and NaN only in full artifact slots.
- Advance implementation/window-policy identities and immutable Definition version; do not rewrite recordings, stored results or add retired executors.

## Capabilities

### New Capabilities
- `analysis/standalone-stft-execution`: One STFT scientific outcome behind the platform execution channel.

### Modified Capabilities
- `analysis/spectrogram`: Replace whole-recording preprocessing with explicitly versioned finite-context preprocessing.
- `analysis/on-demand-windows`: Allow the current STFT bounded scientific contract; other algorithms keep their declared scopes.

## Impact

Backend STFT adapters, identity assertions, scoped numerical/API/strategy tests, desktop STFT consumer checks and validation documentation. WPF continues using its existing Run/on-demand consumers. No Review panel, TNK, live acquisition, new threads, queue or dependency.

Follow-up risk removal: STFT reads only the requested sample range plus finite filter context on each side, clamped to the recording. Science advances to spectrogram-v3-bounded; filtering boundaries may change values. Four-second Hann segments, one-second inner steps, quality rules, frequency/notch settings and units stay unchanged. Static explicitly requested long ranges still cost proportionally to that range; dynamic first-window preparation must not scale with recording duration. Rollback uses the previous build; source recordings and historical artifact bytes are immutable.
