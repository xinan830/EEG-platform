import pytest

from app.services.analysis_input_validation import AnalysisInputValidationError, validate_recording_analysis_input


def test_rejects_non_positive_sampling_rate_before_analysis():
    with pytest.raises(AnalysisInputValidationError, match="采样率") as raised:
        validate_recording_analysis_input(10.0, 0.0, ["F3"], 0.0, 4.0, ["F3"], (1.0, 30.0))
    assert raised.value.code == "ANALYSIS_SAMPLING_RATE_INVALID"


def test_rejects_interval_channel_and_nyquist_using_real_recording_facts():
    with pytest.raises(AnalysisInputValidationError, match="超出文件范围"):
        validate_recording_analysis_input(10.0, 100.0, ["F3"], 8.0, 12.0, ["F3"], (1.0, 30.0))
    with pytest.raises(AnalysisInputValidationError, match="不存在的通道"):
        validate_recording_analysis_input(10.0, 100.0, ["F3"], 0.0, 4.0, ["Fz"], (1.0, 30.0))
    with pytest.raises(AnalysisInputValidationError, match="Nyquist"):
        validate_recording_analysis_input(10.0, 20.0, ["F3"], 0.0, 4.0, ["F3"], (1.0, 30.0))
