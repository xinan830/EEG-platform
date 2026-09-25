# Add Peak Frequency and Band Ratio Scientific Modules

## Why

The backend has a stable Runtime, scientific primitive boundary, and official
algorithm catalog, but it does not yet expose two reusable spectral capabilities
as independently versioned official modules: peak frequency in a declared band
and the ratio of power in two declared bands. These capabilities are needed by
future static, dynamic, and box-selected analysis without overloading IAPF or
the standard Theta/Beta algorithm with broader semantics.

## Scope

- Add an official `PeakFrequency` module backed by the authoritative PSD and
  peak-frequency primitive.
- Add an official `BandRatio` module backed by authoritative band-power
  integration.
- Expose typed, client-safe parameter schemas for frequency bands, channel
  selection, execution mode, and quality policy where applicable.
- Support static ranges, sample-aligned dynamic windows, and UI box selection
  through the existing Runtime execution model.
- Persist units, sample coordinates, resolved bands, quality evidence,
  implementation identity, and scientific version in Run provenance.
- Add golden and independent-reference tests, including invalid ranges, gaps,
  unavailable results, and coordinate/evidence failures.

## Non-goals

- Do not rename or broaden IAPF. IAPF remains the official Alpha peak-frequency
  algorithm with its own fit, candidate locking, and provenance semantics.
- Do not replace or parameterize the existing standard Theta/Beta algorithm.
- Do not add browser-side FFT, PSD, peak, or ratio calculations.
- Do not change existing algorithm formulas, output units, historical Runs, or
  the raw recording format.
- Do not add user-authored executable algorithms or a plugin mechanism.
- Do not make display timebase, paper speed, sensitivity, zoom, or chart state
  scientific parameters.

## Compatibility and migration

This is an additive official-module change. Existing catalog entries and Run
payloads remain valid. New module results use a new scientific version and
explicit output schemas. Historical results remain readable and are not
reinterpreted as PeakFrequency or BandRatio results.
