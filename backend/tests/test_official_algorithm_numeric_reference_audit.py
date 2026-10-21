"""Independent, reproducible numerical references for official algorithms.

Expected values are recomputed from analytic synthetic inputs using explicit
NumPy formulae. Production functions are imported only on the actual side of
comparisons. This is engineering validation, not clinical validation.
"""

from __future__ import annotations

import hashlib
import importlib.metadata
import json
from pathlib import Path

import numpy as np
import pytest
from scipy import signal

from app.algorithms.band_ratio.compute import compute_band_ratio
from app.algorithms.brainbeat.compute import compute_brainbeat
from app.algorithms.faa.official import compute_faa
from app.algorithms.iapf.official import estimate_iapf
from app.algorithms.peak_frequency.compute import compute_peak_frequency
from app.algorithms.rbp.official import five_band_shares
from app.algorithms.theta_beta.compute import compute_theta_beta
from app.models.run import ValidationCreateRequest
from app.scientific.primitives.spectral import (
    SpectralEstimate,
    estimate_spectrogram,
    estimate_welch_psd,
)
from app.services.validations import ValidationService


NUMPY_VERSION = "2.3.4"
SCIPY_VERSION = "1.16.3"
RTOL = 1e-10
ATOL_RATIO = 1e-12
ATOL_PSD = 1e-24
REFERENCE_ID = "analytic-numpy-reference-v1"

assert importlib.metadata.version("numpy") == NUMPY_VERSION
assert importlib.metadata.version("scipy") == SCIPY_VERSION


def _spectrum(freqs: np.ndarray, psd: np.ndarray, sfreq: float = 100.0) -> SpectralEstimate:
    return SpectralEstimate(
        freqs=np.asarray(freqs, dtype=float),
        psd=np.asarray(psd, dtype=float),
        signal_quality=1.0,
        clean_epochs=1,
        total_epochs=1,
        gate_failed=None,
        evidence={"sfreq_hz": sfreq, "reference": "original_recording_no_software_rereference"},
    )


def _reference_band_power(freqs: np.ndarray, psd: np.ndarray, low: float, high: float) -> np.ndarray:
    """Linear edge interpolation and composite trapezoid, independently coded."""
    axis = np.concatenate(([low], freqs[(freqs > low) & (freqs < high)], [high]))
    rows = np.asarray(psd, dtype=float).reshape((-1, len(freqs)))
    output = []
    for row in rows:
        edge_values = np.interp(axis, freqs, row)
        output.append(sum(
            (axis[i + 1] - axis[i]) * (edge_values[i + 1] + edge_values[i]) * 0.5
            for i in range(len(axis) - 1)
        ))
    return np.asarray(output).reshape(np.asarray(psd).shape[:-1])


def _reference_periodogram(segment: np.ndarray, sfreq: float) -> tuple[np.ndarray, np.ndarray]:
    """One-sided Welch segment using a periodic Hann and Parseval density scale."""
    values = np.asarray(segment, dtype=np.float64)
    values = values - np.mean(values, axis=0, keepdims=True)
    n = len(values)
    window = 0.5 - 0.5 * np.cos(2 * np.pi * np.arange(n) / n)
    transformed = np.fft.rfft(values * window[:, None], axis=0)
    density = np.abs(transformed) ** 2 / (sfreq * np.sum(window**2))
    if n % 2 == 0:
        density[1:-1] *= 2.0
    else:
        density[1:] *= 2.0
    return np.fft.rfftfreq(n, 1.0 / sfreq), density.T


def _reference_stft(source: np.ndarray, sfreq: float) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    n, step = int(4 * sfreq), int(sfreq)
    frequencies = np.fft.rfftfreq(n, 1 / sfreq)
    selected = (frequencies >= 1.0) & (frequencies <= 30.0)
    window = 0.5 - 0.5 * np.cos(2 * np.pi * np.arange(n) / n)
    starts = range(0, len(source) - n + 1, step)
    frames = []
    centers = []
    for start in starts:
        frame = source[start:start + n]
        frame = frame - np.mean(frame, axis=0, keepdims=True)
        transformed = np.fft.rfft(frame * window[:, None], axis=0)
        density = np.abs(transformed) ** 2 / (sfreq * np.sum(window**2))
        density[1:-1] *= 2.0
        frames.append(density.T[:, selected])
        centers.append((start + n / 2) / sfreq)
    return np.asarray(centers), frequencies[selected], np.stack(frames)


