import math

import pytest

from app.core.provenance import build_cache_key, canonical_json, sha256_json


def test_canonical_json_is_order_independent_but_preserves_array_order():
    assert canonical_json({"b": 2, "a": [1, 2]}) == canonical_json({"a": [1, 2], "b": 2})
    assert sha256_json({"a": [1, 2]}) != sha256_json({"a": [2, 1]})
    with pytest.raises(ValueError):
        canonical_json({"not_finite": math.nan})


def test_cache_key_changes_for_each_required_identity_component():
    base = {
        "source_sha256": "source",
        "definition_sha256": "definition",
        "config_sha256": "config",
        "implementation_build": "build",
        "actual_range": {"start_s": 10.0, "end_s": 40.0},
    }
    expected = build_cache_key(**base)
    for key, changed in (
        ("source_sha256", "other-source"),
        ("definition_sha256", "other-definition"),
        ("config_sha256", "other-config"),
        ("implementation_build", "other-build"),
        ("actual_range", {"start_s": 11.0, "end_s": 40.0}),
    ):
        assert build_cache_key(**{**base, key: changed}) != expected
