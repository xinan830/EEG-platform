from __future__ import annotations

import hashlib
import importlib.metadata
import ast
import json
from pathlib import Path

import numpy as np
import pytest

from app.tnk_reference.metrics import (
    brainbeat_flat_from_psd, brainbeat_from_psd, brainbeat_ratio,
    composite_meditation, faa, fatigue_ratio, hai,
    rbp, relaxation, spatial_distribution, stability_score,
)
from app.tnk_reference.metrics import residual_spectrum
from app.tnk_reference.iapf import (
    alpha_residual_cog, decide_iapf, fit_fixed_spectrum, select_iapf_peak,
)
from app.tnk_reference.fixtures import faa_report, generate_analytic_fixture
from app.tnk_reference.primitives import band_power, faa_epoch_psd, welch_2s
from app.tnk_reference.state import IapfLockState, LogEmaState, StabilityHistory


def test_reference_manifest_pins_source_environment_fixture_and_formula_hash():
    path = Path(__file__).parent / "fixtures" / "tnk_reference_manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    formula = json.dumps(manifest["fixture"]["channel_formulas"], sort_keys=True,
                         separators=(",", ":")).encode("utf-8")
    assert hashlib.sha256(formula).hexdigest() == manifest["fixture"]["formula_sha256"]
    assert manifest["reference_id"] == "tnk-ant-reference-v1"
    assert manifest["environment"]["specparam"] == "2.0.0rc6"
    assert manifest["fixture"]["sample_layout"] == "samples_by_channels"
    assert manifest["fixture"]["unit"] == "V"
    time_s, values_v, channels = generate_analytic_fixture(path)
    assert values_v.shape == (4000, 5)
    assert np.array_equal(channels, ["Fz", "Pz", "Oz", "F3", "F4"])
    assert time_s[0] == 0.0 and time_s[-1] < 40.0
    assert np.isfinite(values_v).all()


def test_frozen_welch_reference_array_and_ant_sources_match_recorded_identity():
    path = Path(__file__).parent / "fixtures" / "tnk_reference_manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    expected = manifest["environment"]
    actual = {
        "numpy": np.__version__,
        "scipy": importlib.metadata.version("scipy"),
        "specparam": importlib.metadata.version("specparam"),
    }
    for package, version in actual.items():
        if version != expected[package]:
            pytest.skip(f"frozen reference needs {package}=={expected[package]}, found {version}")

    repo_root = Path(__file__).resolve().parents[2]
    ant_root = repo_root / "ANT-Amplify" / "eeg-process-main"
    for relative, digest in manifest["source_identity"]["source_sha256"].items():
        source = ant_root / relative
        assert hashlib.sha256(source.read_bytes()).hexdigest().upper() == digest

    _, values, _ = generate_analytic_fixture(path)
    freqs, psd = welch_2s(values[:, 0], manifest["fixture"]["sampling_rate_hz"])
    arrays = np.concatenate((np.asarray(freqs, dtype="<f8").ravel(),
                             np.asarray(psd, dtype="<f8").ravel())).tobytes()
    frozen = manifest["frozen_reference_arrays"]
    assert list(freqs.shape) == frozen["frequency_shape"]
    assert list(psd.shape) == frozen["psd_shape"]
    assert hashlib.sha256(arrays).hexdigest() == frozen["welch_2s_fz_frequency_psd_sha256_le_f64"]