def _reference_iapf(freqs: np.ndarray, psd: np.ndarray) -> dict[str, float | str | None]:
    mean_psd = np.mean(psd, axis=0)
    fit = (freqs >= 3) & (freqs <= 30) & ~((freqs >= 7) & (freqs <= 13))
    slope, intercept = np.polyfit(np.log10(freqs[fit]), np.log10(mean_psd[fit]), 1)
    log_residual = np.log10(mean_psd[fit]) - (intercept + slope * np.log10(freqs[fit]))
    error = float(np.mean(np.abs(log_residual)))
    total = float(np.sum((np.log10(mean_psd[fit]) - np.mean(np.log10(mean_psd[fit]))) ** 2))
    r2 = float(1 - np.sum(log_residual**2) / total) if total > 0 else None
    alpha = (freqs >= 7) & (freqs <= 13)
    alpha_f = freqs[alpha]
    baseline = 10 ** (intercept + slope * np.log10(alpha_f))
    residual = np.maximum(mean_psd[alpha] - baseline, 0)
    cog = float(np.sum(alpha_f * residual) / np.sum(residual)) if np.sum(residual) > 0 else None
    index = int(np.argmax(residual))
    peak = float(alpha_f[index])
    prominence = float(residual[index] / max(baseline[index], 1e-20))
    value = peak if prominence >= 0.20 else cog
    return {
        "value": value,
        "source": "peak" if prominence >= 0.20 else ("cog" if cog is not None else None),
        "peak_hz": peak,
        "cog_hz": cog,
        "model_error": error,
        "model_r2": r2,
    }


def _reference_brainbeat(freqs: np.ndarray, fz: np.ndarray, pz: np.ndarray, iapf: float) -> float:
    def selected_trapezoid(values: np.ndarray, low: float, high: float) -> float:
        mask = (freqs >= low) & (freqs <= high)
        return float(np.trapezoid(values[mask], freqs[mask])) if np.count_nonzero(mask) >= 2 else 0.0

    fz_total = selected_trapezoid(fz, 1, 30) + 1e-20
    pz_total = selected_trapezoid(pz, 1, 30) + 1e-20
    theta = selected_trapezoid(fz, max(4, iapf - 6), iapf - 2) / fz_total
    alpha = selected_trapezoid(pz, iapf - 2, iapf + 2) / pz_total
    return theta / (alpha + 1e-20)


def _max_error(expected: np.ndarray, actual: np.ndarray) -> tuple[float, tuple[int, ...]]:
    delta = np.abs(np.asarray(actual) - np.asarray(expected))
    flat = int(np.argmax(delta))
    return float(delta.flat[flat]), tuple(int(i) for i in np.unravel_index(flat, delta.shape))


def test_analytic_spectral_reference_max_error_and_failure_coordinates():
    sfreq = 100.0
    time = np.arange(800) / sfreq
    source = np.column_stack((
        20e-6 * np.sin(2 * np.pi * 10.1 * time) + 4e-6 * np.sin(2 * np.pi * 6.25 * time),
        12e-6 * np.sin(2 * np.pi * 17.1 * time),
    ))
    n = 400
    window = 0.5 - 0.5 * np.cos(2 * np.pi * np.arange(n) / n)
    full_frequency = np.fft.rfftfreq(n, 1 / sfreq)
    selected = (full_frequency >= 1) & (full_frequency <= 30)
    reference_windows = []
    for start in (0, 200, 400):
        frequencies, density = _reference_periodogram(source[start:start + n], sfreq)
        np.testing.assert_array_equal(frequencies, full_frequency)
        reference_windows.append(density[:, selected])
    welch_expected = np.maximum(np.mean(np.stack(reference_windows), axis=0), 1e-20)
    welch_actual = estimate_welch_psd(source, sfreq).psd
    welch_abs, welch_abs_at = _max_error(welch_expected, welch_actual)
    welch_rel_array = np.abs(welch_actual - welch_expected) / np.maximum(
        np.maximum(np.abs(welch_actual), np.abs(welch_expected)), np.finfo(float).tiny
    )
    welch_rel_at = tuple(int(i) for i in np.unravel_index(int(np.argmax(welch_rel_array)), welch_rel_array.shape))
    assert welch_abs <= 1e-24
    assert welch_rel_array[welch_rel_at] <= RTOL
    assert welch_abs_at == (0, 36)
    assert welch_rel_at == (0, 76)

    stft_expected = []
    for start in range(0, len(source) - n + 1, int(sfreq)):
        _, density = _reference_periodogram(source[start:start + n], sfreq)
        stft_expected.append(density[:, selected])
    stft_expected_array = np.stack(stft_expected)
    stft_actual = estimate_spectrogram(source, sfreq)[2]
    stft_abs, stft_abs_at = _max_error(stft_expected_array, stft_actual)
    stft_rel_array = np.abs(stft_actual - stft_expected_array) / np.maximum(
        np.maximum(np.abs(stft_actual), np.abs(stft_expected_array)), np.finfo(float).tiny
    )
    stft_rel_at = tuple(int(i) for i in np.unravel_index(int(np.argmax(stft_rel_array)), stft_rel_array.shape))
    assert stft_abs <= 1e-24
    stft_limit = ATOL_PSD + RTOL * np.abs(stft_expected_array)
    assert np.all(np.abs(stft_actual - stft_expected_array) <= stft_limit)
    assert stft_abs_at == (1, 0, 36)
    assert stft_rel_at == (3, 1, 3)
    assert stft_abs == pytest.approx(2.0679515313825692e-25, rel=RTOL, abs=1e-30)
    assert stft_rel_array[stft_rel_at] == pytest.approx(1.9214598813192446e-10, rel=RTOL)


