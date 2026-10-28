from __future__ import annotations

import hashlib
from pathlib import Path

import numpy as np
import pytest
from types import SimpleNamespace

from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithm_runtime.signal_source import SignalSourceData
from app.algorithms.tnk.legacy_biofeedback import CrimsonEngine, ZenliteEngine
from app.algorithms.tnk.legacy_dsp import StreamingResampler, zenlite_relaxation_raw
from app.algorithms.tnk.legacy_models import CrimsonModel, LegacyModelError, ZenliteModel
from app.algorithms.tnk.legacy_runtime import TnkLegacyStreamBundle
from app.algorithms.tnk.runtime import TnkRuntimeConfig, build_tnk_child_adapters


ASSETS = Path(__file__).parents[1] / "app" / "algorithms" / "tnk" / "assets"


def test_legacy_model_assets_keep_frozen_identity():
    assert hashlib.sha256((ASSETS / "cmsn_meditation.tflite").read_bytes()).hexdigest() == CrimsonModel.SHA256
    assert hashlib.sha256((ASSETS / "zenlite-meditation.tflite").read_bytes()).hexdigest() == ZenliteModel.SHA256


def test_legacy_model_runtime_accepts_frozen_input_shapes():
    crimson = CrimsonModel(ASSETS / "cmsn_meditation.tflite")
    zenlite = ZenliteModel(ASSETS / "zenlite-meditation.tflite")
    assert crimson.predict(np.zeros(1250, dtype=np.float32)).shape == (3,)
    assert zenlite.predict(np.zeros(1200, dtype=np.float32)).shape == (2,)


def test_resampler_is_chunk_invariant():
    source = np.sin(2 * np.pi * 10 * np.arange(4000) / 1000).astype(np.float64)
    whole = StreamingResampler(1000, 250).process(source)
    chunked_resampler = StreamingResampler(1000, 250)
    chunked = np.concatenate([chunked_resampler.process(source[:1300]), chunked_resampler.process(source[1300:])])
    np.testing.assert_array_equal(whole, chunked)


def test_zenlite_relaxation_preserves_float32_mapping_and_clip():
    assert zenlite_relaxation_raw({"alpha": 0.9, "beta_low": 0.3, "beta_high": 0.3}) == pytest.approx(100.0)
    assert zenlite_relaxation_raw({"alpha": 0.0, "beta_low": 0.3, "beta_high": 0.3}) is None


def test_legacy_engines_return_structured_warmup_and_unknown_unit_failure():
    with pytest.raises(ValueError, match="单位"):
        CrimsonEngine(1000, input_unit="counts")
    values = np.zeros(1000, dtype=np.float64)
    crimson = CrimsonEngine(1000, model=object())
    result = crimson.run(values, start_s=0.0, end_s=1.0)
    assert result.value is None
    assert result.failure.code == "TNK_WARMUP"
    zenlite = ZenliteEngine(1000, model=object())
    results = zenlite.run(values, start_s=0.0, end_s=1.0)
    assert all(item.failure.code == "TNK_WARMUP" for item in results.values())


def test_model_shape_validation_reports_missing_optional_runtime(tmp_path):
    bad = tmp_path / "bad.tflite"
    bad.write_bytes(b"wrong")
    with pytest.raises(LegacyModelError, match="校验失败"):
        CrimsonModel(bad)


def test_legacy_children_are_internal_time_series_modules():
    children = {item.manifest.algorithm_id: item for item in build_tnk_child_adapters()}
    assert children["tnk_crimson_meditation"].manifest.signal_requirements.representation == "time_series"
    assert children["tnk_zenlite_calmness"].manifest.signal_requirements.preprocessing_scope == "module_defined"
    assert children["tnk_zenlite_spectral_relaxation"].manifest.required_channel_roles == ["Fz"]


