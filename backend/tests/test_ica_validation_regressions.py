"""Regression evidence for reference preservation, exact stopping and runtime failures."""

import logging
import hashlib
import os
from pathlib import Path
from types import SimpleNamespace

import mne
import numpy as np
import pytest

from app.scientific import ica_classifier as classifier
from app.scientific.artifact_removal import fit_ica_model
from app.scientific.contracts.preprocessing import ArtifactRemovalSpec


@pytest.fixture
def car_input():
    names = ("Fp1-AVG", "Fp2-AVG", "C3-AVG", "O1-AVG")
    samples = np.random.default_rng(41).normal(0, 20e-6, (4, 2000))
    samples -= samples.mean(axis=0)
    raw = mne.io.RawArray(samples, mne.create_info(names, 250, "eeg"), verbose=False)
    labels = [name.removesuffix("-AVG") for name in names]
    provenance = {"formulas": [
        {"name": name, "positive": label, "negative_kind": "mean", "negative_labels": labels}
        for name, label in zip(names, labels, strict=True)
    ]}
    return raw, SimpleNamespace(n_components_=3), names, provenance


def _runtime(monkeypatch, inference):
    metadata = {"classifier_version": "test", "onnxruntime_version": "test",
                "iclabel_model_sha256": "test", "recommendation_rule_version": "test"}
    monkeypatch.setattr(classifier, "_load_iclabel_runtime", lambda: (inference, metadata))


def test_classifier_preserves_fit_samples_exactly(monkeypatch, car_input):
    raw, ica, names, provenance = car_input
    before = raw.get_data()

    def inference(classified_raw, *_args, **_kwargs):
        np.testing.assert_array_equal(classified_raw.get_data(), before)
        assert classified_raw.info["custom_ref_applied"] != 0
        return np.tile([1., 0., 0., 0., 0., 0., 0.], (3, 1))

    _runtime(monkeypatch, inference)
    result = classifier.classify_ica_components(raw, ica, names, 250, signal_provenance=provenance)
    assert result.classifier_status == "ready"
    np.testing.assert_array_equal(raw.get_data(), before)


@pytest.mark.parametrize("pool", [["Fp1", "Fp2"], [], ["Fp1", "Fp2", "C3", "C3"]])
def test_partial_or_invalid_average_pool_never_references_again(monkeypatch, car_input, pool):
    raw, ica, names, provenance = car_input
    for formula in provenance["formulas"]:
        formula["negative_labels"] = pool
    _runtime(monkeypatch, lambda *_a, **_k: pytest.fail("partial CAR must not reach inference"))
    before = raw.get_data()
    result = classifier.classify_ica_components(raw, ica, names, 250, signal_provenance=provenance)
    assert result.classifier_unavailable_reason == "non_average_reference_montage"
    np.testing.assert_array_equal(raw.get_data(), before)


def test_claimed_car_with_non_car_samples_is_rejected(monkeypatch, car_input):
    raw, ica, names, provenance = car_input
    raw.apply_function(lambda data: data + 30e-6, verbose=False)
    _runtime(monkeypatch, lambda *_a, **_k: pytest.fail("invalid CAR must not reach inference"))
    result = classifier.classify_ica_components(raw, ica, names, 250, signal_provenance=provenance)
    assert result.classifier_unavailable_reason == "fit_samples_not_common_average"


@pytest.mark.parametrize("error", [FileNotFoundError("weights missing"), OSError("runtime unavailable")])
def test_known_inference_failures_return_unavailable(monkeypatch, car_input, error):
    raw, ica, names, provenance = car_input

    def inference(*_args, **_kwargs):
        raise error

    _runtime(monkeypatch, inference)
    result = classifier.classify_ica_components(raw, ica, names, 250, signal_provenance=provenance)
    assert result.classifier_status == "unavailable"
    assert result.classifier_unavailable_reason == "icalabel_inference_unavailable"


def test_corrupt_onnx_is_unavailable_but_programming_errors_propagate(monkeypatch, car_input):
    from onnxruntime.capi.onnxruntime_pybind11_state import InvalidProtobuf

    raw, ica, names, provenance = car_input

    def inference(*_args, **_kwargs):
        raise InvalidProtobuf("invalid model")

    _runtime(monkeypatch, inference)
    result = classifier.classify_ica_components(raw, ica, names, 250, signal_provenance=provenance)
    assert result.classifier_status == "unavailable"

    def broken(*_args, **_kwargs):
        raise ValueError("invalid scientific feature")

    _runtime(monkeypatch, broken)
    with pytest.raises(ValueError, match="invalid scientific feature"):
        classifier.classify_ica_components(raw, ica, names, 250, signal_provenance=provenance)


def test_missing_model_is_not_reported_as_loaded(monkeypatch):
    monkeypatch.setattr(classifier, "_iclabel_onnx_sha256", lambda: None)
    inference, metadata = classifier._load_iclabel_runtime()
    assert inference is None
    assert metadata["unavailable_reason"] == "icalabel_model_unavailable"
    assert metadata["classifier_version"] is None


