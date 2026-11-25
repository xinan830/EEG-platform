"""Display compression must not change scientific samples or invent scalp sites."""
from types import SimpleNamespace

import numpy as np
import pytest

from app.services.preprocessing_preview import _build_ic_topomap_field, build_preprocessing_preview_window


def preview(values, *, max_points=100, start_s=0, sfreq=1000):
    source = SimpleNamespace(
        data=values, sfreq=sfreq, channel_names=tuple(f"CH{i}" for i in range(values.shape[1])),
        artifact_removal_evidence=None,
    )
    recordings = SimpleNamespace(offline_scientific_preprocessing=SimpleNamespace(
        prepare_continuous=lambda *args, **kwargs: source,
    ))
    return build_preprocessing_preview_window(
        recordings, SimpleNamespace(id="rec"), start_s=start_s, window_s=60,
        max_points=max_points,
    )


def test_preview_preserves_narrow_gaps_extrema_and_channel_specific_times():
    values = np.zeros((10000, 2))
    values[1:5, 0] = np.nan
    values[777, 0] = 150e-6
    values[778, 0] = -120e-6
    values[42:45, 1] = np.nan
    before = values.copy()
    result = preview(values)
    assert None in result["traces_uv"]["CH0"]
    assert 150.0 in result["traces_uv"]["CH0"]
    assert -120.0 in result["traces_uv"]["CH0"]
    for channel in result["channels"]:
        times = result["trace_times_s"][channel]
        trace = result["traces_uv"][channel]
        assert len(times) == len(trace)
        assert np.all(np.diff(times) > 0)
    assert result["display_compression"]["method"] == "finite_run_min_max_envelope_v1"
    assert result["display_compression"]["scientific_resampling"] is False
    np.testing.assert_array_equal(values, before)


def test_preview_keeps_absolute_time_and_high_frequency_envelope():
    values = (np.sin(2 * np.pi * 40 * np.arange(60000) / 1000) * 1e-6)[:, None]
    result = preview(values, max_points=100, start_s=3)
    trace = result["traces_uv"]["CH0"]
    assert max(trace) > 0.99 and min(trace) < -0.99
    assert result["trace_times_s"]["CH0"][0] == 3
    assert result["trace_times_s"]["CH0"][-1] == 59.999
    assert len(trace) <= 100


@pytest.mark.parametrize("names", [
    ["Fp1-F7", "F3-C3", "O1-O2"],
    ["Fp1-Cz", "F3-Cz", "O1-Cz"],
])
def test_ica_topomap_does_not_assign_derived_channels_to_first_electrode(names):
    coords = {"fp1": (-0.3, 0.7), "f3": (-0.4, 0.3), "o1": (-0.3, -0.7)}
    result = _build_ic_topomap_field(0, names, np.ones(3), coords)
    assert result["status"] == "unavailable"
    assert result["included_channels"] == []
    assert all(p["reason"] == "bipolar_or_derived_output_has_no_spatial_rule" for p in result["points"])


@pytest.mark.parametrize("suffix", ["", "-REF", "-AVG"])
def test_ica_topomap_preserves_raw_ref_and_avg_placement(suffix):
    names = [name + suffix for name in ("Fp1", "F3", "O1")]
    coords = {"fp1": (-0.3, 0.7), "f3": (-0.4, 0.3), "o1": (-0.3, -0.7)}
    result = _build_ic_topomap_field(0, names, np.ones(3), coords)
    assert result["status"] == "ready"
    assert result["included_channels"] == names


def test_ica_topomap_formula_provenance_overrides_label_heuristics():
    names = ["Fp1-REF", "F3-REF", "O1-REF"]
    formulas = {name.casefold(): {"positive": name.split("-")[0], "negative_kind": "channel"} for name in names}
    coords = {"fp1": (-0.3, 0.7), "f3": (-0.4, 0.3), "o1": (-0.3, -0.7)}
    unsupported = _build_ic_topomap_field(0, names, np.ones(3), coords, formulas=formulas)
    assert unsupported["status"] == "unavailable"
    for formula in formulas.values():
        formula["negative_kind"] = "mean"
    supported = _build_ic_topomap_field(0, names, np.ones(3), coords, formulas=formulas)
    assert supported["status"] == "ready"


def test_preview_retains_all_gaps_even_when_mandatory_boundaries_exceed_point_target():
    values = np.ones((100, 1)) * 1e-6
    values[1::2] = np.nan
    result = preview(values, max_points=16)
    assert result["traces_uv"]["CH0"].count(None) == 50
    assert len(result["trace_times_s"]["CH0"]) == 100
