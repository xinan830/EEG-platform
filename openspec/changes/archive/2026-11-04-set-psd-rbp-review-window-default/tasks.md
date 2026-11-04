## 1. Declare algorithm defaults

- [x] 1.1 Set PSD and RBP manifest dynamic policies to a 10-second default and one-second refresh while retaining existing supported options, minimum window, and warmup behavior.
- [x] 1.2 Assert the public official catalog exposes those PSD and RBP policy values.

## 2. Verify Review requests

- [x] 2.1 Update the PSD Review parameter test to assert a serialized 10-second window.
- [x] 2.2 Add a focused RBP Review parameter test asserting a serialized 10-second window.
- [x] 2.3 Run focused tests, strict OpenSpec validation, and `git diff --check`.
