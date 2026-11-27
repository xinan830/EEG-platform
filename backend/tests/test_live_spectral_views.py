from time import monotonic, sleep
from uuid import uuid4

import numpy as np
import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.scientific.contracts.spectrogram import density_uv_to_db
from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY as spectral
from app.services.live_analysis.live_rbp_algorithm import LiveRbpAlgorithm
from app.services.live_analysis.live_recording_access import LiveRecordingAccessService
from app.services.analysis.results.result_scalar_inputs import RESULT_SCALAR_ADAPTERS
from app.services.analysis.results.topomap import TopomapService


RATE = 250
NAMES = ["F3", "F4", "C3", "C4"]
POSITIONS = dict(zip(NAMES, [
    {"x": -0.3, "y": 0.3}, {"x": 0.3, "y": 0.3},
    {"x": -0.4, "y": 0}, {"x": 0.4, "y": 0},
], strict=True))


@pytest.fixture
def live():
    session = uuid4().hex
    path = f"/api/live-recordings/sessions/{session}"
    client = TestClient(app)
    assert client.post("/api/live-recordings/sessions", json={
        "session_id": session, "stream_revision": "stream-v1", "schema_revision": "schema-v1",
        "sampling_rate_hz": RATE, "channel_names": NAMES, "channel_units": ["V"] * len(NAMES),
        "capacity_samples": 12 * RATE,
    }).status_code == 201
    try:
        yield client, path, session
    finally:
        client.delete(path)


def _signal(count):
    times = np.arange(count) / RATE
    return np.column_stack([
        15e-6 * np.sin(2 * np.pi * 10 * times + index) +
        (5 + index) * 1e-6 * np.sin(2 * np.pi * (18 + index) * times)
        for index in range(len(NAMES))
    ])


def _append(client, path, start, values):
    response = client.post(path + f"/batches/binary?first_sample_counter={start}&sample_count={len(values)}",
                           content=values.astype("<f8").tobytes())
    assert response.status_code == 200, response.text
    return response.json()["updates"]


def _start(client, path, algorithm, config):
    response = client.post(path + "/algorithms/" + algorithm, json={"config": config})
    assert response.status_code == 201, response.text
    return response.json()


def _wait(client, path, algorithm, revision=0):
    deadline = monotonic() + 5
    while monotonic() < deadline:
        response = client.get(path + "/algorithms/" + algorithm)
        assert response.status_code == 200, response.text
        result = response.json()
        if result["revision"] > revision:
            return result
        sleep(0.005)
    pytest.fail("Timed out waiting for the live result")


def test_live_psd_and_stft_use_review_core_and_latest_window_on_switch(live):
    client, path, _ = live
    values = _signal(13 * RATE)
    first = 1000
    _append(client, path, first, values[:2 * RATE])
    assert _start(client, path, "psd", {"channels": ["F4"]})["state"] == "warming_up"
    _append(client, path, first + 2 * RATE, values[2 * RATE:12 * RATE])
    psd = _wait(client, path, "psd")
    assert psd["state"] == "ready", psd
    assert (psd["start_s"], psd["end_s"]) == (2, 12)
    prepared = spectral.preprocess(values[2 * RATE:12 * RATE, 1:2], RATE, low_hz=1, high_hz=50, notch_hz=50)
    expected = spectral.welch_annotated_channels(prepared, RATE, low_hz=1, high_hz=50)
    payload = psd["payload"]
    assert (payload["unit"], payload["channels"]) == ("V^2/Hz", ["F4"])
    np.testing.assert_array_equal(payload["frequencies_hz"], expected.freqs)
    np.testing.assert_allclose(payload["values"], expected.psd, rtol=1e-12, atol=0)

    assert client.delete(path + "/algorithms/psd").status_code == 204
    _start(client, path, "stft", {"channels": ["F3"]})
    stft = _wait(client, path, "stft")
    assert stft["state"] == "ready", stft
    assert (stft["start_s"], stft["end_s"]) == (2, 12)
    prepared = spectral.preprocess(values[2 * RATE:12 * RATE, :1], RATE, low_hz=1, high_hz=50, notch_hz=50)
    times, frequencies, density, _ = spectral.spectrogram(prepared, RATE, low_hz=1, high_hz=50)
    payload = stft["payload"]
    assert (payload["unit"], payload["channels"]) == ("dB re 1 uV^2/Hz", ["F3"])
    np.testing.assert_array_equal(payload["times_s"], times + 2)
    np.testing.assert_array_equal(payload["frequencies_hz"], frequencies)
    np.testing.assert_allclose(payload["values"], density_uv_to_db(density[:, 0, :] * 1e12))
    updates = _append(client, path, first + 12 * RATE, values[12 * RATE:])
    next_stft = next((update for update in updates if update["revision"] > stft["revision"]), None)
    if next_stft is None:
        next_stft = _wait(client, path, "stft", stft["revision"])
    assert (next_stft["start_s"], next_stft["end_s"]) == (3, 13)


