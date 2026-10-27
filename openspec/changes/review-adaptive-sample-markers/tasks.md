## 1. Display Projection

- [x] 1.1 Add a pure, bounded marker projection/visibility policy using real review-frame samples and the 5 DIP threshold.
- [x] 1.2 Add focused tests for threshold boundaries, finite samples, sample-counter gaps, and exclusion of extrema-only data.

## 2. Review UI

- [ ] 2.1 Add an off-by-default point-and-line toggle to the review toolbar and expose its state to the waveform renderer.
- [ ] 2.2 Render independent SciChart point markers from bounded frame samples while retaining existing continuous line rendering and gap behavior.
- [ ] 2.3 Re-evaluate marker visibility on viewport and plot-size changes without modifying raw, filter, montage, or analysis data.

## 3. Validation

- [ ] 3.1 Run focused desktop review tests and build the WPF desktop project.
- [ ] 3.2 Run strict OpenSpec validation and `git diff --check`; record validation scope and limits.
