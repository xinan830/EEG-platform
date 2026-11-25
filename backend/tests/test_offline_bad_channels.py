"""Tests for Step 1 offline bad-channel detection, MNE spherical interpolation, CAR safety, and caching."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import mne
import numpy as np
import pyedflib
import pytest

from app.scientific.bad_channels import (
    apply_spherical_spline_interpolation,
    build_spherical_spline_interpolator,
    detect_bad_channels,
)
from app.scientific.contracts.preprocessing import (
    BadChannelSpec,
    OfflinePreprocessingSpec,
    ScientificFilterSpec,
)


def _write_multi_channel_edf(path: Path, labels: tuple[str, ...], samples_uv: np.ndarray, sfreq: int = 200) -> bytes:
    """Write sample-major uV matrix (n_samples, n_channels) into an EDF+ file."""
    n_channels = len(labels)
    writer = pyedflib.EdfWriter(str(path), n_channels, pyedflib.FILETYPE_EDFPLUS)
    headers = []
    for idx, label in enumerate(labels):
        col = samples_uv[:, idx]
        lo = min(-200.0, float(np.floor(np.min(col)) - 10.0))
        hi = max(200.0, float(np.ceil(np.max(col)) + 10.0))
        headers.append({
            "label": label,
            "dimension": "uV",
            "sample_frequency": sfreq,
            "physical_min": lo,
            "physical_max": hi,
            "digital_min": -32768,
            "digital_max": 32767,
            "transducer": "",
            "prefilter": "",
        })
    writer.setSignalHeaders(headers)
    writer.writeSamples([np.ascontiguousarray(samples_uv[:, idx], dtype=np.float64) for idx in range(n_channels)])
    writer.close()
    return path.read_bytes()


def test_spherical_spline_interpolation_matches_mne_raw_interpolate_bads_to_machine_precision():
    channels = ("Fp1", "Fp2", "F3", "F4", "C3", "C4", "P3", "P4", "O1", "O2")
    rng = np.random.default_rng(42)
    values_v = rng.normal(0.0, 20e-6, size=(400, len(channels)))
    values_v[:, channels.index("C4")] = 5000e-6  # corrupted channel

    montage = mne.channels.make_standard_montage("colin27_1020")
    info = mne.create_info(list(channels), sfreq=200.0, ch_types="eeg")
    info.set_montage(montage)
    raw = mne.io.RawArray(values_v.T.copy(), info, verbose=False)
    raw.info["bads"] = ["C4"]
    raw.interpolate_bads(reset_bads=True, mode="accurate", verbose=False)
    mne_expected = raw.get_data().T

    plan = build_spherical_spline_interpolator(channels, ("C4",))
    actual = apply_spherical_spline_interpolation(values_v, plan)

    np.testing.assert_allclose(actual[:, channels.index("C4")], mne_expected[:, channels.index("C4")], atol=1e-12, rtol=1e-12)
    # Good channels remain bit-for-bit untouched.
    for idx, ch in enumerate(channels):
        if ch != "C4":
            np.testing.assert_array_equal(actual[:, idx], values_v[:, idx])


def test_spherical_spline_interpolation_preserves_nan_gaps_and_rejects_invalid_targets():
    channels = ("Fp1", "Fp2", "F3", "F4", "C3", "C4")
    rng = np.random.default_rng(7)
    values_v = rng.normal(0.0, 15e-6, size=(200, len(channels)))
    values_v[50:60, channels.index("F4")] = np.nan

    plan = build_spherical_spline_interpolator(channels, ("C4",))
    interpolated = apply_spherical_spline_interpolation(values_v, plan)

    assert np.all(np.isnan(interpolated[50:60, channels.index("C4")]))
    assert np.all(np.isfinite(interpolated[:50, channels.index("C4")]))
    assert np.all(np.isfinite(interpolated[60:, channels.index("C4")]))

    with pytest.raises(ValueError, match="BAD_CHANNEL_INTERPOLATION_UNSUPPORTED"):
        build_spherical_spline_interpolator(channels, ("C4",), channel_kinds=("reference",) * 5 + ("bipolar",))
    with pytest.raises(ValueError, match="BAD_CHANNEL_POSITION_UNAVAILABLE"):
        build_spherical_spline_interpolator((*channels, "AUX1"), ("AUX1",))
    with pytest.raises(ValueError, match="BAD_CHANNEL_INSUFFICIENT_GOOD_DONORS"):
        build_spherical_spline_interpolator(("Fp1", "Fp2", "C4"), ("C4",))
    with pytest.raises(ValueError, match="exclude 或 interpolate"):
        BadChannelSpec(policy="none", channels=("C4",)).normalized()


def test_detect_bad_channels_flags_flatline_saturation_hf_noise_decorrelation_and_nan():
    sfreq = 200.0
    t = np.arange(0, 10.0, 1.0 / sfreq)
    rng = np.random.default_rng(123)
    shared_alpha = 20e-6 * np.sin(2 * np.pi * 10.0 * t) + 10e-6 * np.sin(2 * np.pi * 6.0 * t)

    channels = ("Fp1", "Fp2", "F4", "F3", "C4", "P4", "O2", "Cz")
    data = np.column_stack([
        shared_alpha + rng.normal(0, 2e-6, t.size),              # Fp1: healthy
        0.95 * shared_alpha + rng.normal(0, 2e-6, t.size),       # Fp2: healthy
        0.90 * shared_alpha + rng.normal(0, 2e-6, t.size),       # F4: healthy
        np.full_like(t, 1.0e-6),                                  # F3: flatline
        shared_alpha + 900e-6 * np.sin(2 * np.pi * 2.0 * t),     # C4: extreme amplitude
        3e-6 * np.sin(2 * np.pi * 10.0 * t) + 35e-6 * np.sin(2 * np.pi * 65.0 * t),  # P4: HF noise
        rng.normal(0, 25e-6, t.size),                             # O2: uncorrelated random noise
        shared_alpha.copy(),                                      # Cz: excessive NaN
    ])
    data[: int(0.35 * t.size), channels.index("Cz")] = np.nan

    report = detect_bad_channels(data, sfreq, channels)
    by_name = {item.channel: item for item in report.channels}

    for healthy in ("Fp1", "Fp2", "F4"):
        assert by_name[healthy].is_bad is False, by_name[healthy]
        # Only 3 healthy channels exist in total; marking another healthy channel bad leaves only 2 donors.
        assert by_name[healthy].can_interpolate is False
        assert by_name[healthy].interpolation_unavailable_reason == "insufficient_good_3d_donors"
    assert "flatline" in by_name["F3"].reasons
    assert by_name["F3"].can_interpolate is True  # 3 healthy 3D donors (Fp1, Fp2, F4) remain
    assert "extreme_amplitude" in by_name["C4"].reasons
    assert "high_frequency_noise" in by_name["P4"].reasons
    assert "low_neighbor_correlation" in by_name["O2"].reasons
    assert "excessive_nan_ratio" in by_name["Cz"].reasons
    assert set(report.recommended_bad_channels) == {"F3", "C4", "P4", "O2", "Cz"}

    # Exact 3-channel case (Fp1, Fp2, C4 with C4 bad -> only 2 donors): must report can_interpolate=False.
    small_data = np.column_stack([data[:, 0], data[:, 1], data[:, channels.index("C4")]])
    small_report = detect_bad_channels(small_data, sfreq, ("Fp1", "Fp2", "C4"))
    small_c4 = next(item for item in small_report.channels if item.channel == "C4")
    assert small_c4.is_bad is True
    assert small_c4.can_interpolate is False
    assert small_c4.interpolation_unavailable_reason == "insufficient_good_3d_donors"


def test_pre_rereference_bad_channel_exclude_and_interpolate_prevent_car_contamination(tmp_path: Path):
    from test_recording_import import _client

    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/bad-car-study", json={"name": "Study"}).json()["project_id"]

    labels = ("Fp1", "Fp2", "F3", "F4", "C3", "C4")
    sfreq = 200
    t = np.arange(0, 12.0, 1.0 / sfreq)
    clean_signal = np.column_stack([
        20.0 * np.sin(2 * np.pi * 10.0 * t + 0.1 * idx)
        for idx in range(len(labels))
    ])
    corrupted = clean_signal.copy()
    corrupted[:, labels.index("C4")] = 4000.0 * np.sin(2 * np.pi * 7.0 * t)  # huge artifact on C4

    source = _write_multi_channel_edf(tmp_path / "car_bad.edf", labels, corrupted, sfreq=sfreq)
    channel_snapshot = {
        "Id": "ch-cfg",
        "DeviceSignature": "ant-layout",
        "HardwareReferenceElectrodeLocation": "REF",
        "HardwareGroundElectrodeLocation": "GND",
        "Channels": [
            {"NativeChannelIndex": idx, "ExpectedKind": 1, "ElectrodeLabel": label,
             "IsSelectedForDisplay": True, "DisplayOrder": idx}
            for idx, label in enumerate(labels)
        ],
    }
    car_montage = {
        "Id": "car-montage",
        "DeviceSignature": "ant-layout",
        "ChannelConfigurationSnapshot": channel_snapshot,
        "AverageReferenceLabels": list(labels),
        "DerivedChannels": [
            {"Name": f"{label}-AVG", "PositiveLabel": label, "NegativeKind": 2,
             "NegativeLabels": list(labels), "DisplayOrder": idx}
            for idx, label in enumerate(labels)
        ],
    }
    identity = {
        "exported_signal_sha256": hashlib.sha256(source).hexdigest(),
        "channel_configuration_snapshot_json": channel_snapshot,
        "channels": [
            {"export_label": label, "source_label": label, "native_channel_index": idx, "kind": "1"}
            for idx, label in enumerate(labels)
        ],
        "hardware": {
            "acquisition_montage_snapshot_json": {"Id": "car-montage"},
            "montage_configuration_snapshot_json": car_montage,
            "montage_catalogue_snapshot_json": [car_montage],
        },
    }
    imported = client.post(
        f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01", "export_channel_identity_json": json.dumps(identity)},
        files={"file": ("car_bad.edf", source)},
    )
    assert imported.status_code == 201, imported.text
    recording_id = imported.json()["recording_id"]

    # 1. Inspect endpoint catches C4 automatically on raw unprojected channels.
    inspected = client.get(f"/api/recordings/{recording_id}/preprocessing/bad-channels")
    assert inspected.status_code == 200, inspected.text
    assert "C4" in inspected.json()["recommended_bad_channels"]

    recording = recordings.require_recording(recording_id)
    selected = recordings.select_analysis_montage(recording, "car-montage")

    # 2. Baseline (no bad-channel handling): C4's 4000 uV leaks ~667 uV into F3-AVG.
    with recordings.open_access_session(selected) as baseline_session:
        baseline_f3 = baseline_session.read_samples(0, 600, ("F3-AVG",)).values_v[:, 0]
    assert np.max(np.abs(baseline_f3)) > 500e-6

    # 3. Exclude policy: C4 removed from CAR pool before subtraction; C4-AVG dropped; F3-AVG stays clean.
    exclude_spec = BadChannelSpec(policy="exclude", channels=("C4",))
    with recordings.open_access_session(selected, bad_channel_spec=exclude_spec) as excl_session:
        assert "C4-AVG" not in excl_session.channel_names
        excl_f3 = excl_session.read_samples(0, 600, ("F3-AVG",)).values_v[:, 0]
        assert excl_session.identity.signal_view_sha256 != baseline_session.identity.signal_view_sha256
    assert np.max(np.abs(excl_f3)) < 40e-6

    # 4. Interpolate policy: C4 spherically interpolated BEFORE CAR; both F3-AVG and C4-AVG stay clean.
    interp_spec = BadChannelSpec(policy="interpolate", channels=("C4",))
    with recordings.open_access_session(selected, bad_channel_spec=interp_spec) as interp_session:
        assert "C4-AVG" in interp_session.channel_names
        interp_block = interp_session.read_samples(0, 600, ("F3-AVG", "C4-AVG")).values_v
        assert interp_session.identity.signal_view_sha256 != baseline_session.identity.signal_view_sha256
        assert interp_session.identity.signal_view_sha256 != excl_session.identity.signal_view_sha256
    assert np.max(np.abs(interp_block[:, 0])) < 40e-6
    assert np.max(np.abs(interp_block[:, 1])) < 40e-6

    # 5. Continuous + bounded offline preprocessing caches consume OfflinePreprocessingSpec directly.
    preproc = recordings.offline_scientific_preprocessing
    f_spec_cont = ScientificFilterSpec(
        low_hz=1.0, high_hz=45.0, notch_hz=50.0, scope="whole_recording_before_window_slice",
    )
    spec_none = OfflinePreprocessingSpec(f_spec_cont).validate(200.0)
    spec_interp = OfflinePreprocessingSpec(f_spec_cont, interp_spec).validate(200.0)
    spec_excl = OfflinePreprocessingSpec(f_spec_cont, exclude_spec).validate(200.0)
    assert spec_none.fingerprint() != spec_interp.fingerprint()

    cont_none = preproc.prepare_continuous(
        selected, low_hz=1.0, high_hz=45.0, notch_hz=50.0, preprocessing_spec=spec_none,
    )
    cont_interp = preproc.prepare_continuous(
        selected, low_hz=1.0, high_hz=45.0, notch_hz=50.0, preprocessing_spec=spec_interp,
    )
    cont_excl = preproc.prepare_continuous(
        selected, low_hz=1.0, high_hz=45.0, notch_hz=50.0, preprocessing_spec=spec_excl,
    )
    assert "C4-AVG" not in cont_excl.channel_names
    assert cont_none.data is not cont_interp.data
    assert np.max(np.abs(cont_none.data[:, cont_none.channel_names.index("F3-AVG")])) > 400e-6
    assert np.max(np.abs(cont_interp.data[:, cont_interp.channel_names.index("F3-AVG")])) < 40e-6

    f_spec_win = ScientificFilterSpec(
        low_hz=1.0, high_hz=45.0, notch_hz=50.0, scope="bounded_window_with_finite_context",
    )
    win_spec_interp = OfflinePreprocessingSpec(f_spec_win, interp_spec).validate(200.0)
    with recordings.open_access_session(selected) as s_none, \
         recordings.open_access_session(selected, bad_channel_spec=interp_spec) as s_interp:
        w_none = preproc.prepare_bounded_window(
            s_none, start_sample=400, end_sample=1200, channels=["F3-AVG"],
            low_hz=1.0, high_hz=45.0, notch_hz=50.0, context_s=1.0, algorithm_version="test-v1",
        )
        w_interp = preproc.prepare_bounded_window(
            s_interp, start_sample=400, end_sample=1200, channels=["F3-AVG"],
            low_hz=1.0, high_hz=45.0, notch_hz=50.0, context_s=1.0, algorithm_version="test-v1",
            preprocessing_spec=win_spec_interp,
        )
        with pytest.raises(ValueError, match="BAD_CHANNEL_SPEC_SESSION_MISMATCH"):
            preproc.prepare_bounded_window(
                s_none, start_sample=400, end_sample=1200, channels=["F3-AVG"],
                low_hz=1.0, high_hz=45.0, notch_hz=50.0, context_s=1.0, algorithm_version="test-v1",
                preprocessing_spec=win_spec_interp,
            )
        w_interp_inherited = preproc.prepare_bounded_window(
            s_interp, start_sample=400, end_sample=1200, channels=["F3-AVG"],
            low_hz=1.0, high_hz=45.0, notch_hz=50.0, context_s=1.0, algorithm_version="test-v1",
        )
    assert np.max(np.abs(w_none.data[:, 0])) > 400e-6
    assert np.max(np.abs(w_interp.data[:, 0])) < 40e-6
    assert w_interp.evidence["offline_preprocessing_spec"]["bad_channels"]["policy"] == "interpolate"
    assert w_interp_inherited.evidence["offline_preprocessing_spec"]["bad_channels"]["policy"] == "interpolate"

    # 6. Inspecting bad channels on an ALREADY montage-selected summary still inspects raw unprojected channels.
    from app.services.bad_channel_reader import inspect_recording_bad_channels

    selected_report = inspect_recording_bad_channels(recordings, selected)
    assert [item["channel"] for item in selected_report["channels"]] == list(labels)
    assert "C4" in selected_report["recommended_bad_channels"]
    c4_entry = next(item for item in selected_report["channels"] if item["channel"] == "C4")
    assert c4_entry["can_interpolate"] is True


def test_inspect_bad_channels_reads_raw_unprojected_channels_for_wpf_montage_recording(tmp_path: Path):
    from test_acquisition_analysis_signals import fixture_recording
    from test_recording_import import _client

    client, recordings, _ = _client(tmp_path)
    root = tmp_path / "wpf_montage_rec"
    fixture_recording(root)
    registered = recordings.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    assert registered.stored_name.startswith("wpf-montage:")
    # Montage-projected channels carry suffixes like F3-F7 / Cz-REF; inspection MUST read raw F3/F7/Cz.
    assert any("-" in ch for ch in registered.channels)

    response = client.get(f"/api/recordings/{registered.id}/preprocessing/bad-channels")
    assert response.status_code == 200, response.text
    inspected_channels = [item["channel"] for item in response.json()["channels"]]
    assert inspected_channels == ["F3", "F7", "Cz"]
    # Only 3 channels exist in fixture_recording -> marking any one bad leaves only 2 donors (< 3).
    assert all(
        item["can_interpolate"] is False
        and item["interpolation_unavailable_reason"] == "insufficient_good_3d_donors"
        for item in response.json()["channels"]
    )


def test_detect_bad_channels_ignores_dc_coupled_drift_and_50hz_mains_on_healthy_eeg():
    sfreq = 1000.0
    t = np.arange(0, 12.0, 1.0 / sfreq)
    rng = np.random.default_rng(909)
    channels = ("Fp1", "Fp2", "F3", "F4", "C3", "C4", "O1", "O2")
    brain = (15e-6 * np.sin(2 * np.pi * 10.0 * t))[:, None] + rng.normal(0.0, 5e-6, size=(t.size, len(channels)))
    # Simulate DC-coupled amplifier: 80 mV DC offset + 1.5 mV slow polarization drift + strong 50 Hz mains hum.
    dc_drift = 80e-3 + 1.5e-3 * np.sin(2 * np.pi * 0.04 * t)[:, None]
    mains_50hz = (120e-6 * np.sin(2 * np.pi * 50.0 * t))[:, None]
    data = brain + dc_drift + mains_50hz
    # Inject real broadband 70-90 Hz contact noise on C4 only.
    data[:, 5] += 40e-6 * np.sin(2 * np.pi * 78.0 * t) + 35e-6 * np.sin(2 * np.pi * 84.0 * t)

    report = detect_bad_channels(data, sfreq, channels)
    assert report.recommended_bad_channels == ("C4",)
    by_name = {item.channel: item for item in report.channels}
    assert "high_frequency_noise" in by_name["C4"].reasons
    assert by_name["Fp1"].is_bad is False
    assert by_name["O1"].is_bad is False