def test_independent_welch_periodogram_and_stft_match_exact_axes_and_matrices():
    sfreq = 100.0
    t = np.arange(8 * int(sfreq)) / sfreq
    source = np.column_stack((
        20e-6 * np.sin(2 * np.pi * 10.1 * t) + 4e-6 * np.sin(2 * np.pi * 6.25 * t),
        12e-6 * np.sin(2 * np.pi * 17.1 * t),
    ))
    per_segment = [
        _reference_periodogram(source[start:start + 400], sfreq)[1]
        for start in (0, 200, 400)
    ]
    full_f = np.fft.rfftfreq(400, 1 / sfreq)
    selected = (full_f >= 1.0) & (full_f <= 30.0)
    expected_f = full_f[selected]
    expected_psd = np.maximum(np.mean(np.stack(per_segment), axis=0)[:, selected], 1e-20)
    actual = estimate_welch_psd(source, sfreq)
    np.testing.assert_array_equal(actual.freqs, expected_f)
    np.testing.assert_allclose(actual.psd, expected_psd, rtol=RTOL, atol=ATOL_PSD)

    expected_times, expected_freqs, expected_matrix = _reference_stft(source, sfreq)
    times, freqs, matrix = estimate_spectrogram(source, sfreq)
    np.testing.assert_array_equal(times, expected_times)
    np.testing.assert_array_equal(freqs, expected_freqs)
    np.testing.assert_allclose(matrix, expected_matrix, rtol=RTOL, atol=ATOL_PSD)
    assert matrix.shape == (5, 2, len(freqs))