def test_model_hash_recovers_and_invalidates_file_changes_without_restart(tmp_path, monkeypatch):
    import mne_icalabel

    monkeypatch.setattr(mne_icalabel, "__file__", str(tmp_path / "__init__.py"))
    candidate = tmp_path / "iclabel/network/assets/ICLabelNet.onnx"
    candidate.parent.mkdir(parents=True)
    # Another ONNX file must not stand in for the asset the runtime actually loads.
    (candidate.parent / "backup-ICLabel.onnx").write_bytes(b"backup")
    read_bytes = Path.read_bytes
    reads = []

    def counted_read(path):
        reads.append(path)
        return read_bytes(path)

    monkeypatch.setattr(Path, "read_bytes", counted_read)
    classifier._hash_iclabel_model.cache_clear()
    try:
        assert classifier._load_iclabel_runtime()[1]["unavailable_reason"] == "icalabel_model_unavailable"
        candidate.write_bytes(b"model-one")
        available, first = classifier._load_iclabel_runtime()
        assert available is not None
        assert first["iclabel_model_sha256"] == hashlib.sha256(b"model-one").hexdigest()[:16]
        assert classifier._load_iclabel_runtime()[1] == first
        assert reads == [candidate.resolve()]

        stat = candidate.stat()
        candidate.write_bytes(b"model-two")  # Same size, changed modification time.
        os.utime(candidate, ns=(stat.st_atime_ns, stat.st_mtime_ns + 1_000_000_000))
        second = classifier._load_iclabel_runtime()[1]
        assert second["iclabel_model_sha256"] == hashlib.sha256(b"model-two").hexdigest()[:16]
        assert second["classifier_version"] != first["classifier_version"]
        assert len(reads) == 2

        candidate.unlink()
        assert classifier._load_iclabel_runtime()[0] is None
        candidate.write_bytes(b"restored-model")
        third = classifier._load_iclabel_runtime()[1]
        assert third["iclabel_model_sha256"] == hashlib.sha256(b"restored-model").hexdigest()[:16]
        assert len(reads) == 3
    finally:
        classifier._hash_iclabel_model.cache_clear()


@pytest.mark.parametrize("steps,expected", [
    ([(61, 3.30628793175245e-9, 90.)], False),
    ([(61, 9e-13, 90.)], True),
    ([(i, 1e-5, 30.) for i in range(1, 22)], True),
    ([(i, 1e-5, 30.) for i in range(1, 21)], False),
    ([(i, 1e-5, 30.) for i in range(1, 20)] + [(1, 1e-5, 30.)], False),
    ([], False),
])
def test_unrounded_stopping_rules_and_restart(monkeypatch, car_input, steps, expected, caplog):
    caplog.set_level(logging.INFO, logger="mne")
    class FakeIca:
        n_iter_ = 61

        def __init__(self, **kwargs):
            assert kwargs["fit_params"]["w_change"] == 1e-12

        def fit(self, *_args, **_kwargs):
            for step, change, angle in steps:
                logging.getLogger("mne").info(
                    "step %d - lrate %5f, wchange %8.8f, angledelta %4.1f deg", step, .01, change, angle)

    monkeypatch.setattr(mne.preprocessing, "ICA", FakeIca)
    _, converged, _ = classifier.fit_extended_infomax(car_input[0], n_components=3, random_state=42, max_iter=61)
    assert converged is expected


def test_real_infomax_at_iteration_cap_does_not_treat_rounded_zero_as_converged():
    rng = np.random.default_rng(123)
    sources = rng.laplace(size=(5, 5000))
    samples = (rng.normal(size=(5, 5)) @ sources) * 1e-6
    raw = mne.io.RawArray(samples, mne.create_info(["Fp1", "Fp2", "C3", "C4", "O1"], 250, "eeg"), verbose=False)
    _, converged, iterations = classifier.fit_extended_infomax(raw, n_components=5, random_state=42, max_iter=61)
    assert iterations == 61
    assert converged is False


def test_ica_without_frontal_or_auxiliary_references_can_fit(car_input):
    raw, _, _, _ = car_input
    names = ("F3", "C3", "C4", "O1")
    result = fit_ica_model(raw.get_data().T, 250, names, ArtifactRemovalSpec(method="ica_infomax"))
    assert result.eog_channels_used == result.ecg_channels_used == ()
    assert all(row["eog_score"] is None and row["ecg_score"] is None for row in result.component_scores)


