import time

import numpy as np
import pytest

from app.algorithm_runtime.on_demand import DynamicWindowResponse
from app.bootstrap import build_builtin_registry
from app.services.on_demand_dynamic import DynamicWindowService
from app.services.scientific_compute_coordinator import ScientificComputeCoordinator
from on_demand_fixtures import configure_api, source_hashes


def test_full_and_on_demand_keep_healthy_channel_when_peer_has_invalid_power(tmp_path, monkeypatch):
    from app.scientific.primitives.gateway import ScientificSpectralGateway
    original = ScientificSpectralGateway.welch_annotated_channels

    def one_flat_channel(self, data, sfreq_hz, **kwargs):
        modified = np.array(data, copy=True)
        modified[:, 0] = 0
        return original(self, modified, sfreq_hz, **kwargs)

    monkeypatch.setattr(ScientificSpectralGateway, "welch_annotated_channels", one_flat_channel)
    client, recording, windows, runs, _ = configure_api(tmp_path, duration_s=6, labels=["F3", "F4"])
    config = {"algorithm_id": "rbp", "channel": "F3-REF", "channels": ["F3-REF", "F4-REF"],
              "mode": "dynamic", "time": {"start_s": 0, "end_s": 6},
              "dynamic_window_s": 5, "refresh_step_s": 1}
    try:
        response = client.post("/api/runs", json={"recording_id": recording.id,
            "analysis_type": "official_algorithm", "config": config})
        assert response.status_code == 202, response.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        response = client.post("/api/analysis-windows/ensure", json={"recording_id": recording.id,
            "algorithm_id": "rbp", "config": config, "cursor_sample": 1000,
            "request_id": "partial-from-full"})
        assert response.status_code in {200, 202}, response.text
        target = wait_window(client, response.json()["window_id"])
        assert target["state"] == "ready", target
        assert target["quality"] == "partial"
        preview = client.get(f"/api/runs/{run.run_id}/structured-preview")
        assert preview.status_code == 200, preview.text
        assert preview.json()["quality"]["status"] == "partial"
        for values in preview.json()["arrays"].values():
            assert values[-1][0] is None and np.isfinite(values[-1][1])
        for name in preview.json()["arrays"]:
            values = target["result"]["arrays"][name]
            assert values[0] is None and np.isfinite(values[1])
        assert "spectral_psd" not in target["result"]["arrays"]
        spectrum = windows.get(target["window_id"], include_projection_source=True)["result"]["arrays"]["spectral_psd"]
        assert np.all(np.asarray(spectrum[0]) == 0)
        assert np.isfinite(spectrum[1]).all()
        assert set(target["result"]["evidence"]["extensions"]["channel_failures"]["by_channel"]) == {"F3-REF"}
    finally:
        windows.close()
        client.close()


def wait_window(client, window_id, *, project_id=None):
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        params = {"project_id": project_id} if project_id else None
        response = client.get(f"/api/analysis-windows/{window_id}", params=params)
        assert response.status_code == 200, response.text
        value = response.json()
        if value["state"] not in {"queued", "preparing_source", "computing_window"}:
            return value
        time.sleep(0.01)
    pytest.fail("RBP window did not finish")


