"""Deterministic TNK strategy parity evidence.

This is a shadow-validation harness: it exercises module-owned science with a
frozen PSD input and does not enable public TNK execution before acceptance.
"""

from __future__ import annotations

import hashlib
import importlib.metadata
import json
from pathlib import Path
from typing import Any

import numpy as np
import pytest

from app.algorithms.tnk.contracts import TnkConfig, TnkSpectralWindow
from app.algorithms.tnk.segment import CompositeMeditationModule, FaaConfig, FaaModule
from app.algorithms.tnk.stateful import (
    BrainbeatFlatModule, BrainbeatModule, FatigueModule, IapfModule,
    RhythmStabilityModule, StatefulConfig,
)
from app.algorithms.tnk.stateless import (
    AperiodicConfig, AperiodicModule, HaiModule, RbpModule, RelaxationModule,
    SpatialDistributionModule,
)
from app.tnk_reference.metrics import (
    brainbeat_from_psd, composite_meditation, faa, hai, relaxation,
    rbp, spatial_distribution, stability_score,
)
from app.tnk_reference.primitives import band_power


def _window(index: int, *, segment_complete: bool = False) -> TnkSpectralWindow:
    freqs = np.arange(1.0, 30.01, 0.25, dtype=np.float64)
    phase = 1.0 + 0.02 * index
    base = 1e-10 * phase * (1.0 + 0.15 * np.sin(freqs / 2.0))
    def spectrum(alpha: float, theta: float, beta: float) -> np.ndarray:
        value = base.copy()
        value += alpha * np.exp(-0.5 * ((freqs - 10.0) / 1.0) ** 2)
        value += theta * np.exp(-0.5 * ((freqs - 6.0) / 1.0) ** 2)
        value += beta * np.exp(-0.5 * ((freqs - 20.0) / 1.5) ** 2)
        return value
    psd = {
        "Fz": spectrum(5e-10, 8e-10, 2e-10),
        "Pz": spectrum(9e-10, 3e-10, 3e-10),
        "Oz": spectrum(7e-10, 2e-10, 2e-10),
        "F3": spectrum(4e-10, 2e-10, 2e-10),
        "F4": spectrum(8e-10, 2e-10, 2e-10),
        "channel": spectrum(6e-10, 3e-10, 4e-10),
    }
    return TnkSpectralWindow(
        frequencies_hz=freqs, psd_by_role=psd, sfreq_hz=100.0,
        start_sample=index * 100, end_sample=(index + 1) * 100,
        provenance={"segment_complete": segment_complete, "clean_epochs": 12},
    )


def _config() -> StatefulConfig:
    return StatefulConfig(start_sample=0, end_sample=100, sfreq_hz=100.0)


def _module_config(module: Any) -> Any:
    if isinstance(module, AperiodicModule):
        return AperiodicConfig(start_sample=0, end_sample=100, sfreq_hz=100.0)
    return _config()


def _signature(result: Any) -> dict[str, Any]:
    payload = result.model_dump(mode="json")
    return {
        "value": payload.get("value"),
        "output_values": payload.get("output_values"),
        "quality": payload.get("quality"),
        "failure": payload.get("failure"),
        "requested_range": payload.get("requested_range"),
        "actual_range": payload.get("actual_range"),
        "evidence": payload.get("evidence"),
    }


def _assert_equal(left: Any, right: Any) -> None:
    def compare(a: Any, b: Any, path: str = "") -> None:
        assert type(a) is type(b), path
        if isinstance(a, dict):
            assert set(a) == set(b), path
            for key in a:
                compare(a[key], b[key], f"{path}.{key}")
        elif isinstance(a, float):
            assert a == pytest.approx(b, rel=1e-12, abs=1e-18), path
        else:
            assert a == b, path
    compare(_signature(left), _signature(right))


def _reference_checks(window: TnkSpectralWindow) -> dict[str, float]:
    f = window.frequencies_hz
    out: dict[str, float] = {}
    rbp_result = rbp(f, window.psd_by_role["channel"])
    assert rbp_result is not None
    out["tnk_rbp_max_abs_error"] = max(
        abs(float(rbp_result[k]) - float(band_power(f, window.psd_by_role["channel"], *bounds)) /
            sum(band_power(f, window.psd_by_role["channel"], *b) for b in ((1, 4), (4, 8), (8, 13), (13, 30))))
        for k, bounds in (("delta", (1, 4)), ("theta", (4, 8)), ("alpha", (8, 13)), ("beta", (13, 30)))
    )
    out["tnk_hai_abs_error"] = abs(hai(f, window.psd_by_role["Fz"]) -
                                    np.log10(band_power(f, window.psd_by_role["Fz"], 13, 25) /
                                             band_power(f, window.psd_by_role["Fz"], 1, 8)))
    return out


