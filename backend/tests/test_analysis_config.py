import pytest
from pydantic import ValidationError

from app.models.analysis_config import AnalysisConfigRequest


def test_analysis_config_accepts_safe_dynamic_options():
    config = AnalysisConfigRequest(
        mode="dynamic", channels=[" F3", "Fz"],
        time={"start_s": 20, "end_s": 30}, dynamic_window_s=20, refresh_step_s=5,
    )
    assert config.channels == ["F3", "Fz"]
    assert config.dynamic_window_s == 20


def test_analysis_config_accepts_spectrogram_mode():
    config = AnalysisConfigRequest(mode="spectrogram", channels=["F3"], time={"start_s": 0, "end_s": 4}, custom_frequency_range={"low_hz": 8, "high_hz": 13})
    assert config.mode == "spectrogram"
    assert config.custom_frequency_range.low_hz == 8


@pytest.mark.parametrize("time", [
    {"start_s": 4, "end_s": 4},
    {"start_s": 4, "end_s": 7},
    {"start_s": 0, "end_s": 121},
])
def test_analysis_config_rejects_invalid_ranges(time):
    with pytest.raises(ValidationError):
        AnalysisConfigRequest(channels=["F3"], time=time)


def test_analysis_config_rejects_duplicate_channels_case_insensitive():
    with pytest.raises(ValidationError):
        AnalysisConfigRequest(channels=["F3", "f3"], time={"start_s": 0, "end_s": 4})


@pytest.mark.parametrize("frequency_range", [
    {"low_hz": 0.5, "high_hz": 4},
    {"low_hz": 8, "high_hz": 8},
    {"low_hz": 14, "high_hz": 13},
    {"low_hz": 20, "high_hz": 31},
])
def test_analysis_config_rejects_invalid_custom_frequency_ranges(frequency_range):
    with pytest.raises(ValidationError):
        AnalysisConfigRequest(mode="spectrogram", channels=["F3"], time={"start_s": 0, "end_s": 4}, custom_frequency_range=frequency_range)


def test_analysis_config_rejects_custom_frequency_range_for_psd():
    with pytest.raises(ValidationError):
        AnalysisConfigRequest(mode="static", channels=["F3"], time={"start_s": 0, "end_s": 4}, custom_frequency_range={"low_hz": 1, "high_hz": 2})
