import numpy as np
import pytest

from app.services.montage import apply_montage, build_montage, resolve_channel


def test_average_reference_uses_all_available_channels_without_mutating_input():
    source = np.array([[1.0, 3.0, 5.0]])
    definition = build_montage("average", ["F3", "Fz", "Pz"], ["F3", "Pz"])
    result = apply_montage(source, ["F3", "Fz", "Pz"], definition)
    np.testing.assert_allclose(result, [[-2.0, 2.0]])
    np.testing.assert_array_equal(source, [[1.0, 3.0, 5.0]])


def test_custom_bipolar_and_legacy_aliases():
    assert resolve_channel("T3", ["T7"]) == "T7"
    definition = build_montage("custom_bipolar", ["F3", "Fz", "F4"], ["F3", "Fz", "F4"])
    result = apply_montage(np.array([[4.0, 1.0, -2.0]]), ["F3", "Fz", "F4"], definition)
    np.testing.assert_allclose(result, [[3.0, 3.0]])


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


def test_standard_montage_reports_missing_channels_instead_of_partial_output():
    available = ["Fp1", "Fp2", "F7", "F3", "F4", "F8", "T7", "C3", "Cz", "C4", "T8", "P7", "P3", "Pz", "P4", "P8"]
    definition = build_montage("standard_16", available)
    assert len(definition.channels) == 16
    with pytest.raises(ValueError, match="缺少通道"):
        build_montage("standard_18", available)
