## Why

The minimal acquisition workflow exposes DLL paths and range values as raw
form fields on its primary page. This is an engineering setup surface, not an
operator workflow. It also asks users to type ranges before the device has
reported what ranges exist.

## What Changes

- Move SDK selection into a collapsed **Amplifier settings** surface with a
  file chooser and a single **Test connection** action.
- Permit discovery with an SDK path alone; do not require ranges to test a
  connection.
- Return reference/bipolar range capabilities with each discovered device.
- Replace manually typed range fields with device-returned selectors. A stream
  still requires explicit user selection and device-side validation.
- Keep recording directory as an advanced local-storage preference rather
  than primary amplifier input.

## Non-Goals

- No default range selection, SDK bundling, fabricated device results, or
  change to raw recording/scientific ownership.
