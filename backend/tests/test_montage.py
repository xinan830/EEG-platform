import numpy as np
import pytest

from app.services.montage import (
    apply_montage,
    build_montage,
    describe_montages,
    montage_formulas,
    resolve_channel,
)


def test_average_reference_uses_all_available_channels_without_mutating_input():
    source = np.array([[1.0, 3.0, 5.0]])
    definition = build_montage("average", ["F3", "Fz", "Pz"], ["F3", "Pz"])
    result = apply_montage(source, ["F3", "Fz", "Pz"], definition)
    np.testing.assert_allclose(result, [[-2.0, 2.0]])
    np.testing.assert_array_equal(source, [[1.0, 3.0, 5.0]])


def test_custom_montage_uses_explicit_weighted_terms_and_legacy_aliases():
    assert resolve_channel("T3", ["T7"]) == "T7"
    definition = build_montage(
        "custom_bipolar",
        ["F3", "Fz", "F4"],
        custom_channels=[
            {"name": "Left", "terms": [{"channel": "F3", "weight": 1}, {"channel": "Fz", "weight": -1}]},
            {"name": "Average pair", "terms": [{"channel": "Fz", "weight": 1}, {"channel": "F3", "weight": -0.5}, {"channel": "F4", "weight": -0.5}]},
        ],
    )
    result = apply_montage(np.array([[4.0, 1.0, -2.0]]), ["F3", "Fz", "F4"], definition)
    np.testing.assert_allclose(result, [[3.0, 0.0]])
    assert [item.name for item in definition.channels] == ["Left", "Average pair"]
    assert montage_formulas(definition) == {"Left": "F3 - Fz", "Average pair": "Fz - 0.5*F3 - 0.5*F4"}


@pytest.mark.parametrize(
    ("rows", "message"),
    [
        (None, "至少需要一条"),
        ([{"name": "X", "terms": []}], "至少需要一个"),
        ([{"name": "X", "terms": [{"channel": "Missing", "weight": 1}]}], "不存在"),
        ([{"name": "X", "terms": [{"channel": "F3", "weight": 0}]}], "非零有限数"),
        ([{"name": "X", "terms": [{"channel": "F3", "weight": 1}, {"channel": "F3", "weight": -1}]}], "重复使用通道"),
        ([
            {"name": "X", "terms": [{"channel": "F3", "weight": 1}]},
            {"name": "x", "terms": [{"channel": "F4", "weight": 1}]},
        ], "名称重复"),
    ],
)
def test_custom_bipolar_rejects_invalid_explicit_rows(rows, message):
    with pytest.raises(ValueError, match=message):
        build_montage("custom_bipolar", ["F3", "Fz", "F4"], custom_channels=rows)


def test_longitudinal_bipolar_rejects_incomplete_recording():
    with pytest.raises(ValueError, match="缺少通道"):
        build_montage("longitudinal_bipolar", ["F3", "Fz", "F4", "Pz"])


def test_average_reference_can_exclude_arbitrary_channels():
    source = np.array([[1.0, 3.0, 9.0]])
    definition = build_montage("average", ["F3", "EOG", "Fz"], ["F3", "Fz"], ["EOG"])
    result = apply_montage(source, ["F3", "EOG", "Fz"], definition)
    np.testing.assert_allclose(result, [[-4.0, 4.0]])
    assert definition.excluded_channels == ("EOG",)


def test_transverse_and_cz_montages_require_complete_electrodes():
    available = ["Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8", "T7", "C3", "Cz", "C4", "T8", "P7", "P3", "Pz", "P4", "P8", "O1", "O2"]
    transverse = build_montage("transverse_bipolar", available)
    cz = build_montage("cz_reference", available, ["F3", "F4"])
    assert transverse.channels[0].name == "F7-Fp1"
    assert [item.name for item in cz.channels] == ["F3-Cz", "F4-Cz"]


def test_channel_count_presets_are_not_montage_options():
    available = ["Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8", "T7", "C3", "Cz", "C4", "T8", "P7", "P3", "Pz", "P4", "P8", "O1", "O2", "Oz"]
    montage_ids = {item["id"] for item in describe_montages(available)}
    assert montage_ids.isdisjoint({"standard_16", "standard_18", "standard_20"})
    with pytest.raises(ValueError, match="不支持的导联方案"):
        build_montage("standard_18", available)
