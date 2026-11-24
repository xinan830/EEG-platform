## Decision

The active filter remains authoritative while a replacement session is created
and warmed in the background. Incoming contiguous display blocks are also
queued to the replacement session. Once it has processed through the latest
completed active block, the desktop atomically selects it for the next block
and records that next sample counter as the effective boundary.

Warm-up uses the existing Python-owned causal filter implementation. C# only
transports immutable raw copies and coordinates session state. Binary warm-up
avoids JSON number expansion and its large temporary allocations.

The renderer uses a white cyclic range beginning at the received-sample write
position. Its duration is 0.3 seconds in axis coordinates; near the right edge,
the remainder wraps to the left. No colored cursor is layered over it.

## Rejected alternatives

- Crossfading old and new filtered values was rejected because the blended
  samples belong to neither configured scientific filter.
- Re-filtering the visible history was rejected because it mutates pre-change
  display meaning and previously caused blank partial windows.
- Pausing active output during replacement warm-up was rejected because it
  freezes received-sample time and later forces a visible jump.

## Failure behavior

A failed or superseded replacement is closed without replacing the active
session. Raw capture and persistence continue. A failure is observable while
the previous display filter remains active.
