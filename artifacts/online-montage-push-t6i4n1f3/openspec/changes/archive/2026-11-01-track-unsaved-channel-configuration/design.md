## Context

`ChannelConfigurationWorkspaceViewModel` owns the editable draft and already constructs a persisted-profile snapshot for comparison. The detail view is hosted inside `MainWindow`; leaving can happen through its back button, the global navigation, or closing the main window. See proposal.md for motivation and the spec for required behavior.

## Goals / Non-Goals

**Goals:** keep one authoritative dirty-state calculation in the channel workspace and route all exits from the detail editor through a save/discard/cancel decision.

**Non-Goals:** changing profile persistence format, channel ordering, acquisition state, scientific processing, or the electrode-location model.

## Decisions

- Compare the current persisted draft projection with a saved/initialized `ChannelConfigurationProfile` baseline. This includes metadata, channel entries, electrode locations and map positions; transient search, paging and device-picker state are excluded.
- Advance the baseline only after persistence and refresh succeed. Keep the draft open after a normal save; when leaving, save first and close only after success.
- Use a three-choice Yes/No/Cancel confirmation at navigation boundaries. Yes saves and continues only on success; No discards and continues; Cancel preserves the draft and current page.
- Guard the global navigation and main-window closing paths in `MainWindow`, in addition to the detail back button. Keep dialog presentation in the WPF view/window boundary and dirty calculation in the workspace ViewModel.
- Verify persisted-field equivalence and save-baseline transitions in desktop tests; validate the OpenSpec change and compile/test to an isolated output directory to avoid running-client file locks.

## Risks / Trade-offs

- [A newly added persisted field could be omitted from dirty comparison] → keep comparison based on the complete profile projection and add coverage for channel/electrode changes.
- [A save can fail during an exit attempt] → report the existing error and retain the draft/page; never continue navigation after failure.
- [Main-window close confirmation is synchronous because WPF closing is cancellable] → persistence remains in the explicit Yes path before allowing close; ordinary saves continue using the async command.

## Migration Plan

No data migration is needed. Existing stored profiles load as before. Rollback consists of reverting the WPF dirty-state/navigation changes; persisted profile data is unchanged.
