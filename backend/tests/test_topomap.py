from types import SimpleNamespace

import numpy as np
import pytest

from app.services.topomap import RbpTopomapAdapter, TopomapService, TopomapUnavailable


def _service(values, channels, formulas=None, status="available"):
    run = SimpleNamespace(
        config={"algorithm_id": "rbp"}, recording_id="recording-1",
        result_summary={"structured": {"channel_order": channels}},
        scientific_version="official-rbp-v4", implementation_version="test-build",
    )
    preview = {"recording_id": "recording-1", "status": status, "cursor_s": 5.0,
               "scientific_version": run.scientific_version, "implementation_version": run.implementation_version,
               "window": {"start_sample": 0, "end_sample": 500,
                           "provenance": {"window_start_s": 0.0, "window_end_s": 5.0}},
               "arrays": {band: values for band in ("delta", "theta", "alpha", "beta", "gamma")}}
    queue = SimpleNamespace(
        get=lambda _run_id: run,
        recordings=SimpleNamespace(
            require_recording=lambda _id: SimpleNamespace(id="recording-1", sfreq=100.0),
            select_analysis_montage=lambda recording, _montage: recording,
            analysis_signal_provenance=lambda _recording: {
                "formulas": formulas or [],
                "montage_id": "montage-1", "montage_revision": 2,
                "montage_fingerprint": "fingerprint-1", "snapshot_sha256": "snapshot-1",
                "map_positions": {
                    channel: {"x": x, "y": y, "status": "available", "enabled": True, "source": "template"}
                    for channel, (x, y) in {"Fz": (0.0, -0.8), "Cz": (0.0, 0.0), "Pz": (0.0, 0.8)}.items()
                },
            },
        ),
    )
    result_service = SimpleNamespace(window_preview=lambda *_args, **_kwargs: preview)
    return TopomapService(result_service, queue)


def test_rbp_topomap_uses_snapshot_positions_and_returns_bounded_grid():
    result = _service([0.2, 0.5, 0.8], ["Fz", "Cz", "Pz"]).build_rbp_window("run", cursor_s=5.0)
    assert result.status == "ready"
    assert [band.band_id for band in result.bands] == ["delta", "theta", "alpha", "beta", "gamma"]
    assert result.window_start_s == 0.0 and result.window_end_s == 5.0
    assert result.bands[0].grid is not None and result.bands[0].grid.width == result.bands[0].grid.height == 65
    assert result.bands[0].grid.head_radius == 1.0
    assert all(item.status == "included" for item in result.bands[0].points)
    assert all(value is None or np.isfinite(value) for row in result.bands[0].grid.values for value in row)
    assert result.provenance["montage_fingerprint"] == "fingerprint-1"


def test_topomap_excludes_bipolar_outputs_without_inventing_endpoint():
    service = _service([0.2, 0.5, 0.8], ["Fz-F4", "Cz", "Pz"], formulas=[
        {"name": "Fz-F4", "positive": "Fz", "negative_kind": "specifiedpair"},
    ])
    result = service.build_rbp_window("run", cursor_s=5.0)
    assert result.status == "unavailable"
    assert result.bands[0].points[0].reason == "bipolar_or_derived_output_has_no_spatial_rule"


def test_rbp_adapter_reads_channel_order_from_window_evidence_without_recomputing():
    result = RbpTopomapAdapter().build_input(
        recording_id="recording-1",
        payload={
            "identity": {"scientific_version": "rbp-v2", "implementation_identity": "build"},
            "result": {
                "arrays": {band: [0.1, 0.2, 0.3] for band in ("delta", "theta", "alpha", "beta", "gamma")},
                "array_units": {"alpha": "ratio"},
                "evidence": {"extensions": {"channel_order": {"channels": ["Fz", "Cz", "Pz"]}}},
            },
        },
        window_start_s=5.0, window_end_s=10.0,
    )
    assert result.channel_order == ["Fz", "Cz", "Pz"]
    assert result.values_by_band["alpha"] == [0.1, 0.2, 0.3]
    assert result.window_start_s == 5.0


def test_interpolation_preserves_exact_grid_samples_and_circular_boundary():
    grid = TopomapService(None, None)._interpolate([
        ("left", -1.0, 0.0, 2.0), ("center", 0.0, 0.0, 3.0), ("right", 1.0, 0.0, 4.0),
    ])

    assert grid.values[32][32] == 3.0
    assert grid.values[0][0] is None
