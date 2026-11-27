# Tasks

## 1. Lifecycle regression

- [x] 1.1 Add a WPF lifecycle regression test that detaches and reattaches the dock host, verifies the floating slot remains open, and confirms exactly one owned window is recreated; run the isolated test and confirm it exposes the current failure.

## 2. Restore floating windows

- [x] 2.1 Synchronize floating windows from the current slot collection on host load; run the lifecycle regression and existing multi-algorithm workspace tests.

## 3. Project checks and size assessment

- [x] 3.1 Update `docs/code-size-policy.json` for the current Review XAML and dock-host sizes, then run OpenSpec validation, relevant desktop Review regression tests, and `git diff --check`.