def test_imported_edf_runs_ready_rbp_window_with_verified_montage(tmp_path):
    import hashlib
    import json

    from test_recording_import import _client, _edf_bytes

    client, recordings, _ = _client(tmp_path)
    coordinator = ScientificComputeCoordinator()
    windows = DynamicWindowService(
        recordings, build_builtin_registry(), recordings.database_path, tmp_path / "artifacts",
        compute_coordinator=coordinator,
    )
    client.app.state.dynamic_window_service = windows
    project_id = client.put("/api/projects/desktop/imported-live-rbp", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "imported.edf", ("CH0001", "CH0002"), sample_frequency=200,
                        sample_count=6000)
    channel_snapshot = {
        "Id": "channel-config", "DeviceSignature": "ant-layout",
        "HardwareReferenceElectrodeLocation": "REF", "HardwareGroundElectrodeLocation": "GND",
        "Channels": [
            {"NativeChannelIndex": 8, "ExpectedKind": 2, "ElectrodeLabel": "Fz",
             "IsSelectedForDisplay": True, "DisplayOrder": 0},
            {"NativeChannelIndex": 3, "ExpectedKind": 2, "ElectrodeLabel": "Pz",
             "IsSelectedForDisplay": True, "DisplayOrder": 1},
        ],
    }
    montage = {
        "Id": "saved-montage", "DeviceSignature": "ant-layout",
        "ChannelConfigurationSnapshot": channel_snapshot, "AverageReferenceLabels": [],
        "DerivedChannels": [{"Name": "Fz-REF", "PositiveLabel": "Fz", "NegativeKind": 0,
                             "NegativeLabels": [], "DisplayOrder": 0}],
    }
    identity = {
        "exported_signal_sha256": hashlib.sha256(source).hexdigest(),
        "channel_configuration_snapshot_json": channel_snapshot,
        "channels": [
            {"export_label": "CH0001", "source_label": "Fz", "native_channel_index": 8, "kind": "2"},
            {"export_label": "CH0002", "source_label": "Pz", "native_channel_index": 3, "kind": "2"},
        ],
        "hardware": {
            "acquisition_montage_snapshot_json": {"Id": "saved-montage"},
            "montage_configuration_snapshot_json": montage,
            "montage_catalogue_snapshot_json": [montage],
        },
    }
    try:
        imported = client.post(
            f"/api/projects/{project_id}/recordings/import",
            data={"new_subject_local_code": "S-01", "export_channel_identity_json": json.dumps(identity)},
            files={"file": ("imported.edf", source)},
        )
        assert imported.status_code == 201, imported.text
        recording = recordings.require_recording(imported.json()["recording_id"])
        raw_response = client.get(
            f"/api/recordings/{recording.id}/raw-samples",
            params={"start_sample": 0, "end_sample": 10},
        )
        assert raw_response.status_code == 200, raw_response.text
        assert raw_response.json()["channels"] == ["Fz", "Pz"]
        profile = recording.montage_catalogue[0]
        config = {
            "algorithm_id": "rbp", "channel": "Fz-REF", "channels": ["Fz-REF"], "mode": "dynamic",
            "time": {"start_s": 0, "end_s": 30}, "dynamic_window_s": 10, "refresh_step_s": 1,
            "analysis_montage_id": profile["id"], "analysis_montage_revision": profile["revision"],
            "analysis_montage_fingerprint": profile["fingerprint"],
            "analysis_montage_outputs": [{"role": "Fz", "output": "Fz-REF", "unit": "V"}],
        }
        ensured = client.post("/api/analysis-windows/ensure", json={
            "recording_id": recording.id, "project_id": project_id, "algorithm_id": "rbp", "config": config,
            "cursor_sample": 6000, "request_id": "imported-edf-rbp-window",
        })
        assert ensured.status_code in {200, 202}, ensured.text
        window = wait_window(client, ensured.json()["window_id"], project_id=project_id)
        DynamicWindowResponse.model_validate(window)
        assert window["state"] == "ready", window
        assert window["identity"]["recording_id"] == recording.id
        assert window["identity"]["source_sha256"] == hashlib.sha256(source).hexdigest()
        assert window["range"]["start_sample"] == 4000
        assert window["range"]["end_sample"] == 6000
        assert window["range"]["start_s"] == 20
        assert window["range"]["end_s"] == 30
        assert window["result"]["evidence"]["extensions"]["channel_order"]["channels"] == ["Fz-REF"]
        assert window["result"]["array_units"]["alpha"] == "ratio"
        assert window["result"]["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"
        configuration = json.loads(window["scientific_configuration_json"])
        assert configuration["config"]["analysis_montage_id"] == "saved-montage"
        assert configuration["context"]["signal_mapping"]["channels"] == ["Fz-REF"]
        assert configuration["context"]["signal_mapping"]["analysis_montage"]["id"] == "saved-montage"
        raw_ensured = client.post("/api/analysis-windows/ensure", json={
            "recording_id": recording.id, "project_id": project_id, "algorithm_id": "rbp",
            "config": {"algorithm_id": "rbp", "channel": "Fz", "channels": ["Fz"], "mode": "dynamic",
                       "time": {"start_s": 0, "end_s": 30}, "dynamic_window_s": 10, "refresh_step_s": 1},
            "cursor_sample": 6000, "request_id": "imported-edf-raw-rbp-window",
        })
        assert raw_ensured.status_code in {200, 202}, raw_ensured.text
        raw_window = wait_window(client, raw_ensured.json()["window_id"], project_id=project_id)
        assert raw_window["state"] == "ready", raw_window
        assert raw_window["result"]["evidence"]["extensions"]["channel_order"]["channels"] == ["Fz"]
    finally:
        windows.close(wait=True)
        client.close()


@pytest.mark.parametrize("gap,rejected", [(False, False), (True, False), (False, True)])
def test_rbp_real_file_api_matches_every_full_run_point(tmp_path, gap, rejected):
    client, recording, service, runs, root = configure_api(tmp_path, gap=gap, rejected=rejected)
    before = source_hashes(root)
    config = {"algorithm_id": "rbp", "channel": "F3-REF", "channels": ["F3-REF"], "mode": "dynamic",
              "time": {"start_s": 0, "end_s": recording.duration_s}, "dynamic_window_s": 5, "refresh_step_s": 1}
    try:
        response = client.post("/api/runs", json={"recording_id": recording.id,
            "analysis_type": "official_algorithm", "config": config})
        assert response.status_code == 202, response.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        structured = run.result_summary["structured"]
        frames = structured["windows"]
        assert frames
        for index, frame in enumerate(frames):
            ensured = client.post("/api/analysis-windows/ensure", json={"recording_id": recording.id,
                "algorithm_id": "rbp", "config": config, "cursor_sample": frame["end_sample"],
                "request_id": f"rbp-window-{index}"})
            assert ensured.status_code in {200, 202}, ensured.text
            window = wait_window(client, ensured.json()["window_id"])
            DynamicWindowResponse.model_validate(window)
            expected = {"Complete": "ready", "Partial": "ready", "Rejected": "rejected", "Unavailable": "unavailable"}
            assert window["state"] == expected[frame["state"]], window
            assert window["range"]["start_s"] == frame["start_s"]
            assert window["range"]["end_s"] == frame["end_s"]
            if window["state"] == "ready":
                for name in ("delta", "theta", "alpha", "beta", "gamma"):
                    artifact = runs.list_artifacts(run.run_id)[0]
                    artifact_array = runs.base.artifacts.read_npz(artifact)[name]
                    np.testing.assert_allclose(window["result"]["arrays"][name], [artifact_array[index, 0]], rtol=1e-12, atol=1e-12)
                    assert window["result"]["array_units"][name] == "ratio"
                assert window["result"]["evidence"]["extensions"]["channel_order"]["channels"] == ["F3-REF"]
                assert window["result"]["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"
            else:
                assert window["result"] is None
                assert window["failure"] is not None
            client.delete(f"/api/analysis-windows/subscriptions/{ensured.json()['subscription_id']}")
        assert source_hashes(root) == before
    finally:
        service.close(wait=True)
        client.close()
