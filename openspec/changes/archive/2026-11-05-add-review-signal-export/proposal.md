## Why

The recording review toolbar advertises EDF export but has no working export action. Researchers need a portable signal file for the exact portion currently under review, while preserving the original recording as an immutable source.

## What Changes

- Add EDF and BDF export for the complete recording using original unfiltered source samples.
- Preserve the source sampling rate, channel order, signal unit, and recording start time adjusted to the exported sample offset. Preserve labels in the EDF/BDF header when representable; record aliases and original names, including missing labels, in a companion mapping file.
- Include review events that fall inside the interval as file annotations.
- Reject an interval that crosses a sample gap instead of synthesizing samples or shifting event times.

## Capabilities

### New Capabilities
- `desktop/review-signal-export`: Export raw samples from a visible recording-review interval to EDF or BDF.

### Modified Capabilities
- None.

## Impact

- Affected code: WPF review toolbar and view model, local recording access, export service, desktop tests, and the desktop project dependency list.
- No HTTP or database contract changes. The imported/acquired source remains immutable.
- Adds the BSD-3-Clause SharpEDFlib binding and its Windows x64 native EDFlib runtime.
- Non-goals: filtered or montage-derived export, other file formats, anonymization, and export across discontinuities.
- Scientific-contract impact: time uses the file's absolute sample coordinate; signal values are converted explicitly from the manifest unit to EDF/BDF physical microvolts; source channel order is retained.
- Migration: none.
- Compatibility: new EDF/BDF outputs are interoperable derived files; export does not replace or modify the acquisition source.
