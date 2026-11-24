# Design

For recording duration D, visible duration W and accepted position p, follow
with start = clamp(p - W/2, 0, max(0, D-W)). At the start the playhead moves
from left to center; in the middle the viewport pans under the centered
playhead; at the end the viewport stops and the playhead moves to the right.
Short recordings display all their data and the playhead traverses that range.
Reverse navigation follows the same continuous, reversible rule.

The navigator binds PositionSeconds rather than inferring it from viewport
start. A fixed-width thumb maps its available pixel travel to 0..D, so start,
end and short recordings are all reachable. Drag limiting begins at the actual
position and OS pointer synchronization uses this same mapping. Track clicks
continue to prepare and commit a complete frame atomically.

Use a focused ReviewPlaybackCursor overlay for visual style and plot geometry.
It draws a 1-DIP dashed neutral line and a small downward triangle, is not hit
testable and draws only inside the actual plot area. It has no reader, timer,
animation, filter or scientific state. Both time label modes share geometry.

Continuous movement follows accepted pointer positions directly. No autonomous
easing or trailing animation is added: those would show a different time from
the accepted seek target. Cold reads retain existing cache/loading semantics;
this is not a promise that uncached filtering is instantaneous.

Tests cover start/center/end, reverse movement, short recordings, navigator
pixel/time round trips, no initial offset, playback and atomic jumps. Offscreen
cursor rendering checks style/plot alignment at multiple sizes; hardware/GPU
performance and subjective usability remain manual acceptance.
