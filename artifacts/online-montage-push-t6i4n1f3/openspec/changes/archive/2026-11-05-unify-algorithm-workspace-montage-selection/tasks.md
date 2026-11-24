## 1. Planning and capability wiring

- [x] 1.1 Add the algorithm-workspace delta requirements and validate the change artifacts.
- [x] 1.2 Add a selection instance owner to `AlgorithmListViewModel` and expose only projection/catalogue bindings.

## 2. Lifecycle and requests

- [x] 2.1 Create and initialize the instance after registration from the recorded montage catalogue and acquisition-default rule.
- [x] 2.2 Route selection changes through the existing role remapping, configuration revision, cancellation, and refresh path.
- [x] 2.3 Detach the selection event and release the instance when recording context or catalog lifetime ends.
- [x] 2.4 Ensure all standalone preview and full Run builders capture the current selection snapshot and explicit output mapping.

## 3. Verification

- [x] 3.1 Test algorithm workspace selection isolation and projection behavior.
- [x] 3.2 Test role remapping and request payload identity after selection changes.
- [x] 3.3 Test replacement/disposal detaches the old selection and blocks stale publication.
- [x] 3.4 Run focused desktop tests, WPF build, OpenSpec strict validation, and `git diff --check`; record limits.
