from types import SimpleNamespace

import mne
import numpy as np
import pytest

from app.scientific.contracts.preprocessing import BadChannelSpec
from app.services.preprocessing_preview import attach_ica_component_visuals, build_preprocessing_preview_window


@pytest.mark.parametrize("names,formulas,expected", [
    (["F4-C4", "C4-P4", "F3-O1"], [
        {"name": "F4-C4", "positive": "F4", "negative_labels": ["C4"]},
        {"name": "C4-P4", "positive": "C4", "negative_labels": ["P4"]},
        {"name": "F3-O1", "positive": "F3", "negative_labels": ["O1"]},
    ], ["F4-C4", "C4-P4"]),
    (["F3-AVG", "O1-AVG"], [
        {"name": name, "positive": name.split("-")[0], "negative_labels": ["F3", "C4", "O1"]}
        for name in ("F3-AVG", "O1-AVG")
    ], ["F3-AVG", "O1-AVG"]),
    (["c4", "F3"], [], ["c4"]),
    (["C4-fake"], [], []),
])
def test_interpolated_outputs_follow_positive_and_reference_dependencies(names, formulas, expected):
    prepared = SimpleNamespace(data=np.zeros((100, len(names))), sfreq=100,
                               channel_names=names, artifact_removal_evidence=None)
    recordings = SimpleNamespace(
        offline_scientific_preprocessing=SimpleNamespace(prepare_continuous=lambda *a, **k: prepared),
        analysis_signal_provenance=lambda *a: {"formulas": formulas},
    )
    result = build_preprocessing_preview_window(
        recordings, SimpleNamespace(id="rec"), start_s=0, window_s=1,
        bad_channel_spec=BadChannelSpec(policy="interpolate", channels=("C4",)),
    )
    assert result["interpolated_channels"] == expected


class IdentityIca:
    def get_components(self):
        return np.eye(3)

    def get_sources(self, raw):
        return raw


def test_ica_envelope_keeps_narrow_pulse_gaps_and_original_time_coordinates():
    data = np.zeros((5000, 3), dtype=np.float64)
    data[85:95, 0] = 10
    data[450, 0] = -5
    data[600:650] = np.nan
    before = data.copy()
    fitted = SimpleNamespace(channel_names=("Fp1", "F3", "O1"), sampling_rate_hz=500,
                             info=mne.create_info(["Fp1", "F3", "O1"], 500, "eeg"), ica=IdentityIca())
    report = attach_ica_component_visuals({"components": [{"component_index": 0}]}, fitted, data)
    component = report["components"][0]
    wave = component["preview_waveform"]
    finite = data[np.isfinite(data[:, 0]), 0]
    expected_peak = (10 - finite.mean()) / finite.std()
    assert max(value for value in wave if value is not None) == pytest.approx(expected_peak, abs=5e-5)
    assert None in wave
    times = component["preview_times_s"]
    assert len(times) == len(wave) and np.all(np.diff(times) > 0)
    assert times[0] == 0 and times[-1] == 9.998
    assert times[wave.index(None)] == 1.2
    assert any(.17 <= time <= .188 and value == max(v for v in wave if v is not None)
               for time, value in zip(times, wave))
    assert component["preview_unit"] == "z-score"
    assert component["preview_compression"]["method"] == "finite_run_min_max_envelope_v1"
    assert component["preview_duration_s"] == 10
    np.testing.assert_array_equal(data, before)


def test_ica_preview_all_missing_retains_unavailable_time_instead_of_inventing_zero():
    data = np.full((500, 3), np.nan)
    fitted = SimpleNamespace(channel_names=("Fp1", "F3", "O1"), sampling_rate_hz=500,
                             info=mne.create_info(["Fp1", "F3", "O1"], 500, "eeg"), ica=IdentityIca())
    report = attach_ica_component_visuals({"components": [{"component_index": 0}]}, fitted, data)
    component = report["components"][0]
    assert component["preview_waveform"] == [None]
    assert component["preview_times_s"] == [0]
    assert component["preview_duration_s"] == 1
