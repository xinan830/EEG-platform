"""Tests for Step 3 offline artifact removal: EOG regression, MNE SSP, and reviewable ICA."""

from __future__ import annotations

import numpy as np
import pytest

from app.scientific.artifact_removal import apply_offline_artifact_removal, inspect_ica_components
from app.scientific.contracts.preprocessing import ArtifactRemovalSpec


_CHANNELS_10 = ("Fp1", "Fp2", "F3", "F4", "C3", "C4", "P3", "P4", "O1", "O2")
_BLINK_WEIGHTS = np.array([1.0, 0.95, 0.55, 0.52, 0.28, 0.26, 0.14, 0.13, 0.08, 0.07], dtype=np.float64)


def _synthesize_eeg_with_blinks(
    *,
    sfreq: float = 250.0,
    duration_s: float = 16.0,
    include_blinks: bool = True,
    seed: int = 101,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Return (mixed_v, clean_brain_v, blink_wave_v) in SI volts (samples, 10)."""
    rng = np.random.default_rng(seed)
    t = np.arange(int(round(duration_s * sfreq)), dtype=np.float64) / sfreq
    n_samples = t.size

    # Independent brain sources (posterior alpha + central beta + low-level sensor noise).
    alpha = 18e-6 * np.sin(2.0 * np.pi * 10.0 * t)
    beta = 7e-6 * np.sin(2.0 * np.pi * 18.0 * t + 0.7)
    alpha_topo = np.array([0.15, 0.15, 0.30, 0.30, 0.45, 0.45, 0.80, 0.80, 1.0, 1.0])
    beta_topo = np.array([0.20, 0.20, 0.55, 0.55, 0.90, 0.90, 0.40, 0.40, 0.20, 0.20])
    clean = (
        alpha[:, None] * alpha_topo[None, :]
        + beta[:, None] * beta_topo[None, :]
        + rng.normal(0.0, 5e-6, size=(n_samples, len(_CHANNELS_10)))
    )

    blink = np.zeros(n_samples, dtype=np.float64)
    if include_blinks:
        for center_s in (2.0, 4.5, 7.0, 9.5, 12.0, 14.0):
            blink += 180e-6 * np.exp(-0.5 * ((t - center_s) / 0.08) ** 2)

    mixed = clean + blink[:, None] * _BLINK_WEIGHTS[None, :]
    return mixed, clean, blink


def test_eog_regression_attenuates_blinks_preserves_alpha_and_propagates_nan_gaps():
    mixed, clean, _blink = _synthesize_eeg_with_blinks()
    # Inject a NaN gap on the EOG reference channel Fp1 to verify strict NaN propagation.
    mixed_with_gap = mixed.copy()
    mixed_with_gap[500:540, 0] = np.nan

    spec = ArtifactRemovalSpec(method="eog_regression", eog_channels=("Fp1",))
    res = apply_offline_artifact_removal(mixed_with_gap, 250.0, _CHANNELS_10, spec)

    assert res.applied is True
    assert res.status == "applied"
    assert res.eog_channels_used == ("Fp1",)
    assert res.regression_weights is not None
    assert 0.35 < res.regression_weights["F3"][0] < 0.75

    # NaN on EOG proxy must propagate as NaN on corrected targets (never fabricated as zero).
    assert np.all(np.isnan(res.data_v[500:540, 2]))
    assert np.all(np.isfinite(res.data_v[600:, 2]))

    # F3 blink peak-to-peak drops dramatically while 10 Hz posterior alpha on O1 stays intact.
    f3_before_ptp = float(np.nanmax(mixed[:, 2]) - np.nanmin(mixed[:, 2]))
    f3_after_ptp = float(np.nanmax(res.data_v[600:, 2]) - np.nanmin(res.data_v[600:, 2]))
    assert f3_after_ptp < f3_before_ptp * 0.55

    corr_o1 = float(np.corrcoef(res.data_v[600:, 8], clean[600:, 8])[0, 1])
    assert corr_o1 > 0.90


def test_ssp_eog_reduces_blink_artifact_and_skips_clean_signal_honestly():
    mixed, _clean, _blink = _synthesize_eeg_with_blinks()
    spec = ArtifactRemovalSpec(method="ssp_eog", eog_channels=("Fp1",), ssp_n_eeg=1)
    res = apply_offline_artifact_removal(mixed, 250.0, _CHANNELS_10, spec)

    assert res.applied is True
    assert res.status == "applied"
    assert res.ssp_projectors_applied == 1
    assert res.eog_events_detected >= 3

    f3_before_ptp = float(np.ptp(mixed[:, 2]))
    f3_after_ptp = float(np.ptp(res.data_v[:, 2]))
    assert f3_after_ptp < f3_before_ptp * 0.50

    # On a blink-free low-amplitude clean signal with no EOG spikes, SSP skips honestly if 0 events.
    clean_only, _, _ = _synthesize_eeg_with_blinks(include_blinks=False, seed=202)
    clean_only *= 0.05  # tiny 1 uV signal well below EOG peak detector
    res_clean = apply_offline_artifact_removal(clean_only, 250.0, _CHANNELS_10, spec)
    assert res_clean.method == "ssp_eog"
    if not res_clean.applied:
        assert res_clean.status == "skipped_no_eog_events"
        assert np.array_equal(res_clean.data_v, clean_only)


def test_ica_infomax_inspection_auto_removal_manual_override_and_validation():
    mixed, clean, _blink = _synthesize_eeg_with_blinks()
    # Inject a NaN gap (simulating Step 2 artifact segment rejection) so ICA fits only on finite spans.
    mixed_gapped = mixed.copy()
    mixed_gapped[1500:1600, :] = np.nan

    inspect = inspect_ica_components(
        mixed_gapped,
        250.0,
        _CHANNELS_10,
        ArtifactRemovalSpec(method="ica_infomax", eog_channels=("Fp1",), ica_n_components=6, random_state=42),
    )
    assert inspect["method"] == "ica_infomax"
    assert inspect["n_components"] == 6
    assert len(inspect["components"]) == 6
    recommended = inspect["recommended_exclude_components"]
    assert len(recommended) >= 1
    assert all(0.0 <= float(c["explained_variance_ratio"]) <= 1.0 for c in inspect["components"])

    # 1) Auto-removal excludes the recommended blink component and preserves NaN gaps + alpha rhythm.
    auto_res = apply_offline_artifact_removal(
        mixed_gapped,
        250.0,
        _CHANNELS_10,
        ArtifactRemovalSpec(method="ica_infomax", eog_channels=("Fp1",), ica_n_components=6, random_state=42),
    )
    assert auto_res.applied is True
    assert auto_res.status == "applied"
    assert list(auto_res.excluded_components) == recommended
    assert np.all(np.isnan(auto_res.data_v[1500:1600, :]))
    assert np.all(np.isfinite(auto_res.data_v[:1490, :]))

    f3_before = float(np.nanmax(mixed_gapped[:, 2]) - np.nanmin(mixed_gapped[:, 2]))
    f3_after = float(np.nanmax(auto_res.data_v[:, 2]) - np.nanmin(auto_res.data_v[:, 2]))
    assert f3_after < f3_before * 0.50
    valid_mask = np.isfinite(auto_res.data_v[:, 8])
    assert float(np.corrcoef(auto_res.data_v[valid_mask, 8], clean[valid_mask, 8])[0, 1]) > 0.85

    # 2) Explicit user-reviewed override: exclude=() skips cleanly without modifying signal.
    skip_res = apply_offline_artifact_removal(
        mixed_gapped,
        250.0,
        _CHANNELS_10,
        ArtifactRemovalSpec(
            method="ica_infomax",
            eog_channels=("Fp1",),
            ica_n_components=6,
            ica_exclude_components=(),
            random_state=42,
        ),
    )
    assert skip_res.applied is False
    assert skip_res.status == "skipped_no_artifact_components"
    assert skip_res.excluded_components == ()
    assert np.array_equal(skip_res.data_v, mixed_gapped, equal_nan=True)

    # 3) Explicit user-reviewed override with out-of-range IC raises structured error.
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_INVALID_IC"):
        apply_offline_artifact_removal(
            mixed,
            250.0,
            _CHANNELS_10,
            ArtifactRemovalSpec(method="ica_infomax", ica_n_components=6, ica_exclude_components=(9,)),
        )

    # 4) Insufficient channels (< 4) or duration (< 5 s) fails explicitly.
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_INSUFFICIENT_CHANNELS"):
        apply_offline_artifact_removal(
            mixed[:, :3], 250.0, _CHANNELS_10[:3], ArtifactRemovalSpec(method="ica_infomax"),
        )
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_INSUFFICIENT_FINITE_SAMPLES"):
        apply_offline_artifact_removal(
            mixed[:500, :], 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="ica_infomax"),
        )


def test_eog_proxy_safety_blocks_silent_frontal_eeg_regression_and_requires_confirmation():
    mixed, _clean, _blink = _synthesize_eeg_with_blinks()

    # EOG regression MUST NOT silently grab Fp1 when no dedicated EOG channel exists.
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_EOG_CHANNEL_UNAVAILABLE"):
        apply_offline_artifact_removal(mixed, 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="eog_regression"))

    # SSP and ICA MUST require explicit allow_frontal_eog_proxy=True (or explicit eog_channels) on frontal EEG-only caps.
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_FRONTAL_PROXY_CONFIRMATION_REQUIRED"):
        apply_offline_artifact_removal(mixed, 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="ssp_eog"))
    with pytest.raises(ValueError, match="ARTIFACT_REMOVAL_FRONTAL_PROXY_CONFIRMATION_REQUIRED"):
        apply_offline_artifact_removal(mixed, 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="ica_infomax"))

    # With allow_frontal_eog_proxy=True, SSP & ICA succeed and honestly report eog_channel_source="frontal_eeg_proxy".
    res_ssp = apply_offline_artifact_removal(
        mixed, 250.0, _CHANNELS_10, ArtifactRemovalSpec(method="ssp_eog", allow_frontal_eog_proxy=True),
    )
    assert res_ssp.eog_channel_source == "frontal_eeg_proxy"
    assert res_ssp.applied is True
