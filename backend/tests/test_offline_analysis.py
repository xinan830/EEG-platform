import numpy as np

from app.models.recording import ChannelMapping
from app.processing.offline_analysis import analyze_recording


def test_offline_analysis_emits_iapf_attempts_every_five_seconds_after_thirty_seconds():
    sfreq = 100
    times = np.arange(45 * sfreq) / sfreq
    alpha = 20e-6 * np.sin(2 * np.pi * 10 * times)
    data = np.column_stack([alpha * 0.6, alpha, alpha * 1.2])

    result = analyze_recording(
        data=data,
        sfreq=sfreq,
        mapping=ChannelMapping(fz="Fz", pz="Pz", oz="Oz"),
        channel_names=["Fz", "Pz", "Oz"],
        events=[],
    )

    assert [point.elapsed_s for point in result.iapf_attempts] == [30.0, 35.0, 40.0, 45.0]
    assert result.locked_iapf is not None
    assert abs(result.locked_iapf - 10.0) < 0.5
    assert result.algorithm_contract["algorithm_version"] == "offline-spectral-v3"
    assert result.analysis_input["mapped_channels"]["pz"] == "Pz"
    assert all(point.gate_failed is None for point in result.metrics)


def test_low_quality_metrics_are_unavailable_instead_of_fake_zeroes():
    sfreq = 100
    times = np.arange(12 * sfreq) / sfreq
    artifact = 500e-6 * np.sin(2 * np.pi * 10 * times)
    data = np.column_stack([artifact, artifact, artifact])
    result = analyze_recording(
        data, sfreq, ChannelMapping(fz="Fz", pz="Pz", oz="Oz"),
        ["Fz", "Pz", "Oz"], [],
    )
    assert result.metrics
    assert all(point.gate_failed == "low_quality" for point in result.metrics)
    assert all(point.relaxation is None and point.brainbeat is None for point in result.metrics)


def test_offline_analysis_computes_faa_when_f3_and_f4_are_mapped():
    sfreq = 100
    times = np.arange(45 * sfreq) / sfreq
    alpha = np.sin(2 * np.pi * 10 * times)
    data = np.column_stack([alpha * 8e-6, alpha * 10e-6, alpha * 12e-6, alpha * 10e-6, alpha * 20e-6])
    result = analyze_recording(
        data, sfreq, ChannelMapping(fz="Fz", pz="Pz", oz="Oz", f3="F3", f4="F4"),
        ["Fz", "Pz", "Oz", "F3", "F4"], [],
    )
    assert result.faa["reason"] == ""
    assert np.isclose(result.faa["faa"], np.log(4.0), atol=1e-6)
