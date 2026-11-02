## 1. Inventory and guardrails

- [x] 1.1 Enumerate candidate compatibility paths, callers, XAML `x:Class`/resource references, persisted-data readers, API consumers, and provenance dependencies.
- [x] 1.2 Classify every candidate as historical-file support, current scientific implementation, active adapter, or obsolete internal fallback; freeze the deletion list and record exclusions.
- [x] 1.3 Add or update focused regression checks that prove historical EEG files, raw recordings, persisted results, current TNK children, units, time coordinates, and channel mappings remain available. Existing TNK/recording boundary regression tests passed 11/11.

## 2. Remove confirmed obsolete compatibility paths

- [x] 2.1 Inventory found no removable TNK fallback: `legacy_runtime` is the current Crimson/Zenlite scientific implementation and historical adapters remain required; the obsolete WPF workspace namespace was handled in 2.2.
- [x] 2.2 Migrate one WPF compatibility namespace slice by updating `x:Class`, code-behind, resource URIs, generated BAML/composition references, and tests together.
- [x] 2.3 Search the repository and build outputs for the removed fallback/namespace and verify no public API, stored result, or provenance reader depends on it.
- [x] 2.4 Run focused backend tests, desktop build, desktop tests, and `git diff --check` for the compatibility slice.

## 3. Reduce composition-root responsibilities

- [x] 3.1 Map `App` and `MainWindow` construction, lifecycle, shared-state, and navigation responsibilities and assign each to one owner.
- [x] 3.2 Move only feature-owned construction/lifecycle detail behind explicit module composition; preserve one application composition root and avoid a service locator or DI framework.
- [x] 3.3 Add focused composition tests or static checks proving shared state has one owner and feature modules do not create duplicate global services. Static checks confirm view construction is application-owned and no feature View directly references `MainWindow`.
- [x] 3.4 Build and run the affected desktop tests after the composition slice.

## 4. Tighten navigation boundaries

- [x] 4.1 Inventory `Views/*.xaml.cs` and related code for direct feature navigation through `MainWindow` casts.
- [x] 4.2 Replace feature-navigation casts with the narrowest existing command/module boundary; retain `Window.GetWindow` only for modal-dialog ownership.
- [x] 4.3 Manually verify each changed workflow, including navigation, back/close behavior, dialog ownership, and resource/BAML loading.
- [x] 4.4 Run desktop tests/build and record the manual acceptance evidence.

## 5. Contain generated output

- [x] 5.1 Identify generated `bin/obj`, `.isolated-build`, temporary traces, and other outputs that are safe to ignore or remove; exclude recordings, logs, databases, and user data.
- [x] 5.2 Route isolated build/test outputs under the existing ignored temporary root and add only verified generated-path ignore rules.
- [x] 5.3 Confirm no build, test, or desktop process uses the candidate outputs, then remove only the verified generated artifacts.
- [x] 5.4 Verify `git status` contains no generated artifacts and no user data. Ignored isolated outputs do not appear in status; no recording, log, database, or user-data path is present.

## 6. Final validation and documentation

- [x] 6.1 Run the focused backend and desktop checks, full applicable build/test checks, OpenSpec strict validation, and `git diff --check`.
- [x] 6.2 Update current architecture/module-boundary documentation with the final ownership and navigation rules; remove superseded internal cleanup notes.
- [x] 6.3 Produce a validation report that lists removed paths, preserved compatibility, manual checks, test commands/results, and known limits; label engineering evidence separately from clinical validation.
- [x] 6.4 Confirm no scientific formula, time/unit/channel semantics, public API, historical recording support, or persisted-result readability changed before archiving.
