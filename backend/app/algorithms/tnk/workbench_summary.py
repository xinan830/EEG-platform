"""Display statistics over returned scores; never reprocess EEG or fill missing windows."""
import math

STATE_DEFINITIONS = (
    ("自然调整", 55, "#9E9E9E"), ("平稳专注", 65, "#42A5F5"),
    ("轻度放松", 75, "#66BB6A"), ("深度沉浸", math.inf, "#8E63C7"),
)


def score_state_summary(summary: dict, cursor_s: float) -> dict:
    evidence = summary.get("structured", {}).get("official", {}).get("tnk_evidence", {})
    points = evidence.get("points", [])
    durations = [0.0] * len(STATE_DEFINITIONS)
    previous_time = None
    valid_points = 0
    timeline = []

    def states():
        total = sum(durations)
        return [{"label": label, "color": color, "duration_s": duration,
                 "percent": duration / total * 100 if total > 0 else 0.0}
                for (label, _, color), duration in zip(STATE_DEFINITIONS, durations)]
    for point in points:
        children = point.get("extensions", {}).get("tnk_evidence", {}).get("children", {})
        rows = children.get("tnk_composite_meditation", [])
        if not rows:
            previous_time = None
            continue
        row = rows[0]
        end = row.get("actual_range", {}).get("end_s")
        value = row.get("value")
        if not isinstance(end, (float, int)) or not math.isfinite(end):
            previous_time = None
            continue
        if end > cursor_s:
            break
        if not isinstance(value, (float, int)) or not math.isfinite(value):
            previous_time = None
            continue
        valid_points += 1
        # ANT uses recording-time deltas; no extrapolation before the first
        # returned score or across an unavailable window.
        delta = max(0.0, end - previous_time) if previous_time is not None else 0.0
        previous_time = end
        index = next(index for index, (_, upper, _) in enumerate(STATE_DEFINITIONS) if value < upper)
        durations[index] += delta
        timeline.append({"cursor_s": end, "valid_duration_s": sum(durations), "states": states()})
    total = sum(durations)
    return {
        "cursor_s": cursor_s, "valid_points": valid_points, "valid_duration_s": total,
        "states": states(), "timeline": timeline,
    }
