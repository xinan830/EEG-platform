## Decision

The validator will require `len(requested_channels) == len(contract.channel_role_order)`. Scalar results continue to compare their slash-joined channel string; structured results compare their ordered channel list. No fallback or inferred channel is introduced.