@pytest.mark.parametrize("metric,field_id", [
    ("psd", "psd.band-mean-density.alpha"),
    ("rbp", "rbp.relative.alpha"),
    ("rbp", "rbp.absolute.alpha"),
    ("rbp", "rbp.amplitude.alpha"),
])
def test_live_maps_match_review_projection_and_report_missing_positions(live, metric, field_id):
    client, path, session = live
    values = _signal(10 * RATE)
    _append(client, path, 1000, values)
    algorithm = metric + "-topomap"
    config = {"spectrum": {"channels": NAMES}, "field_id": field_id, "positions": POSITIONS}
    _start(client, path, algorithm, config)
    result = _wait(client, path, algorithm)
    assert result["state"] == "ready", result
    if metric == "rbp":
        source = LiveRecordingAccessService().create(session, "stream-v1", "schema-v1", RATE, NAMES, ["V"] * 4, len(values))
        source.append(1000, values, len(values))
        rbp = LiveRbpAlgorithm()
        requested, _ = rbp.configure(source, config["spectrum"])
        output = rbp.calculate(session, source.read_samples(1000, 3500, NAMES), requested, 1000)
        payload = {"arrays": output.values, "array_units": output.units}
        quality = output.quality
    else:
        prepared = spectral.preprocess(values, RATE, low_hz=1, high_hz=50, notch_hz=50)
        expected = spectral.welch_annotated_channels(prepared, RATE, low_hz=1, high_hz=50)
        payload = {"axes": {"frequency_hz": expected.freqs.tolist()}, "axis_units": {"frequency_hz": "Hz"},
                   "arrays": {"psd": expected.psd.tolist()}, "array_units": {"psd": "V^2/Hz"}}
        quality = dict(zip(NAMES, expected.evidence["channel_quality"], strict=True))
    payload.update(channel_order=NAMES, evidence={"extensions": {"channel_quality": {"by_channel": quality}}})
    fields = RESULT_SCALAR_ADAPTERS[metric].build_input(
        recording_id=session, payload=payload, window_start_s=0, window_end_s=10, field_ids=[field_id],
    )
    expected_field = TopomapService(None, None).project(session, fields, signal_provenance={
        "map_positions": {name: {**position, "status": "available"} for name, position in POSITIONS.items()},
    }).fields[0]
    field = result["payload"]["field"]
    assert field["unit"] == expected_field.unit
    assert field["included_channels"] == expected_field.included_channels == NAMES
    np.testing.assert_allclose(np.asarray(field["grid"]["values"], dtype=float),
                               np.asarray(expected_field.grid.values, dtype=float), rtol=1e-12, atol=0)
    assert client.delete(path + "/algorithms/" + algorithm).status_code == 204
    _start(client, path, algorithm, {**config, "positions": {"F3": {"x": -.3, "y": .3}}})
    missing = _wait(client, path, algorithm)
    assert missing["state"] == "unavailable", missing
    assert missing["payload"]["field"]["grid"] is None
    assert missing["payload"]["field"]["included_channels"] == ["F3"]


def test_live_rbp_topomap_returns_relative_and_absolute_band_fields(live):
    client, path, _ = live
    values = _signal(10 * RATE)
    _append(client, path, 1000, values)
    field_ids = [
        f"rbp.relative.{band}" for band in ("delta", "theta", "alpha", "beta", "gamma")
    ] + [f"rbp.absolute.{band}" for band in ("delta", "theta", "alpha", "beta", "gamma")]
    config = {"spectrum": {"channels": NAMES}, "field_ids": field_ids, "positions": POSITIONS}
    _start(client, path, "rbp-topomap", config)
    result = _wait(client, path, "rbp-topomap")
    assert result["state"] == "ready", result
    assert [field["field_id"] for field in result["payload"]["fields"]] == field_ids
