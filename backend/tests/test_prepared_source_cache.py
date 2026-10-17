import numpy as np

from app.services.analysis_preprocess_cache import (
    AnalysisPreprocessCache,
    PreprocessedRecording,
)
from app.services.prepared_source_contract import PreparedSourceContract


def source_contract(identity="source-1"):
    return PreparedSourceContract(
        schema_version="prepared-source-v1", identity=identity, status="ready",
        sample_rate_hz=200.0, prepared_start_sample=0, prepared_end_sample=4000,
        valid_start_sample=0, valid_end_sample=4000,
        channel_names=("F3",), channel_units=("V",),
        preprocessing_manifest=identity, numerical_tolerance=1e-12,
    )


def test_lookup_reports_structured_contract_miss_reasons():
    cache = AnalysisPreprocessCache()
    assert cache.lookup("recording", "identity").miss_reason == "not_cached"
    cache.put("recording", "identity", PreprocessedRecording(
        np.zeros((4, 1)), 200.0, ("F3",), contract=source_contract(),
    ))

    assert cache.lookup("recording", "identity", source_contract("other")).miss_reason == "contract_mismatch"
    assert cache.lookup("recording", "identity", source_contract()).hit


def test_lookup_rejects_unverifiable_legacy_entry_when_contract_is_required():
    cache = AnalysisPreprocessCache()
    cache.put("recording", "identity", PreprocessedRecording(np.zeros((4, 1)), 200.0, ("F3",)))
    result = cache.lookup("recording", "identity", source_contract())
    assert result.value is None
    assert result.miss_reason == "contract_unavailable"
