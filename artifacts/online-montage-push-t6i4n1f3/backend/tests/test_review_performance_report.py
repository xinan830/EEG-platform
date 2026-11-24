import importlib.util
from pathlib import Path

import pytest


SCRIPT = Path(__file__).resolve().parents[2] / "desktop-client/scripts/summarize_review_performance.py"
SPEC = importlib.util.spec_from_file_location("review_performance_report", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def perf(time, session, stage="FrameBuild", elapsed=20, points=0):
    return (f"2026-10-18T{time}+08:00 INFO review_perf review_session_id={session} "
            f"recording_id=recording stage={stage} source=FilterService revision=1 "
            f"start_s=0 end_s=10 channels=2 points={points} elapsed_ms={elapsed}\n")


def resource(time, session, managed, process=10):
    return (f"2026-10-18T{time}+08:00 INFO review_resource_perf review_session_id={session} "
            f"recording_id=recording process_id={process} position_s=1 playing=True "
            f"working_set_bytes=1000 private_bytes=2000 managed_bytes={managed} gc_heap_bytes=250 "
            "gen0=1 gen1=0 gen2=0 dispatcher_queue_ms=2.5\n")


def test_latest_session_is_selected_by_timestamp_not_recording_position_or_file_order(tmp_path):
    path = tmp_path / "desktop.log"
    path.write_text(perf("21:00:10", "new") + perf("20:00:00", "old"), encoding="utf-8")
    report = MODULE.summarize(path)
    assert report["review_session_id"] == "new"
    assert report["process_resources"] == {}


def test_request_counts_are_scoped_normalized_and_distinct_from_computation(tmp_path):
    path = tmp_path / "desktop.log"
    path.write_text(
        "2026-10-18T20:00:00+08:00 ERROR previous_failure\n"
        + perf("21:00:00", "target", elapsed=10)
        + "2026-10-18T21:00:01+08:00 INFO http_completed method=GET path=/api/runs/run/window-preview status=200\n"
        + "2026-10-18T21:00:02+08:00 INFO http_completed method=POST path=/api/live-filters/sessions/id-a/batches/binary status=200\n"
        + "2026-10-18T21:00:03+08:00 INFO http_completed method=POST path=/api/live-filters/sessions/id-b/batches/binary status=500\n"
        + "2026-10-18T21:00:04+08:00 WARN interval_warning\n"
        + perf("21:00:10", "target", stage="ChartBinding", elapsed=30, points=7500),
        encoding="utf-8",
    )
    report = MODULE.summarize(path, "target")
    assert report["observation_seconds"] == 10
    assert report["http_interval_total"] == 3
    assert report["http_interval_failures"] == 1
    assert report["log_interval_levels"] == {"WARN": 1}
    assert report["stages"]["ChartBinding"]["max_points"] == 7500
    assert report["sources"]["FrameBuild/FilterService"]["count"] == 1
    assert any(row["path"].endswith("window-preview") for row in report["http_interval_routes"])
    assert sum(row["count"] for row in report["http_interval_routes"]
               if "<session>" in row["path"]) == 2


def test_resource_observations_are_grouped_per_process_not_combined_into_false_growth(tmp_path):
    path = tmp_path / "desktop.log"
    path.write_text(
        perf("21:00:00", "target")
        + resource("21:00:01", "target", 300)
        + resource("21:00:11", "target", 200)
        + resource("21:00:12", "target", 9000, process=20)
        + resource("21:00:13", "other", 99999), encoding="utf-8")
    report = MODULE.summarize(path, "target")
    process = report["process_resources"]["10"]
    assert process["samples"] == 2
    assert process["managed_bytes"] == {"first": 300, "last": 200, "peak": 300, "delta": -100}
    assert process["dispatcher_queue"]["p95_ms"] == 2.5
    assert report["process_resources"]["20"]["samples"] == 1


def test_percentiles_and_source_statistics_do_not_mix_cache_with_service(tmp_path):
    path = tmp_path / "desktop.log"
    text = "".join(perf(f"21:00:{index:02}", "target", elapsed=index) for index in range(1, 21))
    text += perf("21:00:21", "target", elapsed=1).replace("source=FilterService", "source=FilterCache")
    path.write_text(text, encoding="utf-8")
    report = MODULE.summarize(path, "target")
    service = report["sources"]["FrameBuild/FilterService"]
    assert service == {"count": 20, "mean_ms": 10.5, "p95_ms": 19.0, "max_ms": 20.0}
    assert report["sources"]["FrameBuild/FilterCache"]["count"] == 1


def test_missing_session_is_explicit_error_not_empty_pass(tmp_path):
    path = tmp_path / "desktop.log"
    path.write_text("not a diagnostic log\n", encoding="utf-8")
    with pytest.raises(ValueError, match="No review_perf"):
        MODULE.summarize(path)
    with pytest.raises(ValueError, match="not found"):
        MODULE.summarize(path, "missing")
