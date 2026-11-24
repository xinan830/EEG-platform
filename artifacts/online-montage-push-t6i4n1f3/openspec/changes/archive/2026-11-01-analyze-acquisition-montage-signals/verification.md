# Verification

2026-10-09 local verification:

- Backend full suite: 479 passed. Includes 31 acquisition-view cases covering
  saved output order/count, all reference formulas, source selection, NaN gaps,
  missing/contradictory metadata, raw/derived registration identity, cache source
  identity, persisted reference provenance and six official algorithms in both
  static and dynamic modes. REF PSD agrees with the original source within
  relative tolerance 1e-12.
- WPF Release suite: 448 passed. Registration JSON explicitly sends
  acquisition mode; returned two-output names remain intact. Review parameter
  choices and request retain derived names and reject an unregistered raw name.
  Brainbeat accepts explicit Fz/Pz hardware-reference outputs, not bipolar roles.
- Real completed recording with 11.079 seconds and 28 raw EEG inputs exposes
  only F7-REF/F3-REF. Projected samples agree exactly with historical sources.
  PSD/STFT/RBP/Theta-Beta/IAPF/peak-frequency static and dynamic submissions
  retained acquisition reference provenance. Static Runs returned gate_failed;
  dynamic Runs completed, which alone does not prove valid windows.
- The older 149.982-second recording's acquisition snapshot also parsed, with
  21 saved outputs. This verification did not rerun all algorithms on it.
- OpenSpec strict validation and git diff --check passed.

The real-recording verifier accepts a directory argument at
`_verify/verify_acquisition_montage_analysis.py`; catalogs/artifacts are temporary
and the input recording remains read-only.

Interactive WPF acceptance remains manual: restart backend and desktop, select
the two-output recording in algorithm selection and review PSD, confirm only
the two saved outputs appear, then change selected output and inspect the
backend result/failure details. No screenshot-based acceptance is claimed.

Existing OpenTK/SkiaSharp framework compatibility and FastAPI test-client
deprecation warnings remain. They are not caused by this source-view change.

File-size review: snapshot validation/projection is a dedicated small module.
RecordingService's compatibility facade is 512 lines, reviewed in
docs/code-size-policy.json with a 520-line limit. No unrelated facade migration
or display-projector changes are included.