def test_reference_package_stays_outside_production_calculator_and_registry_imports():
    repo_root = Path(__file__).resolve().parents[2]
    reference_root = repo_root / "backend" / "app" / "tnk_reference"
    for source in reference_root.glob("*.py"):
        tree = ast.parse(source.read_text(encoding="utf-8"), filename=str(source))
        imported = []
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imported.extend(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                imported.append(node.module)
        assert not any(name.startswith(("app.algorithms", "app.scientific")) for name in imported), source

    registry = (repo_root / "backend" / "app" / "algorithms" / "builtin_modules.py").read_text(encoding="utf-8")
    assert "tnk_reference" not in registry


def test_welch_reference_axis_and_parseable_sine_power():
    sfreq = 128.0
    time = np.arange(int(8 * sfreq)) / sfreq
    values = np.column_stack((20e-6 * np.sin(2 * np.pi * 10 * time),))
    freqs, psd = welch_2s(values, sfreq)
    assert np.array_equal(freqs, np.fft.rfftfreq(2 * int(sfreq), d=1 / sfreq))
    assert psd.shape == (1, len(freqs))
    assert freqs[np.argmax(psd[0])] == 10.0
    assert np.trapezoid(psd[0], freqs) == pytest.approx((20e-6) ** 2 / 2, rel=0.03)


def test_welch_rejects_nonfinite_and_malformed_input():
    with pytest.raises(ValueError):
        welch_2s(np.array([[np.nan]]), 128.0)
    with pytest.raises(ValueError):
        welch_2s(np.ones((8, 2, 1)), 128.0)


def test_faa_pairs_artifacts_discards_tail_and_reports_exact_clean_count():
    sfreq = 20.0
    time = np.arange(int(24 * sfreq)) / sfreq
    f3 = 10e-6 * np.sin(2 * np.pi * 10 * time)
    f4 = 20e-6 * np.sin(2 * np.pi * 10 * time)
    pair = np.column_stack((f3, f4))
    pair[10 * int(sfreq):11 * int(sfreq), 0] += 800e-6
    result = faa_epoch_psd(pair, sfreq)
    assert result["total_epochs"] == 23
    assert result["clean_epochs"] == 21
    output = faa(result["psd"], result["frequencies_hz"], clean_epochs=result["clean_epochs"])
    assert output["clean_epochs"] == 21
    assert output["faa"] == pytest.approx(np.log(4), abs=0.02)
    rejected = faa(result["psd"], result["frequencies_hz"], clean_epochs=9)
    assert rejected["faa"] is None
    assert rejected["reason"] == "too_few_clean_epochs"


def test_faa_rejects_invalid_epoch_contract():
    with pytest.raises(ValueError):
        faa_epoch_psd(np.ones((10, 3)), 128.0)
    with pytest.raises(ValueError):
        faa_epoch_psd(np.ones((10, 2)), 128.0, overlap=1.0)


def test_faa_report_discards_leading_transition_and_keeps_sample_bounds():
    sfreq = 100.0
    time = np.arange(int(30 * sfreq)) / sfreq
    values = np.column_stack((10e-6 * np.sin(2 * np.pi * 10 * time),
                              20e-6 * np.sin(2 * np.pi * 10 * time)))
    result = faa_report(values, sfreq)
    assert result["sample_bounds"] == [1200, 3000]
    assert result["faa"] == pytest.approx(np.log(4), abs=0.02)
    short = faa_report(values[:1000], sfreq)
    assert short["faa"] is None
    assert short["reason"] == "too_short"


def test_band_power_uses_closed_grid_and_trapezoid():
    freqs = np.array([1.0, 2.0, 3.0, 4.0])
    assert band_power(freqs, np.ones(4), 2.0, 4.0) == pytest.approx(2.0)
    assert band_power(freqs, np.ones(4), 2.1, 2.9) == 0.0
    with pytest.raises(ValueError):
        band_power(freqs, np.ones(3), 1.0, 4.0)


def test_rbp_hai_and_missing_power_semantics():
    freqs = np.arange(0.0, 31.0)
    psd = np.ones_like(freqs)
    assert sum(rbp(freqs, psd).values()) == pytest.approx(1.0)
    assert hai(freqs, psd) == pytest.approx(np.log10(12 / 7))
    assert rbp(freqs, np.zeros_like(freqs)) is None
    assert hai(freqs, np.zeros_like(freqs)) is None


def test_relaxation_and_spatial_distribution_preserve_validity_separately():
    assert relaxation(0.15, 1.0) == pytest.approx(50.0)
    assert relaxation(0.15, 0.0) is None
    value = spatial_distribution(0.045, 0.1)
    assert value["value"] == pytest.approx(50.0)
    assert value["valid"] is True
    assert spatial_distribution(0.001, 0.01)["valid"] is False


def test_composite_and_scalar_ratios_match_frozen_formulas():
    assert brainbeat_ratio(0.2, 0.1) == pytest.approx(2.0)
    assert fatigue_ratio(0.3, 0.1) == pytest.approx(3.0)
    combined = composite_meditation(40.0, 80.0, sd_valid=True, stability_ready=True)
    assert combined["value"] == pytest.approx(72.0)
    assert combined["quality"] == "clean"
    degraded = composite_meditation(40.0, 80.0, sd_valid=False, stability_ready=True)
    assert degraded["value"] == pytest.approx(80.0)
    assert degraded["quality"] == "degraded"


def test_dynamic_brainbeat_uses_distinct_fz_pz_relative_powers():
    freqs = np.arange(0.0, 30.01, 0.25)
    fz = np.ones_like(freqs)
    pz = np.ones_like(freqs)
    result = brainbeat_from_psd(freqs, fz, pz, 10.0)
    assert result is not None
    assert result["value"] == pytest.approx(1.0)
    fz[(freqs >= 4) & (freqs <= 8)] *= 3
    assert brainbeat_from_psd(freqs, fz, pz, 10.0)["value"] > result["value"]


def test_brainbeat_flat_subtracts_per_channel_aperiodic_background():
    freqs = np.arange(1.0, 30.01, 0.25)
    psd = np.power(10.0, -8.0 - 1.5 * np.log10(freqs))
    psd += 1e-8 * np.exp(-0.5 * ((freqs - 10.0) / 1.0) ** 2)
    fit = {"aperiodic_offset": -8.0, "aperiodic_exponent": 1.5}
    value = brainbeat_flat_from_psd(freqs, psd, psd, 10.0, fit, fit)
    residual = residual_spectrum(freqs, psd, -8.0, 1.5)
    expected = band_power(freqs, residual, 4.0, 8.0) / band_power(freqs, residual, 8.0, 12.0)
    assert value == pytest.approx(expected)


def test_log_ema_warmup_invalids_and_reset_by_new_instance():
    state = LogEmaState(warmup_count=3, alpha=0.15)
    assert state.update(None) is None
    assert state.update(1.0) is None
    assert state.update(4.0) is None
    assert state.update(9.0) == pytest.approx(36 ** (1 / 3))
    assert state.update(9.0) == pytest.approx(np.exp(0.15 * np.log(9) + 0.85 * np.log(36 ** (1 / 3))))
    assert LogEmaState().update(1.0) is None


def test_stability_history_readiness_and_iapf_lock_transitions():
    stability = StabilityHistory()
    assert stability.update("Fz", 0.4) == pytest.approx(0.65)
    for _ in range(7):
        value = stability.update("Fz", 0.4)
    assert value == pytest.approx(1.0)
    lock = IapfLockState()
    assert lock.update(10.0) is None
    assert lock.update(None) is None
    assert lock.update(11.0) is None
    assert lock.update(12.0) == pytest.approx(11.0)
    assert lock.update(14.0) == pytest.approx(11.0)
    score = stability_score({"Fz": 0.65, "Pz": 0.65}, ready_channels={"Fz"})
    assert score["value"] == pytest.approx(50.0)
    assert score["ready"] is False
    assert score["channels"] == ["Fz", "Pz"]


def test_iapf_reference_fit_and_residual_cog_use_frozen_peak_model():
    freqs = np.arange(3.0, 30.0001, 0.25)
    psd = 1e-10 / freqs ** 1.5 + 5e-10 * np.exp(-0.5 * ((freqs - 10.5) / 0.8) ** 2)
    fit = fit_fixed_spectrum(freqs, psd)
    selected = select_iapf_peak(fit)
    assert selected["peak_exists"] is True
    assert selected["gaussian_cf_hz"] == pytest.approx(10.5, abs=0.4)
    assert fit["aperiodic_exponent"] == pytest.approx(1.5, abs=0.3)
    assert fit["r_squared"] > 0.8
    assert alpha_residual_cog(freqs, psd, fit) == pytest.approx(10.5, abs=0.4)
    assert decide_iapf(fit, 10.5)["source"] == "peak"
    low_quality = dict(fit, r_squared=0.1)
    assert decide_iapf(low_quality, 10.5)["reason"] == "low_r2"
