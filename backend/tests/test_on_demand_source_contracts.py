"""Scientific cache isolation and invalid source-metadata failure semantics."""

import json
import sqlite3

import pytest

from app.algorithm_runtime.on_demand import DynamicWindowRequest
from on_demand_fixtures import configure_api, psd_config, source_hashes
from test_on_demand_psd_api import wait_window


@pytest.mark.parametrize("changes", [
    {"channel": "F7-REF"}, {"low_hz": 2}, {"high_hz": 40}, {"notch_hz": 50},
    {"dynamic_window_s": 10}, {"refresh_step_s": 2},
])
def test_full_scientific_configuration_never_reuses_another_window(tmp_path, changes):
    client, recording, service, _, _ = configure_api(tmp_path)
    config = psd_config(recording)
    request = DynamicWindowRequest(recording_id=recording.id, algorithm_id="psd", config=config, cursor_sample=2000)
    try:
        first = service.ensure(request)
        assert wait_window(client, first["window_id"])["state"] == "ready"
        changed = service.ensure(request.model_copy(update={"config": config | changes}))
        assert changed["session_id"] != first["session_id"]
        assert changed["window_id"] != first["window_id"]
        assert wait_window(client, changed["window_id"])["state"] == "ready"
        assert service.ensure(request)["window_id"] == first["window_id"]
    finally:
        service.close()
        service._executor.shutdown(wait=True)
        client.close()


@pytest.mark.parametrize("identity", ["source", "policy", "build"])
def test_source_policy_and_implementation_build_isolate_published_windows(tmp_path, identity):
    client, recording, service, _, _ = configure_api(tmp_path)
    request = DynamicWindowRequest(recording_id=recording.id, algorithm_id="psd", config=psd_config(recording), cursor_sample=1000)
    try:
        first = service.ensure(request)
        ready = wait_window(client, first["window_id"])
        if identity == "source":
            with sqlite3.connect(service.repository.database_path) as connection:
                connection.execute("UPDATE recordings SET source_sha256=? WHERE id=?", ("new-source-identity", recording.id))
        elif identity == "policy":
            service.capabilities.get("psd", service.registry.get("psd").manifest.scientific_version).window_policy_version = "test-policy-v2"
        else:
            service._identity_resolver.implementation_build = "test-build-v2"
        changed = service.ensure(request)
        assert changed["session_id"] != first["session_id"]
        assert changed["window_id"] != first["window_id"]
        assert wait_window(client, changed["window_id"])["state"] == "ready"
        assert service.get(first["window_id"])["result"] == ready["result"]
    finally:
        service.close()
        service._executor.shutdown(wait=True)
        client.close()


@pytest.mark.parametrize("damage", ["json", "missing_snapshot", "invalid_unit"])
def test_malformed_historical_manifest_cannot_publish_a_fake_ready_result(tmp_path, damage):
    client, recording, service, _, root = configure_api(tmp_path)
    manifest_path = root / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if damage == "json":
        manifest_path.write_text("{invalid-json", encoding="utf-8")
    else:
        if damage == "missing_snapshot":
            manifest["HardwareConfiguration"].pop("montage_configuration_snapshot_json")
        else:
            manifest["Channels"][0]["Unit"] = "unknown-unit"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    corrupted = source_hashes(root)
    try:
        ensured = service.ensure(DynamicWindowRequest(recording_id=recording.id, algorithm_id="psd",
            config=psd_config(recording), cursor_sample=1000))
        result = wait_window(client, ensured["window_id"])
        assert result["state"] == "unavailable"
        assert result["failure"]["code"] == "PSD_WINDOW_UNAVAILABLE"
        assert result["failure"]["message"]
        assert result["result"] is None
        assert not list(service.artifacts_dir.iterdir())
        assert source_hashes(root) == corrupted
    finally:
        service.close()
        service._executor.shutdown(wait=True)
        client.close()
