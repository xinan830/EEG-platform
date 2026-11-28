# Design

Reuse `BackNavigationIconButtonStyle` from application resources, with the same arrow geometry as the other settings pages. Place the button beside a title/description stack, use a 20-point title and 13-point description, and apply 24/20 page margins. Preserve `OnBackClick` and all calibration bindings.

A new shared header component or style duplication would add unnecessary scope. The existing 769-line calibration view is already registered for future responsibility-based splitting; this localized header change stays within that limit and does not justify moving calibration controls or state.

Validate the XAML header contract, compile Release BAML, run existing screen calibration tests and file-size checks. Manual DPI/display calibration is outside this presentation-only fix. Rollback restores the original XAML; no stored data changes.
