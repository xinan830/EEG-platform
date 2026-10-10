# Design

## Compatibility resolver

`RecordingMontageCompatibility` is a pure desktop-domain resolver. It first
requires the candidate's bound channel-configuration identity to equal the
immutable recording snapshot identity, validates the montage definition, then collects all source labels from derived
channels and the saved average-reference group. For every required label it
checks the candidate snapshot and compares the candidate native index, expected
kind, label, device signature, and hardware reference/ground locations with
the recording snapshot.

The resolver separately checks the manifest source channel for uniqueness, V
units, and agreement with the historical snapshot's native index, label and
kind. It does not alter `MontageDisplayProjector`, which must continue to read
the historical recording channels.

## Selection boundary

The UI binds only compatible montage profiles; incompatible reasons remain in
the catalog for diagnostics. Empty selections are disabled with a visible raw
channel fallback notice and a reason tooltip. There is no mixed-option wrapper
or duplicate selection collection. The session accepts only a profile instance
from its verified catalog, never an arbitrary object with the same profile ID.

## Alternatives

- Allowing a different `ChannelConfigurationId` was rejected because review
  must not mix montage provenance from separately saved channel configurations,
  even where their current scientific mappings happen to be equal.
- Comparing labels only was rejected because swapped native mappings are the
  reported bug.
- Comparing every UI field was rejected because display order and visibility
  are not signal identity; only actual dependencies are required.

## Testing

Add catalog tests for same mapping/different ID, swapped native mapping, display
order-only changes, bipolar, mean and specified-pair dependencies, missing
source channels, duplicate labels, missing history, and device mismatch.
