"""Summarize one review session without exporting signal values or full log lines."""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from datetime import datetime
import json
import math
from pathlib import Path
from statistics import mean


def _events(path: Path):
    with path.open(encoding="utf-8-sig") as stream:
        for line in stream:
            parts = line.split()
            if len(parts) < 3:
                continue
            try:
                timestamp = datetime.fromisoformat(parts[0])
            except ValueError:
                continue
            fields = dict(part.split("=", 1) for part in parts[3:] if "=" in part)
            yield timestamp, parts[1], parts[2], fields


def _timings(values: list[float]) -> dict:
    ordered = sorted(values)
    return {
        "count": len(values),
        "mean_ms": round(mean(values), 3),
        "p95_ms": ordered[math.ceil(len(values) * 0.95) - 1],
        "max_ms": ordered[-1],
    }


def summarize(path: Path, session: str | None = None) -> dict:
    if session is None:
        latest = max(
            ((time, fields["review_session_id"]) for time, _, event, fields in _events(path)
             if event == "review_perf" and "review_session_id" in fields),
            default=None,
        )
        if latest is None:
            raise ValueError("No review_perf session found in this log")
        session = latest[1]

    stages = defaultdict(list)
    sources = defaultdict(list)
    points = defaultdict(int)
    resources = defaultdict(list)
    times = []
    recordings = set()
    for time, _, event, fields in _events(path):
        if fields.get("review_session_id") != session:
            continue
        if event not in {"review_perf", "review_resource_perf"}:
            continue
        times.append(time)
        recordings.add(fields["recording_id"])
        if event == "review_perf":
            stage, source = fields["stage"], fields["source"]
            elapsed = float(fields["elapsed_ms"])
            stages[stage].append(elapsed)
            sources[f"{stage}/{source}"].append(elapsed)
            points[stage] = max(points[stage], int(fields["points"]))
        else:
            resources[fields["process_id"]].append((time, fields))
    if not times:
        raise ValueError("Requested review session was not found")

    first, last = min(times), max(times)
    requests = Counter()
    errors = Counter()
    failures = 0
    for time, level, event, fields in _events(path):
        if not first <= time <= last:
            continue
        if level in {"WARN", "ERROR"}:
            errors[level] += 1
        if event != "http_completed":
            continue
        route = fields.get("path", "unknown")
        # Filter session IDs are transport details, not aggregation dimensions.
        prefix = "/api/live-filters/sessions/"
        if route.startswith(prefix):
            remainder = route[len(prefix):].partition("/")[2]
            route = prefix + "<session>" + ("/" + remainder if remainder else "")
        status = fields.get("status", "unknown")
        requests[(fields.get("method", "unknown"), route, status)] += 1
        if status.startswith(("4", "5")):
            failures += 1

    process_summaries = {}
    for process_id, samples in resources.items():
        samples.sort(key=lambda row: row[0])
        process_summary = {
            "samples": len(samples),
            "dispatcher_queue": _timings([float(row[1]["dispatcher_queue_ms"]) for row in samples]),
        }
        for key in ("working_set_bytes", "private_bytes", "managed_bytes", "gc_heap_bytes",
                    "gen0", "gen1", "gen2"):
            values = [int(row[1][key]) for row in samples]
            process_summary[key] = {
                "first": values[0], "last": values[-1], "peak": max(values),
                "delta": values[-1] - values[0],
            }
        process_summaries[process_id] = process_summary

    return {
        "review_session_id": session,
        "recording_ids": sorted(recordings),
        "first": first.isoformat(), "last": last.isoformat(),
        "observation_seconds": round((last - first).total_seconds(), 3),
        "stages": {key: {**_timings(values), "max_points": points[key]}
                   for key, values in sorted(stages.items())},
        "sources": {key: _timings(values) for key, values in sorted(sources.items())},
        "process_resources": process_summaries,
        "http_interval_total": sum(requests.values()),
        "http_interval_failures": failures,
        "http_interval_routes": [dict(method=method, path=route, status=status, count=count)
                                 for (method, route, status), count in sorted(requests.items())],
        "log_interval_levels": dict(errors),
        "limitations": [
            "HTTP and warning/error counts cover the wall-clock interval, not causal session attribution.",
            "Observation duration does not prove uninterrupted playback or cover the whole recording.",
            "ChartBinding and dispatcher queue time are not GPU render time or frame rate.",
            "FrameBuild point count is a placeholder; use ChartBinding to observe bound points.",
            "Process memory includes other workspaces; GC heap size describes the last GC.",
            "Absent resource samples mean memory stability is unverified, not zero usage.",
        ],
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", type=Path)
    parser.add_argument("--session", help="Defaults to the review session with the latest timestamp")
    args = parser.parse_args()
    try:
        report = summarize(args.log, args.session)
    except (OSError, ValueError, KeyError) as error:
        parser.exit(2, f"Cannot summarize review log: {error}\n")
    print(json.dumps(report, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
