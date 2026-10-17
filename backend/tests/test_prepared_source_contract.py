import pytest

from app.services.prepared_source_contract import PreparedSourceContract


def contract(**changes):
    values = {
        "schema_version": "prepared-source-v1",
        "identity": "source-1",
        "status": "ready",
        "sample_rate_hz": 200.0,
        "prepared_start_sample": 0,
        "prepared_end_sample": 4000,
        "valid_start_sample": 0,
        "valid_end_sample": 4000,
        "channel_names": ("F3", "F7"),
        "channel_units": ("V", "V"),
        "preprocessing_manifest": "manifest-1",
        "numerical_tolerance": 1e-12,
    }
    values.update(changes)
    return PreparedSourceContract(**values)


def test_prepared_source_contract_requires_exact_scientific_identity():
    expected = contract()
    assert expected.matches(contract())
    assert not expected.matches(contract(identity="source-2"))
    assert not expected.matches(contract(channel_names=("F7", "F3")))
    assert not expected.matches(contract(preprocessing_manifest="manifest-2"))
    assert not expected.matches(contract(filter_phase="causal"))
    assert not expected.matches(contract(reference_semantics="average_reference"))
    assert not expected.matches(contract(gap_policy="zero_fill"))
    assert not expected.matches(contract(boundary_policy="pad"))
    assert not expected.matches(contract(implementation_identity="other-build"))


@pytest.mark.parametrize("field,value", [
    ("prepared_end_sample", 0),
    ("valid_end_sample", 5000),
    ("channel_units", ("V",)),
    ("status", "unknown"),
    ("filter_phase", ""),
    ("reference_semantics", ""),
    ("gap_policy", ""),
    ("boundary_policy", ""),
    ("implementation_identity", ""),
])
def test_prepared_source_contract_rejects_invalid_state(field, value):
    with pytest.raises(ValueError):
        contract(**{field: value})
