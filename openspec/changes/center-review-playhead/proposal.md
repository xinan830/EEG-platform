# Center review playhead

## Why
Review currently follows the playhead at one quarter of the visible window.
The bottom navigator converts a viewport start back into a playhead using that
offset, excluding both recording endpoints and jumping on the first drag.
The red solid cursor spans the chart host rather than the real plot area.

## What changes
- The viewport follows recording position at its center, clamped to recording bounds.
- The bottom navigator selects actual recording time, including 0 and duration.
- A dark-gray dashed playhead with a small top triangle replaces the red solid line.
- Cursor geometry uses the actual SciChart plot area; time display mode never moves it.

## Scope
WPF review navigation and presentation only. Raw samples, time mapping, filters,
analysis requests, acquisition and backend algorithms are unchanged. Existing
bounded drag scheduling, atomic track jumps and rate limits remain in force.
