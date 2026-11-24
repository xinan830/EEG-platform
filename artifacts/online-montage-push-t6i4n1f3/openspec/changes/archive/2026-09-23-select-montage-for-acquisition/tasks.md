## 1. Configuration source

- [x] 1.1 Remove the channel-list apply action and automatic global default application.
- [x] 1.2 Expose valid channel profiles to montage configuration without an apply state.
- [x] 1.3 Preserve reference locking and immutable montage channel snapshots.

## 2. Acquisition preparation

- [x] 2.1 Add montage selection to the acquisition preparation controls.
- [x] 2.2 Validate the selected montage and channel snapshot before opening the stream.
- [x] 2.3 Persist channel and montage snapshots in stream metadata.
- [x] 2.4 Render selected montage outputs from raw display batches without changing raw storage.

## 3. Verification

- [x] 3.1 Add focused tests for montage selection and derived display traces.
- [x] 3.2 Run desktop Release tests, strict OpenSpec validation, and diff checks.