def test_peak_iapf_band_ratio_theta_beta_and_rbp_match_independent_spectrum():
    freqs = np.arange(1.0, 50.25, 0.25)
    requested = {"start_s": 2.0, "end_s": 12.0}
    actual_range = {"start_s": 2.0, "end_s": 12.0}
    base = 1e-12 / freqs
    alpha = 9e-12 * np.exp(-0.5 * ((freqs - 10.0) / 0.7) ** 2)
    psd = np.vstack((base + alpha, base + 1.5 * alpha, base + 0.8 * alpha))
    spectrum = _spectrum(freqs, psd)

    peak = compute_peak_frequency(spectrum, channel="Fz", low_hz=8, high_hz=13,
                                  requested_range=requested, actual_range=actual_range)
    assert peak.quality == "clean"
    assert peak.requested_range == requested and peak.actual_range == actual_range
    assert peak.value == freqs[(freqs >= 8) & (freqs <= 13)][int(np.argmax(psd[0, (freqs >= 8) & (freqs <= 13)]))]

    iapf_expected = _reference_iapf(freqs, psd)
    iapf_actual = estimate_iapf(spectrum)
    assert iapf_actual.source == iapf_expected["source"]
    for field in ("value", "peak_hz", "cog", "model_error", "model_r2"):
        expected_field = "cog_hz" if field == "cog" else field
        np.testing.assert_allclose(getattr(iapf_actual, field), iapf_expected[expected_field],
                                   rtol=RTOL, atol=1e-12, equal_nan=False)

    ratio = compute_band_ratio(spectrum, channel="Fz", numerator_low_hz=8, numerator_high_hz=13,
                               denominator_low_hz=13, denominator_high_hz=30,
                               requested_range=requested, actual_range=actual_range)
    assert ratio.quality == "clean"
    assert ratio.requested_range == requested and ratio.actual_range == actual_range
    ref_num = float(_reference_band_power(freqs, psd[0], 8, 13))
    ref_den = float(_reference_band_power(freqs, psd[0], 13, 30))
    np.testing.assert_allclose(ratio.value, ref_num / ref_den, rtol=RTOL, atol=ATOL_RATIO)
    assert ratio.evidence["extensions"]["band_ratio_evidence"]["numerator_power_v2"] == pytest.approx(ref_num)

    for index, name in enumerate(("Fz", "Pz", "Oz")):
        row = psd[index:index + 1]
        iapf_reference = _reference_iapf(freqs, row)["value"]
        theta_expected = (float(_reference_band_power(freqs, row[0], max(4, iapf_reference - 6), iapf_reference - 2))
                          / float(_reference_band_power(freqs, row[0], iapf_reference + 2, 30)))
        theta_actual = compute_theta_beta(_spectrum(freqs, row), channel=name,
                                         requested_range=requested, actual_range=actual_range)
        np.testing.assert_allclose(theta_actual.value, theta_expected, rtol=RTOL, atol=ATOL_RATIO)

    rbp_freqs = freqs[freqs <= 50]
    rbp_psd = psd[:1, :len(rbp_freqs)]
    powers, shares = five_band_shares(_spectrum(rbp_freqs, rbp_psd))
    bounds = {"delta": (1, 4), "theta": (4, 8), "alpha": (8, 13), "beta": (13, 30), "gamma": (30, 50)}
    expected_powers = {name: float(_reference_band_power(rbp_freqs, rbp_psd[0], *band)) * 1e12
                       for name, band in bounds.items()}
    for name in bounds:
        np.testing.assert_allclose(powers[name], expected_powers[name], rtol=RTOL, atol=ATOL_RATIO)
        np.testing.assert_allclose(shares[name], expected_powers[name] / sum(expected_powers.values()),
                                   rtol=RTOL, atol=ATOL_RATIO)


def _reference_faa(left: np.ndarray, right: np.ndarray, sfreq: float) -> tuple[float, float, float, int]:
    n = int(round(2 * sfreq))
    step = n // 2
    window = 0.5 - 0.5 * np.cos(2 * np.pi * np.arange(n) / n)
    freqs = np.fft.rfftfreq(n, 1 / sfreq)
    densities = [[], []]
    accepted = 0
    for start in range(0, min(len(left), len(right)) - n + 1, step):
        pair = [left[start:start+n], right[start:start+n]]
        pair = [x - np.mean(x) for x in pair]
        if not all(np.isfinite(x).all() and np.max(np.abs(x)) <= 150e-6 for x in pair):
            continue
        accepted += 1
        for index, x in enumerate(pair):
            fft = np.fft.rfft(x * window)
            density = np.abs(fft)**2 / (sfreq * np.sum(window**2))
            density[1:-1] *= 2
            densities[index].append(density)
    means = [np.mean(values, axis=0) for values in densities]
    p_left = float(_reference_band_power(freqs, means[0], 8, 13))
    p_right = float(_reference_band_power(freqs, means[1], 8, 13))
    return float(np.log(p_right) - np.log(p_left)), p_left, p_right, accepted


