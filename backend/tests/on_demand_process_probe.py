"""Subprocess harness for abrupt OS termination and autonomous lease recovery."""

import json
import sys
import threading
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.algorithms.psd.runner import PsdAlgorithm
from app.bootstrap import build_builtin_registry
from app.services.on_demand_dynamic import DynamicWindowService
from app.services.recordings import RecordingService
from on_demand_fixtures import psd_config


def main():
    scratch, recording_id, mode = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
    recordings = RecordingService(scratch / "stored", scratch / "catalog.sqlite3")
    if mode == "block":
        def blocked(self, inputs, config, plan, identity, check_cancelled=None):
            print(json.dumps({"checkpoint": "executing", "pid": __import__("os").getpid()}), flush=True)
            threading.Event().wait()
        PsdAlgorithm.execute_window = blocked
    service = DynamicWindowService(recordings, build_builtin_registry(), recordings.database_path, scratch / "artifacts")
    try:
        if mode == "block":
            recording = recordings.require_recording(recording_id)
            response = service.ensure(DynamicWindowRequest(recording_id=recording_id, algorithm_id="psd",
                config=psd_config(recording), cursor_sample=1000, request_id="crash-probe"))
            print(json.dumps({"window_id": response["window_id"]}), flush=True)
            threading.Event().wait()
        else:
            window_id = sys.argv[4]
            deadline = time.monotonic() + 20
            while time.monotonic() < deadline:
                response = service.get(window_id)
                if response["state"] in {"ready", "failed", "rejected", "unavailable"}:
                    print(json.dumps(response), flush=True)
                    return
                time.sleep(0.02)
            raise TimeoutError("autonomous recovery did not finish")
    finally:
        service.close()
        service._executor.shutdown(wait=True)


if __name__ == "__main__":
    main()
