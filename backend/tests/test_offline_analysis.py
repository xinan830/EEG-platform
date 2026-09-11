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
