# Add WPF static STFT workflow

## Why

The backend exposes official STFT, but the product WPF algorithm page only submits and renders PSD. Catalog visibility currently overstates usable desktop functionality.

## Scope

Enable a completed, backend-registered recording's one raw EEG channel and explicit static time range to run official STFT. Render the backend's bounded `time_center_s x frequency_hz` `power_db` matrix with declared units, missing-cell treatment, Run identity, ranges, and quality. Retain PSD behavior.

## Compatibility and scientific impact

No backend schema, algorithm, persisted artifact, or raw recording changes. Dynamic STFT and other catalog algorithms remain unavailable in this form. WPF performs only display-coordinate and color mapping; backend values and provenance remain authoritative.

## Migration and rollback

This is additive desktop behavior. Reverting the WPF STFT form and preview leaves backend Runs and artifacts readable; no data migration is required.