def test_legacy_adapter_reads_typed_raw_source_and_returns_provenance():
    sfreq = 1000.0
    signal = 20e-6 * np.sin(2 * np.pi * 10 * np.arange(6000) / sfreq)

    def load_time_series_source(*, start_sample, end_sample, **_):
        return SignalSourceData(
            values_v=signal[start_sample:end_sample, None].astype(np.float64),
            role_to_output={"Fz": "Fz"}, channel_order=("Fz",), sfreq_hz=sfreq,
            start_sample=start_sample, end_sample=end_sample, unit="V",
            missing_sample_policy="reject", montage_id="m", montage_fingerprint="f",
            preprocessing_scope="module_defined", preprocessing={"low_hz": None, "high_hz": None, "notch_hz": None},
            provenance={"source": "test"},
        )

    payload = SimpleNamespace(load_time_series_source=load_time_series_source)
    inputs = AlgorithmInputs(recording_id="r", channel="Fz", sfreq_hz=sfreq, duration_s=6.0, payload=payload)
    adapter = next(item for item in build_tnk_child_adapters() if item.manifest.algorithm_id == "tnk_zenlite_spectral_relaxation")
    config = TnkRuntimeConfig(channel="Fz", mode="static", start_s=0.0, end_s=6.0)
    result = adapter.compute_window(inputs, config, 0.0, 6.0)
    assert result.actual_range == {"start_s": 0.0, "end_s": 6.0}
    assert result.evidence["extensions"]["tnk_evidence"]["source_provenance"] == {"source": "test"}


def test_legacy_engines_are_chunk_invariant_after_warmup():
    class CrimsonFixture:
        def predict(self, _values):
            return np.asarray([0.2, 0.3, 0.4], dtype=np.float32)

    class ZenliteFixture:
        def predict(self, _values):
            return np.asarray([0.2, 0.3], dtype=np.float32)

    values = 20e-6 * np.sin(2 * np.pi * 10 * np.arange(5000) / 1000)
    crimson_whole = CrimsonEngine(1000, model=CrimsonFixture()).run(values, start_s=0, end_s=5)
    crimson_stream = CrimsonEngine(1000, model=CrimsonFixture())
    crimson_stream.run(values[:4000], start_s=0, end_s=4)
    crimson_chunked = crimson_stream.process_chunk(values[4000:], start_s=4, end_s=5)
    assert crimson_chunked.value == pytest.approx(crimson_whole.value)

    zenlite_whole = ZenliteEngine(1000, model=ZenliteFixture()).run(values, start_s=0, end_s=5)
    zenlite_stream = ZenliteEngine(1000, model=ZenliteFixture())
    zenlite_stream.run(values[:4000], start_s=0, end_s=4)
    zenlite_chunked = zenlite_stream.process_chunk(values[4000:], start_s=4, end_s=5)
    for metric in ZenliteEngine.metric_ids:
        assert zenlite_chunked[metric].value == pytest.approx(zenlite_whole[metric].value)


def test_legacy_stream_bundle_reads_one_shared_fz_chunk_for_both_preprocessing_rates():
    calls = []
    values = 20e-6 * np.sin(2 * np.pi * 10 * np.arange(6000) / 1000)

    def load_time_series_source(*, start_sample, end_sample, **_):
        calls.append((start_sample, end_sample))
        return SignalSourceData(
            values_v=values[start_sample:end_sample, None].astype(np.float64),
            role_to_output={"Fz": "Fz"}, channel_order=("Fz",), sfreq_hz=1000,
            start_sample=start_sample, end_sample=end_sample, unit="V",
            missing_sample_policy="reject", montage_id="m", montage_fingerprint="f",
            preprocessing_scope="module_defined", preprocessing={}, provenance={"source": "test"},
        )

    payload = SimpleNamespace(load_time_series_source=load_time_series_source)
    inputs = AlgorithmInputs(recording_id="r", channel="Fz", sfreq_hz=1000, duration_s=6, payload=payload)
    bundle = TnkLegacyStreamBundle()
    bundle.advance(inputs, channel="Fz", end_sample=5000, start_s=0, end_s=5)
    bundle.advance(inputs, channel="Fz", end_sample=6000, start_s=1, end_s=6)
    assert calls == [(0, 5000), (5000, 6000)]

