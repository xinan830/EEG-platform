"""Inspect persisted real PSD/RBP window artifacts without reexecuting science."""

from copy import deepcopy

import numpy as np
import pytest

from app.api.custom_frequency_inspection import router
from app.services.custom_frequency_inspection import CustomFrequencyInspectionService
from app.scientific.primitives.spectral import band_power
from on_demand_fixtures import configure_api, source_hashes
from test_on_demand_rbp_api import wait_window


def test_projection_reads_saved_current_window_only_and_preserves_five_band_outputs(tmp_path, monkeypatch, caplog):
    caplog.set_level("INFO")
    from app.services.algorithm_recording_context import _RecordingAlgorithmContext
    from app.scientific.primitives.gateway import ScientificSpectralGateway
    from app.services.topomap import TopomapService
    client, recording, windows, _, root = configure_api(tmp_path, duration_s=12)
    client.app.include_router(router)
    client.app.state.custom_frequency_inspection_service = CustomFrequencyInspectionService(windows)
    before = source_hashes(root)
    saved = {}
    configs = {}
    try:
        for algorithm in ("psd", "rbp"):
            config = {"algorithm_id": algorithm, "channels": ["F3-REF", "F7-REF"],
                      "mode": "dynamic", "time": {"start_s": 0, "end_s": 12},
                      "dynamic_window_s": 10, "refresh_step_s": 1, "notch_hz": 50}
            if algorithm == "rbp":
                config["channel"] = "F3-REF"
            else:
                config.update(low_hz=1, high_hz=50)
            configs[algorithm] = config
            response = client.post("/api/analysis-windows/ensure", json={
                "recording_id": recording.id, "algorithm_id": algorithm, "config": config,
                "cursor_sample": 2000, "request_id": f"custom-{algorithm}"})
            assert response.status_code in {200, 202}, response.text
            saved[algorithm] = wait_window(client, response.json()["window_id"])
            assert saved[algorithm]["state"] == "ready", saved[algorithm]
        snapshot = deepcopy(saved)
        windows._artifact_store.clear_cache()
        with monkeypatch.context() as scoped:
            scoped.setattr(windows._artifact_store, "_decode",
                lambda *_, **__: pytest.fail("scope rejection must precede artifact access"))
            for payload in saved.values():
                with pytest.raises(KeyError, match="project scope"):
                    windows.get(payload["window_id"], project_id="another-project",
                                include_projection_source=True)

        def forbidden(*args, **kwargs):
            pytest.fail("Projection must not read EEG or execute Welch")
        monkeypatch.setattr(_RecordingAlgorithmContext, "load_spectrum", forbidden)
        monkeypatch.setattr(ScientificSpectralGateway, "welch_annotated_channels", forbidden)
        monkeypatch.setattr(ScientificSpectralGateway, "welch", forbidden)
        monkeypatch.setattr(TopomapService, "_interpolate", forbidden)
        monkeypatch.setattr("app.services.topomap.resolve_dynamic_signal_provenance", forbidden)
        pair = {"psd_window_id": saved["psd"]["window_id"], "rbp_window_id": saved["rbp"]["window_id"]}
        response = client.post("/api/analysis-windows/custom-frequency/bins", json=pair)
        assert response.status_code == 200, response.text
        bins = response.json()
        assert len(bins["frequency_hz"]) == 197
        assert bins["frequency_hz"][:3] == [1.0, 1.25, 1.5]
        assert bins["bin_intervals"][0] == {"low_hz": 1.0, "high_hz": 1.125}
        assert bins["bin_intervals"][-1] == {"low_hz": 49.875, "high_hz": 50.0}
        assert [row["channel"] for row in bins["channels"]] == ["F3-REF", "F7-REF"]
        assert "spectral_psd" not in saved["rbp"]["result"]["arrays"]
        assert "frequency_hz" not in saved["rbp"]["result"]["axes"]
        spectrum = windows.get(saved["rbp"]["window_id"], include_projection_source=True)["result"]
        for index, row in enumerate(bins["channels"]):
            assert sum(row["rbp_absolute_power_uv2"]) == pytest.approx(row["rbp_denominator_uv2"])
            assert sum(row["rbp_relative_power_percent"]) == pytest.approx(100)
            np.testing.assert_allclose(row["psd_density_uv2_per_hz"],
                np.asarray(saved["psd"]["result"]["arrays"]["psd"][index]) * 1e12)
        with monkeypatch.context() as scoped:
            scoped.setattr(windows._artifact_store, "_decode",
                lambda *_, **__: pytest.fail("range drag must reuse the validated saved window"))
            for low, high in ((1, 3), (1.1, 2.9), (1, 50)):
                response = client.post("/api/analysis-windows/custom-frequency/range",
                    json={**pair, "low_hz": low, "high_hz": high})
                assert response.status_code == 200, response.text
                row = response.json()["channels"][0]
                expected = band_power(spectrum["axes"]["frequency_hz"],
                                      spectrum["arrays"]["spectral_psd"][0], low, high) * 1e12
                assert row["rbp_absolute_power_uv2"] == pytest.approx(expected)
                assert row["amplitude_rms_uv"] == pytest.approx(np.sqrt(expected))
        for algorithm, payload in saved.items():
            assert windows.get(payload["window_id"]) == snapshot[algorithm]
        warm = client.post("/api/analysis-windows/ensure", json={
            "recording_id": recording.id, "algorithm_id": "rbp", "config": configs["rbp"],
            "cursor_sample": 2000, "request_id": "cached-public-rbp"})
        assert warm.status_code == 200, warm.text
        assert "spectral_psd" not in warm.json()["result"]["arrays"]
        warm_subscription = warm.json()["subscription_id"]
        released = client.delete(f"/api/analysis-windows/subscriptions/{warm_subscription}")
        assert released.status_code == 200
        messages = [record.getMessage() for record in caplog.records]
        admission = next(message for message in messages if "dynamic_window_admission_perf" in message
                         and "request_id=cached-public-rbp" in message)
        assert "lock_wait_ms=" in admission and "recording_metadata_ms=" in admission
        assert "montage_catalogue_ms=" in admission and "montage_snapshot_rows=" in admission
        assert "database_connect_calls=" in admission
        api = next(message for message in messages if "dynamic_window_api_perf" in message
                   and "analysis_request_id=cached-public-rbp" in message)
        assert "audit_ms=" in api and "response_encode_ms=" in api and "response_bytes=" in api
        assert any("dynamic_subscription_release_perf" in message and f"subscription_id={warm_subscription}" in message
                   and "service_ms=" in message and "audit_ms=" in message for message in messages)
        assert any("custom_frequency_bins request_id=" in message and "response_encode_ms=" in message
                   and "response_bytes=" in message for message in messages)
        assert any("dynamic_window_worker_started" in message and "executor_queue_ms=" in message
                   and "algorithm_id=rbp" in message for message in messages)
        assert any("dynamic_window_worker_perf" in message and "algorithm_id=rbp" in message
                   and "cancellation_state_read_calls=" in message
                   and "cancellation_state_read_ms=" in message for message in messages)
        assert source_hashes(root) == before
    finally:
        windows.close(wait=True)
        client.close()
