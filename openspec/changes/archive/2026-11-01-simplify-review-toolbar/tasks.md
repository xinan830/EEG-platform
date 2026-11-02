## Implementation

- [x] Remove duplicate toolbar actions and enable existing edge-to-edge layout.
- [x] Verify XAML composition, build and strict spec validation.

Release build: zero warnings/errors. XML checks confirm both menu actions remain,
duplicate toolbar actions are absent and edge-to-edge layout is enabled.
Strict validation and scoped diff check passed. Native screenshot acceptance
and portable package regeneration were not performed for this layout change.