def test_faa_and_offline_brainbeat_match_independent_references_in_any_window_order():
    sfreq = 100.0
    t = np.arange(30 * int(sfreq)) / sfreq
    f3 = 10e-6 * np.sin(2 * np.pi * 10 * t)
    f4 = 20e-6 * np.sin(2 * np.pi * 10 * t)
    expected_faa, p3, p4, clean = _reference_faa(f3, f4, sfreq)
    actual_faa = compute_faa(f3, f4, sfreq)
    np.testing.assert_allclose(actual_faa["faa"], expected_faa, rtol=RTOL, atol=1e-12)
    np.testing.assert_allclose(actual_faa["p_f3"], p3, rtol=RTOL, atol=ATOL_PSD)
    np.testing.assert_allclose(actual_faa["p_f4"], p4, rtol=RTOL, atol=ATOL_PSD)
    assert actual_faa["clean_epochs"] == clean

    freqs = np.arange(1.0, 30.25, 0.25)
    baseline = 1e-12 / freqs
    bump = 8e-12 * np.exp(-0.5 * ((freqs - 10) / 0.7) ** 2)
    windows = [
        _spectrum(freqs, np.vstack((baseline + 0.5 * bump, baseline + bump)), sfreq),
        _spectrum(freqs, np.vstack((baseline + 0.8 * bump, baseline + 0.4 * bump)), sfreq),
    ]
    def evaluate(item: SpectralEstimate) -> tuple[float, float]:
        iapf_ref = _reference_iapf(item.freqs, item.psd)["value"]
        expected = _reference_brainbeat(item.freqs, item.psd[0], item.psd[1], float(iapf_ref))
        actual = compute_brainbeat(item, fz_channel="Fz", pz_channel="Pz",
                                   requested_range={"start_s": 0, "end_s": 10},
                                   actual_range={"start_s": 0, "end_s": 10})
        assert actual.quality == "clean"
        assert actual.actual_range == {"start_s": 0, "end_s": 10}
        return expected, float(actual.value)
    forward = [evaluate(window) for window in windows]
    backward = {i: evaluate(windows[i]) for i in reversed(range(len(windows)))}
    for i, (expected, actual) in enumerate(forward):
        np.testing.assert_allclose(actual, expected, rtol=RTOL, atol=ATOL_RATIO)
        assert actual == backward[i][1]


def test_reference_validationrun_and_array_artifact_record_hashes_and_error_coordinate(tmp_path: Path):
    sfreq = 100.0
    t = np.arange(4 * int(sfreq)) / sfreq
    raw = 8e-6 * np.sin(2 * np.pi * 10 * t)
    full_freqs, full_expected = _reference_periodogram(raw[:, None], sfreq)
    selected = (full_freqs >= 1.0) & (full_freqs <= 30.0)
    freqs, expected = full_freqs[selected], np.maximum(full_expected[:, selected], 1e-20)
    actual = estimate_welch_psd(raw[:, None], sfreq).psd
    error, coordinate = _max_error(expected, actual)
    artifact_path = tmp_path / "synthetic-reference-arrays.npz"
    np.savez_compressed(artifact_path, frequencies_hz=freqs, expected_psd=expected, actual_psd=actual)
    checksum = hashlib.sha256(artifact_path.read_bytes()).hexdigest()
    config = {
        "reference": REFERENCE_ID,
        "numpy": NUMPY_VERSION,
        "scipy": SCIPY_VERSION,
        "sfreq_hz": sfreq,
        "window_samples": [0, len(raw)],
        "rtol": RTOL,
        "atol": ATOL_PSD,
        "unit": "V^2/Hz",
    }
    config_hash = hashlib.sha256(json.dumps(config, sort_keys=True).encode()).hexdigest()
    service = ValidationService(tmp_path / "validation.sqlite3")
    run = service.create(
        ValidationCreateRequest(
            kind="synthetic_official_algorithm_reference",
            algorithm_id="psd",
            algorithm_version="offline-spectral-v3",
            dataset_identity={"kind": "analytic_synthetic", "sha256": hashlib.sha256(raw.tobytes()).hexdigest()},
            config_sha256=config_hash,
            tolerances={"rtol": RTOL, "atol": ATOL_PSD},
            expected=expected.reshape(-1).tolist(),
            actual=actual.reshape(-1).tolist(),
        ),
        evidence={
            "scope": "engineering_validation_only_not_clinical_validation",
            "reference_implementation": REFERENCE_ID,
            "environment": {"numpy": NUMPY_VERSION, "scipy": SCIPY_VERSION},
            "artifact": {"path": artifact_path.name, "sha256": checksum, "shape": list(expected.shape)},
            "comparison": {"max_absolute_error": error, "max_error_coordinate": list(coordinate)},
            "frequency_bins_hz": [float(freqs[0]), float(freqs[-1]), int(len(freqs))],
        },
    )
    assert run.passed
    assert run.max_absolute_error == pytest.approx(error)
    assert run.evidence["artifact"]["sha256"] == checksum
    assert service.get(run.validation_id).validation_id == run.validation_id
    with np.load(artifact_path, allow_pickle=False) as data:
        np.testing.assert_array_equal(data["frequencies_hz"], freqs)
        np.testing.assert_allclose(data["expected_psd"], data["actual_psd"], rtol=RTOL, atol=ATOL_PSD)
