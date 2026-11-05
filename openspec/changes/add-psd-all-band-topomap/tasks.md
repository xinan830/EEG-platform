## 1. Backend Projection

- [x] 1.1 Support multiple unique PSD band fields in one adapter request while preserving single-field and frequency-bin behavior.
- [x] 1.2 Keep PSD band reduction, bandwidth, and exact bounds traceable per field; add focused numerical and API tests.

## 2. Review Presentation

- [x] 2.1 Add concise band selector labels and the “全部频段” option; request one or five fields for the current PSD window.
- [x] 2.2 Render five stable topomap items for the all-band selection and retain the existing single-map behavior.
- [x] 2.3 Add focused WPF tests for option labels, requested IDs, multi-field binding, and concise map titles.

## 3. Validation

- [x] 3.1 Update the topomap file-size review for the scoped backend service changes.
- [x] 3.2 Run focused backend and WPF tests, strict OpenSpec validation, and `git diff --check`.

## 4. Color Normalization Repair

- [x] 4.1 Add regressions for the observed small PSD range, equivalent PSD/RBP pixel colors, and constant bounds; verify failure on the original renderer.
- [x] 4.2 Remove absolute epsilon floors from shared normalization and constant-bound handling while preserving locked scales and endpoint saturation.
- [x] 4.3 Run focused rendering/PSD/RBP tests, strict OpenSpec validation, file-size assessment and diff checks; record manual acceptance limits.

## 5. Montage Spatial Eligibility

- [x] 5.1 Project validated average-reference outputs at their positive electrode position while retaining exclusion for channel and specified-pair outputs.
- [x] 5.2 Add regressions for average-reference projection and bipolar exclusion; run focused PSD/RBP topomap tests plus strict OpenSpec validation.
