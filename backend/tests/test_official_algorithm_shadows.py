import numpy as np
import pytest

from app.eeg_core.official_algorithms.validation import shadow_brainbeat, shadow_brainbeat_ema, shadow_faa, shadow_iapf, shadow_rbp, shadow_theta_beta
from app.algorithms.catalog import ensure_official_definitions, official_definition, official_definition_draft
from app.scientific.primitives.spectral import SpectralEstimate
from app.services.definitions import DefinitionService


def test_rbp_shadow_matches_legacy_band_integrals_in_requested_channel_order():
    frequencies = np.arange(1.0, 30.25, 0.25)
    psd = np.vstack((np.ones_like(frequencies), 2.0 + frequencies / 30.0)) * 1e-12
    report = shadow_rbp(frequencies, psd, ["Oz", "F3"])
    assert report["passed"] is True
    assert report["channels"] == ["Oz", "F3"]
    assert all(row["max_absolute_error"] <= 1e-12 for row in report["bands"])


def test_official_definition_metadata_retains_distinct_faa_brainbeat_and_iapf_semantics():
    assert official_definition("faa")["paired_quality"] is True
    assert official_definition("brainbeat")["stateful_ema"] is True
    assert official_definition("iapf")["sources"] == ["peak", "cog"]


def test_faa_shadow_matches_independent_paired_epoch_reference():
    sfreq = 100.0
    times = np.arange(int(30 * sfreq)) / sfreq
    report = shadow_faa(10e-6 * np.sin(2 * np.pi * 10 * times), 20e-6 * np.sin(2 * np.pi * 10 * times), sfreq)
    assert report["passed"] is True
    assert report["candidate"]["faa"] == pytest.approx(np.log(4.0), abs=1e-12)


def test_brainbeat_shadow_keeps_realtime_frame_and_log_ema_semantics_separate():
    frequencies = np.arange(0.0, 31.0, 0.5)
    fz = np.ones_like(frequencies)
    pz = np.ones_like(frequencies)
    fz[(frequencies >= 4.0) & (frequencies <= 8.0)] = 3.0
    pz[(frequencies >= 8.0) & (frequencies <= 12.0)] = 2.0
    frame = shadow_brainbeat(frequencies, fz, pz, 10.0)
    ema = shadow_brainbeat_ema([frame["legacy"], frame["legacy"] * 2.0, frame["legacy"] / 2.0], warmup_epochs=2, ema_alpha=0.25)
    assert frame["passed"] is True
    assert ema["passed"] is True
    assert ema["frames"][0]["legacy"] is None
    assert ema["frames"][1]["legacy"] is not None


def test_iapf_shadow_matches_peak_and_low_quality_semantics():
    frequencies = np.arange(1.0, 30.25, 0.25)
    baseline = 1e-12 / frequencies
    peak = baseline + 8e-12 * np.exp(-0.5 * ((frequencies - 10.0) / 0.5) ** 2)
    spectrum = SpectralEstimate(frequencies, np.vstack((peak, peak)), 1.0, 14, 14, None)
    report = shadow_iapf(spectrum)
    assert report["passed"] is True
    assert report["candidate"]["source"] == "peak"
    failed = shadow_iapf(SpectralEstimate(np.array([]), np.empty((1, 0)), 0.0, 0, 0, "low_quality"))
    assert failed["passed"] is True
    assert failed["candidate"]["gate_failed"] == "low_quality"


def test_theta_beta_shadow_is_independent_from_brainbeat_and_preserves_channel_order():
    frequencies = np.arange(1.0, 30.25, 0.25)
    psd = np.ones((3, len(frequencies))) * 1e-12
    psd[0, (frequencies >= 4.0) & (frequencies <= 8.0)] *= 3.0
    psd[1, (frequencies >= 4.0) & (frequencies <= 8.0)] *= 2.0
    spectrum = SpectralEstimate(frequencies, psd, 1.0, 14, 14, None)
    report = shadow_theta_beta(spectrum, 10.0, ("Fz", "Pz", "O2"))
    assert report["passed"] is True
    assert report["source_channels"] == ["Fz", "Pz", "O2"]
    assert report["output_channels"] == ["Fz", "Pz", "Oz"]
    assert report["candidate"]["Fz"] > report["candidate"]["Pz"]


def test_official_definitions_are_immutable_published_records_without_a_shadow_cutover(tmp_path):
    service = DefinitionService(tmp_path / "official-definitions.sqlite3")
    first = ensure_official_definitions(service)
    second = ensure_official_definitions(service)
    assert first == second
    assert len(service.list()) == 7
    assert official_definition_draft("rbp").graph["outputs"] == ["delta_rbp", "theta_rbp", "alpha_rbp", "beta_rbp"]
    assert official_definition_draft("iapf").quality_rules["execution_kind"] == "official_composite_run_adapter"
