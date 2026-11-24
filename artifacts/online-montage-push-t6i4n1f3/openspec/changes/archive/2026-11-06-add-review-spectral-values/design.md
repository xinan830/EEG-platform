## Context

Review already has PSD and RBP dynamic adapters. PSD results include the frequency density matrix and have an existing backend band-mean-density projection; RBP results include per-channel relative and absolute band-power arrays. The backend has an RMS helper, but it is not yet exposed by a review projection. The current Review coordinator drives one adapter at a time, so the numeric panel must bind both existing adapters to the shared playback cursor.

## Goals / Non-Goals

### Performance correction

The first implementation incorrectly used the complete topomap endpoint for numeric reads: this generated spatial grids that the table discarded and reset the table collection on each update. Replace that coupling at its ownership boundary. A shared result-to-scalar adapter is consumed by both the map service and a typed scalar-only window resource. Numeric reads never resolve coordinates, interpolate grids or create chart payloads. Keep channel row objects stable and notify only changed scalar properties. Waveform playback and viewport loading must not await the numeric resource. Do not add compatibility fallbacks, switches, alternate PSD/RBP mathematics or altered window policies.

**Goals:** Bind the panel to the current results from both existing PSD/RBP adapters, show one scalar row per montage output for the selected canonical frequency band, and preserve their existing playback window and segmentation settings.

**Non-Goals:** Add a scientific algorithm or alternate computation path, change official PSD/RBP calculations or quality rules, display every PSD frequency bin, or infer unavailable values. The existing PSD and RBP adapters may each request their current window from the same playback cursor.

## Decisions

- Bind one existing PSD adapter and one existing RBP adapter to the same recording, montage snapshot, cursor sample and review lifecycle. Reuse their existing window routes; do not implement another PSD/RBP algorithm or duplicate their calculations.
- Read PSD band-mean density from the existing PSD projection, in V^2/Hz at its backend boundary; format the displayed value in µV²/Hz using the existing presentation conversion. Read RBP absolute power in µV² and relative power as a ratio from the same current RBP result; display relative power as a percentage.
- Derive amplitude only in the backend by applying the existing RMS helper to the matching RBP absolute power: sqrt(power in µV²) gives µV. Expose it as a field in the existing RBP result-projection response; preserve unavailable values and reasons, and do not alter official RBP arrays.
- Use the existing review analysis window settings and result lifecycle. Verify the adapter-declared 10-second windows and shared four-second Welch segments with 50% overlap, then accept a row only when both results refer to the same recording, cursor, actual sample window and ordered montage outputs. Preserve source quality and provenance; do not pair stale or mismatched results.
- Show a numeric table with montage output order, one selected band at a time, and columns for amplitude, absolute power, relative power and PSD density. The selector uses the canonical Delta, Theta, Alpha, Beta and Gamma bands.

## Projection Contract And Consumers

- The typed `GET /api/analysis-windows/{window_id}/scalar-values` resource accepts explicit `field_ids` (at most five) and optional project scope. The Review numeric panel is its consumer. The response contains window/recording/algorithm identity, versions, actual seconds, field units, ordered channel scalars, quality, failure and provenance; it contains no coordinates, grids or chart payload. `rbp.amplitude.<band>` reads saved RBP absolute power, calls `band_rms_uv`, returns `uV`, and records `rbp-band-rms-amplitude-v1`. Official algorithm identity, arrays and result schema remain unchanged.
- Scalar failure semantics: missing windows return 404, invalid fields 422, unsupported algorithms or invalid saved contracts 409. Pending windows return unavailable; invalid values and unknown/unavailable channel quality return null scalars with reasons. Neither scientific execution nor coordinate lookup is called by scalar reads. There is no fallback to the topomap endpoint.
- Missing arrays, wrong units or channel dimensions produce an unavailable field with `ABSOLUTE_POWER_NOT_AVAILABLE`. Negative, non-finite and nonnumeric powers produce a null point. Per-channel quality and reasons remain available independently of spatial inclusion. A channel without map coordinates can still have a valid numeric value; the table does not require an interpolated map.
- Fetch the five existing PSD fields and five RMS fields once per matched current window pair. Band changes select already returned scalars. Never derive PSD from RBP or replace source failures with zeros. The table labels amplitude explicitly as RMS and PSD explicitly as band-mean density.
- Raw-channel review uses all current channels in their declared order, just as montage review uses all available montage outputs. This also makes the existing RBP raw-mode analysis channel set complete; its selected trend channel remains unchanged.
- `result_scalar_inputs.py` owns the existing PSD/RBP result-to-scalar adapters shared by `result_scalars.py` and `topomap.py`. The map service alone owns spatial preparation/interpolation. Internal map-specific adapter names and their callers are migrated directly, without aliases; existing map HTTP field semantics remain unchanged.
- `ReviewSpectralValueRow` owns observable values for one stable channel. The row collection changes only when ordered channels change. Window/band updates and unavailable transitions update existing properties, preserving DataGrid row containers and suppressing unchanged notifications.
- Row tooltips reuse the shared bounded, wrapping description style. Format channel quality as its status, supplied clean/total segments, peak amplitude and reason codes; never serialize the nested evidence object into visible text. Show each displayed metric in the table's unit and six significant digits, and preserve failure code/message without dumping failure JSON. This changes presentation only, not result validity or requests.

## Validation Boundary

Focused tests cover source identity, dimensions, units, quality, band selection, shared playback targets, drag/seek, montage changes, cancellation and late responses. A real synthetic WPF recording exercises the existing backend PSD/RBP window routes and their projections. An isolated WPF render verifies row-container reuse and numeric binding. These checks do not establish manual acceptance in a running review workspace with the user's recording. Initial evidence is in `docs/validation/2026-11-06-review-spectral-values-report.md`; the final separation tests, interleaved projection benchmark and known baseline failures are in `docs/validation/2026-11-06-review-spectral-performance-fix.md`.

## Risks / Trade-offs

- Two existing adapters and their result projections add work while the panel is open → use existing latest-only bindings, cancel obsolete targets and release both sessions on close.
- Existing PSD and RBP outputs may be unavailable or have different window identities → show an unavailable/loading state and do not synthesize a matched row.
- Existing algorithms retain their own quality policies → display each source's quality and provenance; the table does not imply identical accepted samples or clinical meaning.
- µV²/Hz formatting differs from the backend's V²/Hz unit → use the established display conversion once and cover it with a focused presentation contract check.

## Migration Plan

No stored data migration. Deploy the backend scalar resource and desktop scalar consumer together. Existing spatial consumers continue to use their map endpoint, while the numeric panel requires the scalar capability explicitly; no compatibility branch or alternate science implementation is retained.