@pytest.mark.parametrize("scenario", ["no_reference", "partial_car", "missing_weights", "inference_failure"])
def test_ica_review_api_scientific_and_runtime_regressions(tmp_path, monkeypatch, scenario):
    import json
    from fastapi.testclient import TestClient
    from test_ica_label_integration import _import_with_car_and_ref_montages, _synthesize_multiclass_artifact_eeg

    original_post = TestClient.post

    def import_with_partial_pool(client, url, **kwargs):
        if str(url).endswith("/recordings/import"):
            identity = json.loads(kwargs["data"]["export_channel_identity_json"])
            hardware = identity["hardware"]
            car = hardware["montage_configuration_snapshot_json"]
            car["AverageReferenceLabels"] = ["Fp1", "Fp2"]
            for channel in car["DerivedChannels"]:
                channel["NegativeLabels"] = ["Fp1", "Fp2"]
            hardware["montage_catalogue_snapshot_json"][0] = car
            kwargs["data"]["export_channel_identity_json"] = json.dumps(identity)
        return original_post(client, url, **kwargs)

    if scenario == "partial_car":
        monkeypatch.setattr(TestClient, "post", import_with_partial_pool)
    samples = _synthesize_multiclass_artifact_eeg(sfreq=250, duration_s=12)
    client, _, recording = _import_with_car_and_ref_montages(tmp_path, samples, 250)
    if scenario == "missing_weights":
        monkeypatch.setattr(classifier, "_iclabel_onnx_sha256", lambda: None)
    elif scenario == "inference_failure":
        def inference(*_args, **_kwargs):
            raise FileNotFoundError("ICLabelNet.onnx missing")
        _runtime(monkeypatch, inference)
    response = client.get(f"/api/recordings/{recording.id}/preprocessing/ica-components", params={
        "montage_id": "montage-car", "low_hz": 1, "high_hz": 50, "notch_hz": 50,
        "allow_frontal_eog_proxy": "false", "ica_n_components": 8,
    })
    assert response.status_code == 200, response.text
    report = response.json()
    assert report["eog_channels_used"] == report["ecg_channels_used"] == []
    assert all(row["eog_score"] is None and row["ecg_score"] is None for row in report["components"])
    if scenario == "no_reference":
        assert report["classifier_method"] == "mne_icalabel_onnx"
        assert all(row["iclabel_probabilities"] is not None for row in report["components"])
    else:
        assert report["classifier_unavailable_reason"] == {
            "partial_car": "non_average_reference_montage",
            "missing_weights": "icalabel_model_unavailable",
            "inference_failure": "icalabel_inference_unavailable",
        }[scenario]
        assert all(not row["recommended_exclude"] for row in report["components"])


def test_broadband_ica_fit_applies_synced_notch_for_strong_50hz_line_noise(tmp_path):
    from test_ica_label_integration import _import_with_car_and_ref_montages, _synthesize_multiclass_artifact_eeg

    sfreq = 250.0
    samples = _synthesize_multiclass_artifact_eeg(sfreq=sfreq, duration_s=24.0, seed=19)
    t = np.arange(len(samples), dtype=np.float64) / sfreq
    rng = np.random.default_rng(7)
    phases = rng.uniform(0.0, 2.0 * np.pi, size=samples.shape[1])
    amps = rng.uniform(140e-6, 240e-6, size=samples.shape[1])
    noisy = samples + amps[None, :] * np.sin(2.0 * np.pi * 50.0 * t[:, None] + phases[None, :])

    client, _, recording = _import_with_car_and_ref_montages(tmp_path, noisy, sfreq)
    notched_resp = client.get(f"/api/recordings/{recording.id}/preprocessing/ica-components", params={
        "montage_id": "montage-car", "low_hz": 1, "high_hz": 50, "notch_hz": 50,
        "allow_frontal_eog_proxy": "true", "ica_n_components": 12,
    })
    assert notched_resp.status_code == 200, notched_resp.text
    notched = notched_resp.json()
    assert notched["fit_notch_hz"] == 50.0
    assert notched["classifier_status"] == "ready"
    top = notched["components"][0]
    assert top["iclabel_label"] != "line noise"
    assert any(row["iclabel_label"] == "brain" and row["explained_variance_ratio"] >= 0.03 for row in notched["components"])

    unnotched_resp = client.get(f"/api/recordings/{recording.id}/preprocessing/ica-components", params={
        "montage_id": "montage-car", "low_hz": 1, "high_hz": 50, "notch_hz": "",
        "allow_frontal_eog_proxy": "true", "ica_n_components": 12,
    })
    # FastAPI treats empty notch_hz query or omitted? Verify explicit None via service path if needed.
    if unnotched_resp.status_code == 422:
        from app.scientific.contracts.preprocessing import ArtifactRemovalSpec
        from app.services.artifact_inspection import inspect_recording_ica_components
        # Obtain RecordingService via client app state
        svc = client.app.state.recording_service
        unnotched = inspect_recording_ica_components(
            svc, recording, montage_id="montage-car", low_hz=1.0, high_hz=50.0, notch_hz=None,
            removal_spec=ArtifactRemovalSpec(method="ica_infomax", allow_frontal_eog_proxy=True, ica_n_components=12),
        )
    else:
        assert unnotched_resp.status_code == 200, unnotched_resp.text
        unnotched = unnotched_resp.json()
    assert unnotched["fit_notch_hz"] is None
    assert unnotched["components"][0]["iclabel_label"] == "line noise"
    assert unnotched["components"][0]["explained_variance_ratio"] > 0.40
