from __future__ import annotations

import numpy as np
import pytest

from app.algorithms.tnk import (
    CompositeMeditationModule, FaaModule, TnkSpectralWindow, describe_arrays,
    build_stateful_modules, build_stateless_modules,
)
from app.algorithms.tnk.segment import FaaConfig
from app.algorithms.tnk.stateful import (
    BrainbeatModule, BrainbeatFlatModule, FatigueModule, IapfModule,
    RhythmStabilityModule, StatefulConfig,
)
from app.algorithms.tnk.stateless import AperiodicConfig, HaiConfig, RelaxationConfig, RbpConfig, SpatialConfig
from app.services.tnk_artifacts import store_tnk_arrays
from app.models.run import RunArtifact


def spectral_window(*, roles=("Fz", "Pz", "Oz", "F3", "F4"), provenance=None, quality="clean"):
    freqs = np.arange(0.0, 50.01, 0.5)
    values = {role: np.ones_like(freqs) for role in roles}
    return TnkSpectralWindow(
        frequencies_hz=freqs, psd_by_role=values, sfreq_hz=100.0,
        start_sample=0, end_sample=400, quality=quality,
        provenance=provenance or {},
    )


def test_stateless_and_stateful_module_ids_are_unique_and_unregistered():
    stateless = build_stateless_modules()
    stateful = build_stateful_modules()
    ids = [module.manifest.algorithm_id for module in (*stateless, *stateful, FaaModule(), CompositeMeditationModule())]
    assert len(ids) == len(set(ids)) == 12
    assert all(identifier.startswith("tnk_") for identifier in ids)


def test_stateless_modules_have_typed_roles_and_structured_failures():
    window = spectral_window(roles=("channel",))
    result = next(module for module in build_stateless_modules() if module.manifest.algorithm_id == "tnk_rbp").compute(
        window, RbpConfig(start_sample=0, end_sample=400, sfreq_hz=100),
    )
    assert result.quality == "clean"
    assert set(result.output_values) == {"delta", "theta", "alpha", "beta"}
    missing = next(module for module in build_stateless_modules() if module.manifest.algorithm_id == "tnk_hai").compute(
        window, HaiConfig(start_sample=0, end_sample=400, sfreq_hz=100),
    )
    assert missing.value is None
    assert missing.failure.code == "TNK_CHANNEL_ROLE_MISSING"


def test_relaxation_and_spatial_modules_expose_quality_and_provenance():
    window = spectral_window(roles=("Pz", "Oz", "Fz"))
    relaxation = next(module for module in build_stateless_modules() if module.manifest.algorithm_id == "tnk_relaxation")
    output = relaxation.compute(window, RelaxationConfig(start_sample=0, end_sample=400, sfreq_hz=100, iapf_hz=10))
    assert output.value is not None
    assert output.evidence["extensions"]["tnk_evidence"]["provenance"] == {}
    spatial = next(module for module in build_stateless_modules() if module.manifest.algorithm_id == "tnk_spatial_distribution")
    assert spatial.compute(window, SpatialConfig(start_sample=0, end_sample=400, sfreq_hz=100, iapf_hz=10)).value is not None


def test_brainbeat_reset_and_warmup_are_deterministic():
    module = BrainbeatModule()
    window = spectral_window(roles=("Fz", "Pz"))
    config = StatefulConfig(start_sample=0, end_sample=400, sfreq_hz=100, warmup_count=3)
    first = module.compute(window, config)
    second = module.compute(window, config)
    third = module.compute(window, config)
    assert first.quality == second.quality == "insufficient_data"
    assert third.quality == "clean"
    module.reset()
    assert module.compute(window, config).quality == "insufficient_data"


def test_iapf_accumulates_valid_candidates_and_does_not_reset_on_failure():
    module = IapfModule()
    config = StatefulConfig(start_sample=0, end_sample=400, sfreq_hz=100)
    window = spectral_window(roles=("Pz", "Oz"))
    peak = np.exp(-0.5 * ((window.frequencies_hz - 10.0) / 0.8) ** 2)
    base = np.maximum(window.frequencies_hz, 1.0) ** -1.5
    window.psd_by_role["Pz"] = window.psd_by_role["Oz"] = base + 5.0 * peak
    assert module.compute(window, config).evidence["extensions"]["tnk_evidence"]["candidate_count"] == 1
    failed = module.compute(spectral_window(roles=("Pz", "Oz")), config)
    assert failed.failure.code == "TNK_CANDIDATE_INVALID"
    assert failed.evidence["extensions"]["tnk_evidence"]["candidate_count"] == 1
    peak11 = np.exp(-0.5 * ((window.frequencies_hz - 11.0) / 0.8) ** 2)
    window.psd_by_role["Pz"] = window.psd_by_role["Oz"] = base + 5.0 * peak11
    module.compute(window, config)
    peak12 = np.exp(-0.5 * ((window.frequencies_hz - 12.0) / 0.8) ** 2)
    window.psd_by_role["Pz"] = window.psd_by_role["Oz"] = base + 5.0 * peak12
    locked = module.compute(window, config)
    assert locked.evidence["extensions"]["tnk_evidence"]["locked"] is True
    assert locked.value == pytest.approx(11.0, abs=0.15)


