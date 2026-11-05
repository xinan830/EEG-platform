from pathlib import Path

import numpy as np
import pytest

from app.models.analysis_config import AnalysisConfigRequest
from app.scientific.primitives.spectral import estimate_spectrogram, estimate_spectrogram_with_quality
from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from app.services.recordings import RecordingService
from app.services.recording_access import RecordingAccessSession, RecordingSourceIdentity
from app.services.recording_samples import ArraySampleReader


def _service(tmp_path: Path) -> tuple[RecordingService, object]:
    service = RecordingService(tmp_path / "recordings", tmp_path / "catalog.sqlite3")
    recording = service.create_recording("sample.bdf", ".bdf", b"raw")
    return service, recording


def _provide_data(monkeypatch, service, data, sfreq, names):
    def open_session(_recording, *, verify_content_hash=True):
        reader = ArraySampleReader(data, sfreq, names)
        return RecordingAccessSession(
            RecordingSourceIdentity("test", "test", "test"), reader, sfreq,
            len(data), names, (), lambda: None, serialize_reads=False,
        )
    monkeypatch.setattr(service, "open_access_session", open_session)


def test_spectrum_preserves_requested_channel_order_and_units(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = np.column_stack([
        10e-6 * np.sin(2 * np.pi * 10 * times),
        20e-6 * np.sin(2 * np.pi * 10 * times),
        30e-6 * np.sin(2 * np.pi * 10 * times),
    ])
    _provide_data(monkeypatch, service, data, sfreq, ["Fz", "Pz", "Oz"])

    payload = service.load_spectrum(recording, channels=["Oz", "Fz"])

    assert payload["channels"] == ["Oz", "Fz"]
    assert payload["units"] == {"psd": "uV^2/Hz", "absolute_power": "uV^2", "relative_power": "ratio"}
    assert payload["algorithm_version"] == "offline-spectral-v4"
    np.testing.assert_allclose(
        sum(payload["relative_band_power"]["Oz"].values()), 1.0, rtol=1e-5
    )


def test_spectrum_exposes_backend_declared_welch_step(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(10 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, data, sfreq, ["F3"])

    payload = service.load_spectrum(recording, 0.0, 10.0, ["F3"])

    assert payload["welch_contract"]["welch_segment_s"] == 4.0
    assert payload["welch_contract"]["welch_segment_overlap"] == 0.5
    assert payload["welch_contract"]["welch_step_s"] == 2.0


def test_welch_segment_psds_are_opt_in_and_keep_output_units(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(20 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, data, sfreq, ["O1"])

    ordinary = service.load_spectrum(recording, 0.0, 20.0, ["O1"])
    diagnostic = service.load_spectrum(
        recording, 0.0, 20.0, ["O1"], include_welch_segments=True,
    )
    estimate = _RecordingAlgorithmContext._spectral_estimate(diagnostic)

    assert "welch_segments" not in ordinary
    assert diagnostic["welch_segments"]["offsets_s"] == list(range(0, 18, 2))
    assert len(diagnostic["welch_segments"]["psd_uV2_per_hz"]) == 9
    assert len(diagnostic["welch_segments"]["psd_uV2_per_hz"][0][0]) == len(diagnostic["frequencies_hz"])
    assert estimate.welch_segment_offsets_s == tuple(float(value) for value in range(0, 18, 2))
    np.testing.assert_allclose(
        estimate.welch_segments_psd * 1e12,
        diagnostic["welch_segments"]["psd_uV2_per_hz"],
    )


def test_spectrum_accepts_a_long_recording_range_without_a_fixed_cap(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(150 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, data, sfreq, ["F3"])

    payload = service.load_spectrum(recording, 0.0, 149.982, ["F3"])

    assert payload["window_start_s"] == 0.0
    assert payload["window_duration_s"] == pytest.approx(149.98)


def test_spectrum_rejects_a_range_past_the_recording_end_instead_of_truncating(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(150 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, data, sfreq, ["F3"])

    with pytest.raises(ValueError, match="超出文件范围"):
        service.load_spectrum(recording, 1.0, 149.982, ["F3"])


def test_spectrum_uses_the_requested_frequency_range_for_filter_cache_and_output(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(20 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 40 * times) + 6e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, data, sfreq, ["Fz"])

    wide = service.load_spectrum(recording, 0.0, 20.0, ["Fz"], low_hz=0.5, high_hz=50.0)
    default = service.load_spectrum(recording, 0.0, 20.0, ["Fz"], low_hz=1.0, high_hz=30.0, notch_hz=None)

    assert wide["frequencies_hz"][0] == 0.5
    assert wide["frequencies_hz"][-1] == 50.0
    assert wide["filter_contract"]["bandpass_hz"] == [0.5, 50.0]
    assert wide["filter_contract"]["notch_hz"] == 50.0
    assert wide["filter_contract"]["notch_quality_factor"] == 30.0
    assert default["frequencies_hz"][0] == 1.0
    assert default["frequencies_hz"][-1] == 30.0
    assert default["filter_contract"]["notch_hz"] is None


def test_spectrum_separates_filter_range_from_output_axis_and_reuses_filter_cache(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(20 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, data, sfreq, ["Fz"])

    first = service.load_spectrum(
        recording, 0.0, 20.0, ["Fz"], notch_hz=60.0,
        filter_low_hz=0.5, filter_high_hz=40.0,
        output_low_hz=1.0, output_high_hz=50.0,
    )
    second = service.load_spectrum(
        recording, 0.0, 20.0, ["Fz"], notch_hz=60.0,
        filter_low_hz=0.5, filter_high_hz=40.0,
        output_low_hz=5.0, output_high_hz=10.0,
    )

    assert first["filter_contract"]["bandpass_hz"] == [0.5, 40.0]
    assert first["filter_contract"]["notch_hz"] == 60.0
    assert first["filter_frequency_range_hz"] == {"low_hz": 0.5, "high_hz": 40.0}
    assert first["frequency_range_hz"] == {"low_hz": 1.0, "high_hz": 50.0}
    assert first["frequencies_hz"][0] == 1.0
    assert first["frequencies_hz"][-1] == 50.0
    assert second["frequencies_hz"][0] == 5.0
    assert second["frequencies_hz"][-1] == 10.0
    assert first["filter_contract"]["bandpass_hz"] == second["filter_contract"]["bandpass_hz"]


def test_spectrum_notch_changes_signal_and_preprocessing_identity(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 500.0
    times = np.arange(20 * int(sfreq)) / sfreq
    data = (20e-6 * np.sin(2 * np.pi * 50 * times) + 5e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, data, sfreq, ["Fz"])

    common = dict(filter_low_hz=1.0, filter_high_hz=80.0, output_low_hz=45.0, output_high_hz=55.0)
    notched = service.load_spectrum(recording, 0.0, 20.0, ["Fz"], notch_hz=50.0, **common)
    unnotched = service.load_spectrum(recording, 0.0, 20.0, ["Fz"], notch_hz=None, **common)

    notch_bin = int(np.argmin(np.abs(np.asarray(notched["frequencies_hz"]) - 50.0)))
    assert notched["psd"]["Fz"][notch_bin] < unnotched["psd"]["Fz"][notch_bin] * 0.1
    assert notched["filter_contract"]["notch_hz"] == 50.0
    assert unnotched["filter_contract"]["notch_hz"] is None
    assert len(service._analysis_preprocess_cache._entries) == 2


def test_spectrogram_uses_explicit_notch_without_whole_record_cache(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(12 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, data, sfreq, ["Fz"])

    payload = service.load_spectrogram(recording, 0.0, 12.0, ["Fz"], low_hz=1.0, high_hz=50.0, notch_hz=50.0)

    assert payload["notch_hz"] == 50.0
    assert payload["frequency_range_hz"] == {"low_hz": 1.0, "high_hz": 50.0}
    assert payload["frequencies_hz"][-1] == 50.0
    assert not service._analysis_preprocess_cache._entries


def test_spectrum_reuses_continuous_preprocessed_recording(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = np.column_stack([
        10e-6 * np.sin(2 * np.pi * 10 * times),
        8e-6 * np.sin(2 * np.pi * 6 * times),
    ])
    _provide_data(monkeypatch, service, data, sfreq, ["Fz", "Pz"])
    calls = {"count": 0}

    import app.services.mapped_preprocessing as mapped_preprocessing
    original = mapped_preprocessing.preprocess_blocks

    def counted(*args, **kwargs):
        calls["count"] += 1
        return original(*args, **kwargs)

    monkeypatch.setattr(mapped_preprocessing, "preprocess_blocks", counted)
    service.load_spectrum(recording, channels=["Fz"], window_s=10)
    service.load_spectrum(recording, channels=["Pz"], window_s=10)

    assert calls["count"] == 1


def test_on_demand_spectrum_reuses_canonical_preprocessed_source(tmp_path, monkeypatch):
    from dataclasses import replace

    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = np.column_stack([
        10e-6 * np.sin(2 * np.pi * 10 * times),
        8e-6 * np.sin(2 * np.pi * 6 * times),
    ])
    recording = replace(recording, sfreq=sfreq, duration_s=30.0, channels=("Fz", "Pz"))
    _provide_data(monkeypatch, service, data, sfreq, ["Fz", "Pz"])

    static = service.load_spectrum(recording, 0.0, 10.0, ["Fz", "Pz"])
    class OnDemandControl:
        is_on_demand = True

        def check_cancelled(self):
            pass

    on_demand = service.load_spectrum(
        recording, 0.0, 10.0, ["Pz"], execution_context=OnDemandControl(),
    )

    np.testing.assert_array_equal(on_demand["psd"]["Pz"], static["psd"]["Pz"])
    assert on_demand["band_power"]["Pz"] == static["band_power"]["Pz"]
    prepared = [
        entry for (recording_id, _identity), entry
        in service._analysis_preprocess_cache._entries.items()
        if recording_id == recording.id
    ]
    assert len(prepared) == 1
    assert prepared[0].channel_names == ("Fz", "Pz")
    assert prepared[0].data.shape == data.shape


def test_spectrum_and_on_demand_spectrum_share_preprocessing(tmp_path, monkeypatch):
    from dataclasses import replace

    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = np.column_stack([
        10e-6 * np.sin(2 * np.pi * 10 * times),
        8e-6 * np.sin(2 * np.pi * 6 * times),
    ])
    recording = replace(recording, sfreq=sfreq, duration_s=30.0, channels=("Fz", "Pz"))
    _provide_data(monkeypatch, service, data, sfreq, ["Fz", "Pz"])
    calls = {"count": 0}

    import app.services.mapped_preprocessing as mapped_preprocessing
    original = mapped_preprocessing.preprocess_blocks

    def counted(*args, **kwargs):
        calls["count"] += 1
        return original(*args, **kwargs)

    monkeypatch.setattr(mapped_preprocessing, "preprocess_blocks", counted)
    service.load_spectrum(recording, 0.0, 10.0, ["Fz"], low_hz=1.0, high_hz=30.0)

    class OnDemandControl:
        is_on_demand = True

        def check_cancelled(self):
            pass

    service.load_spectrum(
        recording, 0.0, 10.0, ["Pz"], notch_hz=50.0,
        filter_low_hz=1.0, filter_high_hz=30.0,
        output_low_hz=1.0, output_high_hz=30.0,
        execution_context=OnDemandControl(),
    )

    assert calls["count"] == 1
    assert len(service._analysis_preprocess_cache._entries) == 1


def test_on_demand_spectrogram_matches_static_without_whole_preprocessed_source(tmp_path, monkeypatch):
    from dataclasses import replace

    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(12 * int(sfreq)) / sfreq
    data = np.column_stack([
        10e-6 * np.sin(2 * np.pi * 10 * times),
        8e-6 * np.sin(2 * np.pi * 6 * times),
    ])
    recording = replace(recording, sfreq=sfreq, duration_s=12.0, channels=("Fz", "Pz"))
    _provide_data(monkeypatch, service, data, sfreq, ["Fz", "Pz"])

    class OnDemandControl:
        is_on_demand = True

        def check_cancelled(self):
            pass

    static = service.load_spectrogram(recording, 0.0, 12.0, ["Pz"], notch_hz=None)
    service._analysis_preprocess_cache.clear_recording(recording.id)
    on_demand = service.load_spectrogram(
        recording, 0.0, 12.0, ["Pz"], notch_hz=None,
        execution_context=OnDemandControl(),
    )

    np.testing.assert_array_equal(on_demand["power_linear"]["Pz"], static["power_linear"]["Pz"])
    np.testing.assert_array_equal(on_demand["power_db"]["Pz"], static["power_db"]["Pz"])
    assert not service._analysis_preprocess_cache._entries
    assert on_demand["preprocessing"]["channel_order"] == ["Pz"]


def test_configured_v4_defaults_record_current_baseline(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(40 * int(sfreq)) / sfreq
    data = np.column_stack([10e-6 * np.sin(2 * np.pi * 10 * times), 8e-6 * np.sin(2 * np.pi * 6 * times)])
    _provide_data(monkeypatch, service, data, sfreq, ["F3", "Fz"])
    frozen = service.load_spectrum(recording, 0, 30, ["F3", "Fz"])
    configured = service.load_configured_spectrum(recording, AnalysisConfigRequest(
        mode="static", channels=["F3", "Fz"], time={"start_s": 0, "end_s": 30}
    ))
    assert configured["algorithm_version"] == "offline-spectral-v4-configurable"
    assert configured["baseline_algorithm_version"] == "offline-spectral-v4"
    assert len(configured["analysis_config_hash"]) == 12
    np.testing.assert_allclose(configured["psd"]["F3"], frozen["psd"]["F3"])
    assert configured["band_power"] == frozen["band_power"]
    assert configured["relative_band_power"] == frozen["relative_band_power"]
    provenance = configured["analysis_provenance"]
    assert provenance["contract_version"] == "analysis-provenance-v1"
    assert provenance["welch"]["step_s"] == 2.0
    assert provenance["actual_range"] == {"start_s": 0.0, "end_s": 30.0}
    # Golden values for the deterministic 10 Hz / 6 Hz synthetic fixture.
    assert configured["band_power"]["F3"]["alpha"] == pytest.approx(49.9927616751, rel=1e-7)
    assert configured["band_power"]["Fz"]["theta"] == pytest.approx(31.9984029486, rel=1e-7)
    assert configured["relative_band_power"]["F3"]["alpha"] == pytest.approx(
        configured["band_power"]["F3"]["alpha"] /
        sum(value for value in configured["band_power"]["F3"].values() if value is not None),
        rel=1e-12,
    )


def test_configured_static_range_rejects_file_overflow(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    times = np.arange(1000) / 100.0
    values = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, values, 200.0, ["F3"])
    config = AnalysisConfigRequest(mode="static", channels=["F3"], time={"start_s": 5, "end_s": 15})
    import pytest
    with pytest.raises(ValueError, match="超出文件范围"):
        service.load_configured_spectrum(recording, config)


def test_configured_static_range_accepts_one_sample_rounding_error(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    times = np.arange(7634) / 1000.0
    values = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, values, 1000.0, ["F3"])
    config = AnalysisConfigRequest(mode="static", channels=["F3"], time={"start_s": 1.022, "end_s": 7.635})
    payload = service.load_configured_spectrum(recording, config)
    assert payload["actual_end_s"] == 7.634


def test_configured_spectrogram_accepts_one_sample_rounding_error(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    config = AnalysisConfigRequest(mode="spectrogram", channels=["F3"], time={"start_s": 1.022, "end_s": 7.635})
    monkeypatch.setattr(service, "load_spectrogram", lambda *_args, **_kwargs: {
        "recording_id": recording.id, "window_start_s": 1.022, "window_duration_s": 6.612,
        "sfreq_hz": 500.0, "channels": ["F3"], "times_s": [1.022], "frequencies_hz": [1.0],
        "power": {"F3": [[1.0]]}, "units": "uV^2/Hz", "algorithm_version": "spectrogram-v4-bounded-notch",
        "frequency_range_hz": {"low_hz": 1.0, "high_hz": 50.0},
        "filter_frequency_range_hz": {"low_hz": 1.0, "high_hz": 50.0},
        "notch_hz": None, "preprocessing": {"scope": "bounded_window_with_finite_context"},
        "band_power_timeseries": {"F3": {"delta": [1.0], "theta": [1.0], "alpha": [1.0], "beta": [1.0]}},
        "segment_s": 4.0, "step_s": 1.0,
    })
    payload = service.load_configured_spectrogram(recording, config)
    assert payload["algorithm_version"] == "spectrogram-v4-bounded-notch-configurable"
    assert payload["actual_end_s"] == 7.634
    assert payload["matrix_shape"] == [1, 1]
    assert payload["first_center_s"] == 1.022
    assert "band_power_timeseries" in payload
    assert payload["analysis_provenance"]["contract_version"] == "analysis-provenance-v1"
    assert payload["analysis_provenance"]["welch"]["step_s"] == 1.0


def test_configured_spectrogram_returns_backend_custom_band_trend(tmp_path, monkeypatch):
    service, recording = _service(tmp_path)
    sfreq = 200.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    _provide_data(monkeypatch, service, data, sfreq, ["F3"])
    config = AnalysisConfigRequest(
        mode="spectrogram", channels=["F3"], time={"start_s": 0, "end_s": 30},
        custom_frequency_range={"low_hz": 8, "high_hz": 13},
    )

    payload = service.load_configured_spectrogram(recording, config)

    np.testing.assert_allclose(
        payload["custom_band_power_timeseries"]["F3"],
        payload["band_power_timeseries"]["F3"]["alpha"],
    )
    assert payload["custom_band"] == {
        "low_hz": 8.0,
        "high_hz": 13.0,
        "unit": "uV^2",
        "integration": "trapezoid_with_interpolated_boundaries",
        "frequency_resolution_hz": 0.25,
        "frequency_points_hz": list(np.arange(8.0, 13.25, 0.25)),
        "algorithm_version": "spectrogram-custom-band-v1",
    }


def test_spectrogram_times_are_window_centers_and_shape_is_stable():
    sfreq = 500.0
    times = np.arange(30 * int(sfreq)) / sfreq
    data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
    centers, frequencies, power = estimate_spectrogram(data, sfreq)
    assert len(centers) == 27
    assert centers[0] == 2.0
    assert centers[-1] == 28.0
    assert len(frequencies) == 117
    assert power.shape == (27, 1, 117)


def test_spectrogram_quality_keeps_bad_window_position():
    sfreq = 200.0
    values = np.zeros((8 * int(sfreq), 1))
    values[4 * int(sfreq):5 * int(sfreq)] = 200e-6
    centers, _freqs, power, quality = estimate_spectrogram_with_quality(values, sfreq)
    assert centers.tolist() == [2.0, 3.0, 4.0, 5.0, 6.0]
    assert len(quality) == 5
    assert quality[1]["status"] == "bad"
    assert quality[1]["reason"] == "amplitude_threshold"
    assert np.isnan(power[1]).all()
    assert quality[2]["status"] == "bad"
