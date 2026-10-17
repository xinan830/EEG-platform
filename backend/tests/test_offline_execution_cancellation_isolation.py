"""The two offline execution paths must not cancel each other."""

from dataclasses import replace

from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.models.run import RunCreateRequest
from app.services.dynamic_window_resources import DynamicWindowResourcePolicy
from on_demand_fixtures import configure_api, psd_config
from test_on_demand_psd_api import wait_window


def test_cancelled_full_run_does_not_cancel_a_new_on_demand_window(tmp_path):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    try:
        queued = runs.enqueue(RunCreateRequest(
            recording_id=recording.id, analysis_type="official_algorithm", config=psd_config(recording),
        ))
        cancelled = runs.cancel(queued.run_id)
        assert cancelled.cancel_requested is True

        request = DynamicWindowRequest(
            recording_id=recording.id, algorithm_id="psd", config=psd_config(recording), cursor_sample=1000,
        )
        ensured = windows.ensure(request)
        ready = wait_window(client, ensured["window_id"])
        assert ready["state"] == "ready"
    finally:
        windows.close()
        windows._executor.shutdown(wait=True)
        client.close()


def test_failed_on_demand_window_does_not_block_full_run(tmp_path):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    original_policy = windows.resource_policy
    try:
        windows.resource_policy = replace(original_policy, max_preparation_bytes=1)
        request = DynamicWindowRequest(
            recording_id=recording.id, algorithm_id="psd", config=psd_config(recording), cursor_sample=1000,
        )
        failed = wait_window(client, windows.ensure(request)["window_id"])
        assert failed["state"] == "failed"

        windows.resource_policy = DynamicWindowResourcePolicy()
        queued = runs.enqueue(RunCreateRequest(
            recording_id=recording.id, analysis_type="official_algorithm", config=psd_config(recording),
        ))
        completed = runs.process_next()
        assert completed is not None
        assert completed.run_id == queued.run_id
        assert completed.status.value == "completed"
    finally:
        windows.close()
        windows._executor.shutdown(wait=True)
        client.close()
