# Enforce Recording/Montage Mapping Compatibility

## Why

Review currently treats a montage as compatible when its electrode labels are
present in a recording. That is insufficient when two channel configurations
swap labels across native inputs: the review projector correctly reads the
historical recording, but a montage bound to the swapped mapping is silently
accepted.

## Scope

- Require a candidate montage to be bound to the exact channel-configuration
  identity stored in the recording, then compare its snapshot with the
  immutable channel snapshot stored in the recording.
- Validate every source dependency used by a montage, including positive,
  negative, mean-reference, and specified-pair channels.
- Keep the existing historical-label projector unchanged.
- Reject missing historical mapping metadata as unverifiable rather than
  guessing compatibility.

## Non-goals

- No raw-recording rewrite.
- No change to EEG arithmetic or display projection.
- No comparison by profile ID, name, description, or display order.
- No unrelated review UI redesign.

## Compatibility and migration

New recordings already persist `channel_configuration_id` and
`channel_configuration_snapshot_json`. Existing recordings without that data
remain readable as raw records, but montage switching is unavailable because
the configuration source cannot be verified. A duplicated configuration with
the same mapping is intentionally not interchangeable in the review selector.

## Scientific contract impact

A montage is applicable only when each source signal it needs can be resolved
from the recording and the candidate's physical mapping agrees with the
recording mapping. Display order is presentation-only. The original recording
and its historical mapping are immutable.