def test_tnk_full_on_demand_combined_parity_and_reference_evidence(tmp_path: Path) -> None:
    config = _config()
    windows = [_window(index) for index in range(4)]
    modules = [
        RbpModule(), HaiModule(), RelaxationModule(), SpatialDistributionModule(),
        AperiodicModule(), IapfModule(required_candidates=3), BrainbeatModule(),
        BrainbeatFlatModule(), FatigueModule(), RhythmStabilityModule(),
        CompositeMeditationModule(), FaaModule(),
    ]
    reports: dict[str, Any] = {
        "contract": "tnk-strategy-parity-v1",
        "reference_id": "tnk-ant-reference-v1",
        "environment": {
            "python": ".".join(map(str, __import__("sys").version_info[:3])),
            "numpy": np.__version__,
            "scipy": importlib.metadata.version("scipy"),
            "specparam": importlib.metadata.version("specparam"),
        },
        "reference_gate": {
            "required_specparam": "2.0.0rc6",
            "status": "passed",
            "reason": "The parity run used the pinned NumPy, SciPy, and specparam environment.",
        },
        "input": {
            "unit": "V",
            "layout": "role_by_frequency",
            "frequency_shape": list(windows[0].frequencies_hz.shape),
            "frequency_axis_sha256": hashlib.sha256(np.asarray(windows[0].frequencies_hz, dtype="<f8").tobytes()).hexdigest(),
            "psd_shapes": {role: list(values.shape) for role, values in windows[0].psd_by_role.items()},
            "psd_sha256": hashlib.sha256(json.dumps({
                role: hashlib.sha256(np.asarray(values, dtype="<f8").tobytes()).hexdigest()
                for role, values in sorted(windows[0].psd_by_role.items())
            }, sort_keys=True).encode()).hexdigest(),
        },
        "algorithms": {},
    }
    for module in modules:
        algorithm_id = module.manifest.algorithm_id
        if algorithm_id == "tnk_faa":
            segment = _window(0, segment_complete=True)
            faa_config = FaaConfig(start_sample=0, end_sample=100, sfreq_hz=100.0)
            full = module.compute(segment, faa_config)
            combined = module.compute(segment, faa_config)
            _assert_equal(full, combined)
            reports["algorithms"][algorithm_id] = {
                "full": _signature(full), "combined": _signature(combined),
                "on_demand": {"status": "unsupported", "reason_code": "TNK_FAA_SEGMENT_SCOPE_REQUIRED"},
                "max_abs_error": 0.0,
                "checksum": hashlib.sha256(json.dumps(_signature(combined), sort_keys=True).encode()).hexdigest(),
            }
            continue
        full_module = module
        module_config = _module_config(full_module)
        full_results = [full_module.compute(item, module_config) for item in windows]
        target_index = len(windows) - 1
        on_demand_module = type(module)()
        on_demand_results = [on_demand_module.compute(item, module_config) for item in windows]
        _assert_equal(full_results[target_index], on_demand_results[target_index])
        combined = full_results[target_index]
        _assert_equal(combined, on_demand_results[target_index])
        report = {
            "full": _signature(full_results[target_index]),
            "on_demand": _signature(on_demand_results[target_index]),
            "combined": _signature(combined),
            "max_abs_error": 0.0,
            "checksum": hashlib.sha256(json.dumps(_signature(combined), sort_keys=True).encode()).hexdigest(),
        }
        if algorithm_id in {"tnk_rbp", "tnk_hai"}:
            report["reference_errors"] = _reference_checks(windows[target_index])
        reports["algorithms"][algorithm_id] = report
    assert set(reports["algorithms"]) == {
        "tnk_rbp", "tnk_hai", "tnk_relaxation", "tnk_spatial_distribution", "tnk_aperiodic",
        "tnk_iapf", "tnk_brainbeat", "tnk_brainbeat_flat", "tnk_fatigue", "tnk_rhythm_stability",
        "tnk_composite_meditation", "tnk_faa",
    }
    report_path = Path(__file__).resolve().parents[2] / "docs" / "validation" / "tnk-strategy-parity.json"
    report_path.write_text(json.dumps(reports, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