def test_faa_requires_complete_segment_and_clean_epoch_count():
    module = FaaModule()
    config = FaaConfig(start_sample=0, end_sample=400, sfreq_hz=100)
    incomplete = module.compute(spectral_window(roles=("F3", "F4")), config)
    assert incomplete.failure.code == "TNK_SEGMENT_REQUIRED"
    complete = module.compute(spectral_window(roles=("F3", "F4"), provenance={"segment_complete": True, "clean_epochs": 10}), config)
    assert complete.value == pytest.approx(0.0)


def test_brainbeat_flat_requires_its_own_fit_and_composite_keeps_degraded_evidence():
    flat = BrainbeatFlatModule()
    config = StatefulConfig(start_sample=0, end_sample=400, sfreq_hz=100)
    missing = flat.compute(spectral_window(roles=("Fz", "Pz")), config)
    assert missing.failure.code == "TNK_FIT_INVALID"
    composite = CompositeMeditationModule()
    window = spectral_window(roles=("dependencies",), provenance={
        "spatial_distribution": 30.0, "rhythm_stability": 70.0,
        "spatial_valid": False, "stability_ready": True,
    })
    output = composite.compute(window, config)
    assert output.value == 70.0
    assert output.quality == "degraded"
    assert output.evidence["extensions"]["tnk_evidence"]["weights"]["spatial_distribution"] == 0.0


def test_aperiodic_module_uses_specparam_and_reports_fit_evidence():
    module = next(item for item in build_stateless_modules() if item.manifest.algorithm_id == "tnk_aperiodic")
    window = spectral_window(roles=("Fz", "Pz", "Oz"))
    window.psd_by_role = {
        role: np.maximum(window.frequencies_hz, 1.0) ** -1.5 + 5.0 * np.exp(-0.5 * ((window.frequencies_hz - 10) / 0.8) ** 2)
        for role in ("Fz", "Pz", "Oz")
    }
    output = module.compute(window, AperiodicConfig(start_sample=0, end_sample=400, sfreq_hz=100))
    assert output.quality == "clean"
    assert all(value is not None for value in output.output_values.values())
    assert "fit_quality" in output.evidence["extensions"]["tnk_evidence"]


def test_fatigue_and_stability_keep_independent_channel_state():
    window = spectral_window(roles=("Fz", "Pz", "Oz"))
    config = StatefulConfig(start_sample=0, end_sample=400, sfreq_hz=100, warmup_count=2)
    fatigue = FatigueModule()
    assert fatigue.compute(window, config).quality == "insufficient_data"
    assert fatigue.compute(window, config).quality == "clean"
    assert set(fatigue.compute(window, config).output_values) == {"Fz", "Pz", "Oz"}
    stability = RhythmStabilityModule()
    for _ in range(7):
        partial = stability.compute(window, config)
        assert partial.quality == "partial"
    ready = stability.compute(window, config)
    assert ready.quality == "clean"
    stability.reset()
    assert stability.compute(window, config).quality == "partial"


def test_tnk_artifact_descriptor_is_bounded_and_deterministic():
    arrays = {"freqs_hz": np.array([1.0, 2.0]), "psd": np.array([[3.0, 4.0]])}
    descriptor = describe_arrays(
        arrays, artifact_kind="tnk-reference-spectrum",
        units={"freqs_hz": "Hz", "psd": "V^2/Hz"},
        environment={"python": "3.10.20"},
        source_identity={"reference_id": "tnk-ant-reference-v1"},
    )
    assert descriptor.array_names == ["freqs_hz", "psd"]
    assert descriptor.shapes == {"freqs_hz": [2], "psd": [1, 2]}
    assert len(descriptor.arrays_sha256) == 64
    with pytest.raises(ValueError):
        describe_arrays({"psd": np.array([np.inf])}, artifact_kind="bad", units={"psd": "V^2/Hz"}, environment={}, source_identity={})


def test_tnk_arrays_use_existing_artifact_store_boundary():
    class FakeStore:
        def write_npz(self, **kwargs):
            assert kwargs["run_id"] == "run-1"
            assert kwargs["kind"] == "tnk-spectrum"
            assert kwargs["unit"] == "mixed"
            return RunArtifact(
                artifact_id="artifact-1", run_id=kwargs["run_id"], kind=kwargs["kind"],
                relative_path="run-1/artifact-1.npz", media_type="application/x-npz",
                byte_size=12, sha256="a" * 64, unit=kwargs["unit"],
                shape={name: list(np.asarray(value).shape) for name, value in kwargs["arrays"].items()},
                created_at="2026-01-01T00:00:00Z",
            )
    stored = store_tnk_arrays(
        FakeStore(), run_id="run-1", artifact_kind="tnk-spectrum",
        arrays={"freqs": np.array([1.0, 2.0]), "psd": np.array([3.0, 4.0])},
        units={"freqs": "Hz", "psd": "V^2/Hz"}, environment={"python": "3.10.20"},
        source_identity={"reference_id": "tnk-ant-reference-v1"},
    )
    assert stored.artifact.sha256 == "a" * 64
    assert stored.descriptor.source_identity["reference_id"] == "tnk-ant-reference-v1"
