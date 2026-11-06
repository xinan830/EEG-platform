"""Synthetic admission/read costs; no production recording or server is touched.

Run from the repository root with backend/.venv/Scripts/python.exe and this path.
The comparison loads only the old catalogue/read methods from the named Git
revision; it does not restore files or execute an old scientific algorithm.
"""

import ast
import gc
import json
import logging
from pathlib import Path
import statistics
import subprocess
import sys
import tempfile
import textwrap
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.core.performance_trace import performance_trace
from app.persistence.repositories.dynamic_execution_state import DynamicExecutionStateReader
from app.services.recordings import RecordingService
import app.services.recordings as recording_module
import app.persistence.repositories.dynamic_attempt as attempt_module
from test_acquisition_analysis_signals import fixture_recording, compatible_average_profile
from test_on_demand_admission import admission


def old_source(path, revision):
    return subprocess.check_output(["git", "show", f"{revision}:{path}"], encoding="utf-8")


def old_method(source, class_name, method_name, namespace):
    tree = ast.parse(source)
    cls = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == class_name)
    method = next(node for node in cls.body if isinstance(node, ast.FunctionDef) and node.name == method_name)
    scope = dict(namespace)
    exec(textwrap.dedent(ast.get_source_segment(source, method)), scope)
    return scope[method_name]


def measure(action, repeats):
    elapsed, connections = [], []
    for _ in range(repeats):
        with performance_trace("synthetic_benchmark") as trace:
            started = time.perf_counter()
            action()
            elapsed.append((time.perf_counter() - started) * 1000)
        connections.append(trace.metrics.get("database_connect_calls", 0))
    return {"median_ms": round(statistics.median(elapsed), 3),
            "max_ms": round(max(elapsed), 3), "connections_per_iteration": connections}


def main():
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], encoding="utf-8").strip()
    logging.getLogger("brain_platform.performance").setLevel(logging.INFO)
    before_namespace = {"__name__": "catalogue_before"}
    exec(old_source("backend/app/services/analysis_montage_snapshots.py", revision), before_namespace)
    old_require = old_method(old_source("backend/app/services/recordings.py", revision),
                             "RecordingService", "require_recording", vars(recording_module))
    old_active = old_method(old_source("backend/app/persistence/repositories/dynamic_attempt.py", revision),
                            "DynamicAttemptRepositoryMixin", "is_attempt_active", vars(attempt_module))
    with tempfile.TemporaryDirectory(prefix="window-infrastructure-") as temporary:
        root = Path(temporary)
        _, manifest = fixture_recording(root / "recording")
        profiles = [dict(compatible_average_profile(manifest), Id=f"saved-{index}") for index in range(30)]
        after = RecordingService(root / "stored", root / "catalog.sqlite3")
        recording = after.register_wpf_recording(str(root / "recording"), signal_mode="acquisition_montage",
                                                 analysis_montage_profiles=profiles)
        before = RecordingService(root / "stored", root / "catalog.sqlite3")
        before._analysis_montage_snapshots = before_namespace["AnalysisMontageSnapshots"](before.database_path)
        expected = old_require(before, recording.id).montage_catalogue
        assert after.require_recording(recording.id).montage_catalogue == expected
        results = {"baseline_revision": revision, "snapshot_rows": 30, "catalogue_repeats": 20}
        results["catalogue_before"] = measure(lambda: old_require(before, recording.id), 20)
        results["catalogue_after"] = measure(lambda: after.require_recording(recording.id), 20)

        worker_root = root / "worker"
        worker_root.mkdir()
        fixture = admission.__wrapped__(worker_root)
        service, request, started, release = next(fixture)
        reader = None
        try:
            response = service.ensure(request)
            assert started.wait(2)
            attempt = service.repository.get_active_attempt(response["window_id"])

            def old_checks():
                for _ in range(100):
                    assert service.repository.get_window(response["window_id"]).state != "cancelled"
                    assert old_active(service.repository, attempt.attempt_id, attempt.lease_token)

            def new_checks():
                for _ in range(100):
                    assert reader.read() == "active"

            results["100_checks_before"] = measure(old_checks, 5)
            reader = DynamicExecutionStateReader(service.repository.database_path, response["window_id"],
                                                 attempt.attempt_id, attempt.lease_token)
            results["100_checks_after"] = measure(new_checks, 5)
            results["reader_setup_connections"] = 1
        finally:
            if reader is not None:
                reader.close()
            release.set()
            service.close(wait=True)
            try:
                next(fixture)
            except StopIteration:
                pass
            gc.collect()
        print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
