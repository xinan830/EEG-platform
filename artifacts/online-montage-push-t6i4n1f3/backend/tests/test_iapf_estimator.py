from __future__ import annotations

import json

import numpy as np

from app.algorithms.iapf import compute as iapf_compute
from app.algorithms.iapf.compute import compute_iapf
from app.algorithms.iapf.official import estimate_iapf
from app.scientific.primitives.spectral import SpectralEstimate


def _spectrum(alpha_excess: np.ndarray | None = None) -> SpectralEstimate:
    freqs = np.arange(1.0, 30.001, 0.25)
    baseline = 1e-10 / freqs**1.2
    psd = baseline if alpha_excess is None else baseline + alpha_excess
    return SpectralEstimate(freqs, psd[None, :], 1.0, 9, 9, None)


def test_prominent_local_alpha_peak_is_selected_as_paf() -> None:
    base = 1e-10 / np.arange(1.0, 30.001, 0.25) ** 1.2
    freqs = np.arange(1.0, 30.001, 0.25)
    excess = base * np.exp(-0.5 * ((freqs - 10.25) / 0.6) ** 2)
    excess[(freqs < 7.0) | (freqs > 13.0)] = 0.0

    estimate = estimate_iapf(_spectrum(excess))

    assert estimate.source == "peak"
    assert estimate.value == 10.25
    assert estimate.peak_width_bins is not None and estimate.peak_width_bins >= 1.2
    assert estimate.peak_prominence_ratio is not None and estimate.peak_prominence_ratio >= 0.20


def test_aperiodic_only_spectrum_does_not_produce_a_numeric_iapf() -> None:
    estimate = estimate_iapf(_spectrum())

    assert estimate.value is None
    assert estimate.gate_failed in {"no_peak_no_cog", "no_supported_alpha_peak"}


def test_small_random_spectral_ripples_do_not_produce_a_numeric_iapf() -> None:
    rng = np.random.default_rng(2026)
    freqs = np.arange(1.0, 30.001, 0.25)
    baseline = 1e-10 / freqs**1.2
    rippled = baseline * np.exp(rng.normal(0.0, 0.03, len(freqs)))
    alpha_excess = rippled - baseline

    estimate = estimate_iapf(_spectrum(alpha_excess))

    assert estimate.value is None


def test_single_bin_spike_is_not_accepted_as_peak_or_cog() -> None:
    freqs = np.arange(1.0, 30.001, 0.25)
    baseline = 1e-10 / freqs**1.2
    excess = np.zeros_like(freqs)
    excess[np.flatnonzero(freqs == 10.0)[0]] = baseline[np.flatnonzero(freqs == 10.0)[0]] * 20.0

    estimate = estimate_iapf(_spectrum(excess))

    assert estimate.value is None
    assert estimate.peak_width_bins is None
    assert estimate.cog_effective_bins is not None and estimate.cog_effective_bins < 3.0


def test_broad_alpha_excess_can_use_cog_when_no_local_peak_passes() -> None:
    freqs = np.arange(1.0, 30.001, 0.25)
    baseline = 1e-10 / freqs**1.2
    excess = np.zeros_like(freqs)
    alpha = (freqs >= 7.0) & (freqs <= 13.0)
    excess[alpha] = baseline[alpha] * np.linspace(0.25, 0.45, np.count_nonzero(alpha))

    estimate = estimate_iapf(_spectrum(excess))

    assert estimate.source == "cog"
    assert estimate.value is not None and 9.5 < estimate.value < 10.5
    assert estimate.cog_excess_ratio is not None and estimate.cog_excess_ratio >= 0.20
    assert estimate.cog_effective_bins is not None and estimate.cog_effective_bins >= 3.0


def test_edge_excess_does_not_become_a_local_peak() -> None:
    freqs = np.arange(1.0, 30.001, 0.25)
    baseline = 1e-10 / freqs**1.2
    excess = baseline * np.exp(-0.5 * ((freqs - 7.0) / 0.6) ** 2)
    excess[(freqs < 7.0) | (freqs > 13.0)] = 0.0

    estimate = estimate_iapf(_spectrum(excess))

    assert estimate.source != "peak"
    assert estimate.peak_hz is None


def test_compute_logs_segment_estimates_without_spectral_arrays(monkeypatch) -> None:
    records = []
    monkeypatch.setattr(iapf_compute._LOGGER, "info", lambda message, *args: records.append(message % args))
    freqs = np.arange(1.0, 30.001, 0.25)
    baseline = 1e-10 / freqs**1.2
    spectra = []
    for center in (9.25, 9.5):
        excess = baseline * np.exp(-0.5 * ((freqs - center) / 0.6) ** 2)
        excess[(freqs < 7.0) | (freqs > 13.0)] = 0.0
        spectra.append(baseline + excess)
    spectrum = _spectrum(np.mean(spectra, axis=0) - baseline)
    spectrum = SpectralEstimate(
        spectrum.freqs, spectrum.psd, 1.0, 9, 9, None,
        welch_segments_psd=np.asarray(spectra)[:, None, :],
        welch_segment_offsets_s=(0.0, 2.0),
    )

    compute_iapf(
        spectrum, channel="O1-REF", recording_id="rec-1",
        requested_range={"start_s": 58.0, "end_s": 78.0},
    )

    record = next(item for item in records if "iapf_welch_segment_diagnostics" in item)
    assert "recording_id=rec-1" in record
    assert "window_start_s=58.000" in record
    diagnostics = json.loads(record.split("segments=", 1)[1])
    assert [item["start_s"] for item in diagnostics] == [58.0, 60.0]
    assert [item["peak_hz"] for item in diagnostics] == [9.25, 9.5]
    assert "psd" not in diagnostics[0]
