"""Reject malformed module outputs before serializing/persisting current Runs."""

import numpy as np
import pytest

from app.algorithm_runtime.contracts import (
    AlgorithmResult, AlgorithmSeriesResult, AlgorithmStructuredResult,
)
from app.algorithm_runtime.errors import InvalidAlgorithmResultError
from app.algorithm_runtime.result_validation import validate_algorithm_result
from app.algorithms.iapf.manifest import MANIFEST as IAPF
from app.algorithms.psd.manifest import MANIFEST as PSD
from app.algorithms.rbp.manifest import MANIFEST as RBP
from app.services.run_result_serialization import _serialize_structured_result


def scalar():
    return AlgorithmResult(value=10.0, unit="Hz", channel="Fz",
                           requested_range={"start_s": 0, "end_s": 10}, quality="clean")


def series():
    return AlgorithmSeriesResult(values=[10.0], time_s=[5.0], unit="Hz", channel="Fz",
                                 windows=[{"start_s": 0, "end_s": 5}], quality=["clean"],
                                 failures=[None], states=["Complete"], point_evidence=[{}])


def structured():
    return AlgorithmStructuredResult(
        output_kind="frequency_series", channel_order=["Fz"],
        axes={"frequency_hz": np.array([8.0, 9.0, 10.0])}, axis_units={"frequency_hz": "Hz"},
        arrays={"psd": np.ones(3) * 1e-12, "band_share": np.ones(5) / 5},
        array_units={"psd": "V^2/Hz", "band_share": "ratio"},
        requested_range={"start_s": 0, "end_s": 10}, quality="clean",
    )


@pytest.mark.parametrize("mutate", [
    lambda result: setattr(result, "unit", "uV"),
    lambda result: setattr(result, "channel", "Pz"),
    lambda result: setattr(result, "value", float("inf")),
    lambda result: setattr(result, "quality", "invented"),
    lambda result: setattr(result, "quality", "unavailable"),
    lambda result: setattr(result, "evidence", {"extensions": {"algorithm_id": {}}}),
])
def test_scalar_rejects_invalid_unit_role_value_quality_or_evidence(mutate):
    result = scalar()
    mutate(result)
    with pytest.raises(InvalidAlgorithmResultError):
        validate_algorithm_result(result, IAPF, "static", ["Fz"])


@pytest.mark.parametrize("field", ["time_s", "windows", "quality", "failures", "states", "point_evidence"])
def test_series_rejects_truncated_parallel_fields(field):
    result = series()
    setattr(result, field, [])
    with pytest.raises(InvalidAlgorithmResultError, match="count"):
        validate_algorithm_result(result, IAPF, "dynamic", ["Fz"])


@pytest.mark.parametrize("mutate", [
    lambda result: result.arrays.update(psd=np.ones(2)),
    lambda result: result.arrays.update(band_share=np.ones(4)),
    lambda result: result.axes.update(frequency_hz=np.array([10.0, 9.0, 8.0])),
    lambda result: result.array_units.update(psd="uV^2/Hz"),
    lambda result: setattr(result, "channel_order", ["Pz"]),
    lambda result: setattr(result, "output_kind", "time_frequency"),
])
def test_structured_rejects_mismatched_axes_arrays_units_kind_and_roles(mutate):
    result = structured()
    mutate(result)
    with pytest.raises(InvalidAlgorithmResultError):
        validate_algorithm_result(result, PSD, "static", ["Fz"])


def test_multi_field_scalar_requires_every_declared_output():
    result = scalar()
    result.unit = "ratio"
    result.value = None
    with pytest.raises(InvalidAlgorithmResultError, match="all output"):
        validate_algorithm_result(result, RBP, "static", ["Fz"])
    result.output_values = {field.name: 0.2 for field in RBP.output_contract.fields}
    validate_algorithm_result(result, RBP, "static", ["Fz"])


def test_new_algorithm_identity_uses_the_same_validation_and_serialization():
    manifest = PSD.model_copy(deep=True)
    manifest.algorithm_id = "synthetic_new_algorithm"
    result = structured()
    validate_algorithm_result(result, manifest, "static", ["fz"])
    summary, arrays = _serialize_structured_result(result, manifest.algorithm_id, "合成算法")
    assert summary["output"]["id"] == "synthetic_new_algorithm"
    assert summary["arrays"]["psd"] == {"unit": "V^2/Hz", "shape": [3]}
    assert summary["axes"]["frequency_hz"]["array_key"] == "axis_frequency_hz"
    np.testing.assert_array_equal(arrays["axis_frequency_hz"], result.axes["frequency_hz"])


def test_wrong_result_family_is_rejected():
    with pytest.raises(InvalidAlgorithmResultError, match="declared"):
        validate_algorithm_result(scalar(), PSD, "static", ["Fz"])


@pytest.mark.parametrize("queued", [False, True], ids=["synchronous", "queued"])
def test_invalid_module_output_persists_its_error_without_an_artifact(tmp_path, monkeypatch, queued):
    from app.models.run import RunStatus
    from app.services.run_queue import PersistentRunQueue
    from test_official_algorithm_runs import _request, _service

    service, recording_id = _service(tmp_path)
    queue = PersistentRunQueue(service.recordings, service.repository.database_path, tmp_path / "artifacts") if queued else None
    executor_service = queue.base if queue else service
    module = executor_service.algorithm_runtime_registry.get("iapf")
    malformed = scalar()
    malformed.unit = "uV"
    monkeypatch.setattr(module, "execute_static", lambda *_args, **_kwargs: malformed)
    request = _request(recording_id, "iapf")
    if queue:
        queue.enqueue(request)
        run = queue.process_next()
    else:
        run = service.create(request)
    assert run.status is RunStatus.FAILED
    assert run.error.code == "ALGORITHM_RESULT_INVALID"
    assert run.error.stage == "analysis"
    assert run.result_summary is None
    assert executor_service.list_artifacts(run.run_id) == []
