from dataclasses import replace
from concurrent.futures import ThreadPoolExecutor
from threading import Event

import numpy as np
import pytest

from app.services.analysis_preprocess_cache import (
    AnalysisPreprocessCache,
    PreprocessedRecording,
)
from app.services.prepared_source_contract import PreparedSourceContract
from app.services.ica_review_cache import IcaReviewCache


def source_contract(identity="source-1"):
    return PreparedSourceContract(
        schema_version="prepared-source-v1", identity=identity, status="ready",
        sample_rate_hz=200.0, prepared_start_sample=0, prepared_end_sample=4000,
        valid_start_sample=0, valid_end_sample=4000,
        channel_names=("F3",), channel_units=("V",),
        preprocessing_manifest=identity, numerical_tolerance=1e-12,
        numerical_tolerance_evidence="independent PSD comparison; rtol=1e-12, atol=1e-24 V^2/Hz",
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


def test_published_source_samples_are_read_only():
    cache = AnalysisPreprocessCache()
    source = PreprocessedRecording(np.ones((4, 1)), 200.0, ("F3",))
    cache.put("recording", "identity", source)
    cached = cache.get("recording", "identity")
    assert not cached.data.flags.writeable
    with pytest.raises(ValueError):
        cached.data[0, 0] = 9.0


def test_invalidation_detaches_inflight_source_and_prevents_old_writeback():
    cache = AnalysisPreprocessCache()
    started, release = Event(), Event()
    old = PreprocessedRecording(np.ones((4, 1)), 200.0, ("F3",))
    new = PreprocessedRecording(np.full((4, 1), 2.0), 200.0, ("F3",))

    def create_old():
        started.set()
        assert release.wait(5)
        return old

    with ThreadPoolExecutor(max_workers=1) as pool:
        pending = pool.submit(cache.get_or_create, "recording", "identity", create_old)
        try:
            assert started.wait(5)
            cache.clear_recording("recording")
            current = cache.get_or_create("recording", "identity", lambda: new)
            assert current is new
        finally:
            release.set()
        assert pending.result(timeout=5) is old
    assert cache.get("recording", "identity") is new
    assert not old.data.flags.writeable


@pytest.mark.parametrize("invalidate_all", [False, True])
def test_invalidated_ica_fit_cannot_overwrite_new_model(invalidate_all):
    cache = IcaReviewCache()
    old_token = cache.publication_token("recording")
    if invalidate_all:
        cache.clear()
    else:
        cache.invalidate_recording("recording")
    current_token = cache.publication_token("recording")
    model = object()
    cache.put("fit", model, recording_id="recording", token=current_token)
    cache.put("fit", object(), recording_id="recording", token=old_token)
    assert cache.get("fit") is model


@pytest.mark.parametrize("field,value", [
    ("sample_rate_hz", 250.0),
    ("prepared_end_sample", 3999),
    ("channel_names", ("F7",)),
    ("channel_units", ("uV",)),
    ("preprocessing_manifest", "other-filter-contract"),
    ("filter_phase", "causal"),
    ("reference_semantics", "average_reference"),
    ("gap_policy", "reject"),
    ("boundary_policy", "padded"),
    ("implementation_identity", "other-build"),
])
def test_contract_change_is_a_cache_miss(field, value):
    cache = AnalysisPreprocessCache()
    expected = source_contract()
    cache.put("recording", "identity", PreprocessedRecording(
        np.zeros((4000, 1)), 200.0, ("F3",), contract=expected,
    ))
    changes = {field: value}
    if field == "prepared_end_sample":
        changes["valid_end_sample"] = value
    result = cache.lookup("recording", "identity", replace(expected, **changes))
    assert result.value is None
    assert result.miss_reason == "contract_mismatch"
