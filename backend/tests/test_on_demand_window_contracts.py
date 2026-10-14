import pytest
from pydantic import ValidationError

from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity,
    DynamicWindowIdentity,
    DynamicWindowPlan,
    DynamicWindowRequest,
    DynamicWindowResult,
    DynamicWindowResponse,
    window_identity_key,
)


def _identity() -> DynamicWindowIdentity:
    return DynamicWindowIdentity(
        recording_id="recording",
        source_sha256="source-hash",
        algorithm_id="psd",
        definition_id="definition",
        definition_version="1",
        scientific_version="psd-v1",
        implementation_identity="psd-runner",
        configuration_sha256="config-hash",
        window_policy_version="policy-v1",
        start_sample=500,
        end_sample=1000,
    )


def _session_identity() -> DynamicSessionIdentity:
    values = _identity().model_dump(exclude={"start_sample", "end_sample", "generation"})
    return DynamicSessionIdentity(**values)


def test_window_identity_is_half_open_and_immutable():
    identity = _identity()
    assert identity.end_sample - identity.start_sample == 500
    with pytest.raises(ValidationError):
        identity.end_sample = 900


def test_window_identity_key_is_stable_and_contains_no_display_state():
    identity = _identity()
    same = _identity()
    assert window_identity_key(identity) == window_identity_key(same)
    assert window_identity_key(identity).startswith("dynamic-window-v1:")


@pytest.mark.parametrize(
    "payload",
    [
        {"start_sample": 10, "end_sample": 10},
        {"start_sample": 11, "end_sample": 10},
    ],
)
def test_window_identity_rejects_empty_or_reverse_range(payload):
    with pytest.raises(ValidationError):
        DynamicWindowIdentity(
            recording_id="recording", source_sha256="source", algorithm_id="psd",
            definition_id="definition", definition_version="1", scientific_version="v1",
            implementation_identity="runner", configuration_sha256="config",
            window_policy_version="policy-v1", **payload,
        )


def test_request_forbids_client_supplied_window_boundaries():
    with pytest.raises(ValidationError):
        DynamicWindowRequest(
            recording_id="recording", algorithm_id="psd", cursor_sample=100,
            start_sample=0, end_sample=500,
        )


def test_request_rejects_fractional_cursor_sample():
    with pytest.raises(ValidationError):
        DynamicWindowRequest(recording_id="recording", algorithm_id="psd", cursor_sample=1.5, config={})


def test_unavailable_plan_requires_reason():
    with pytest.raises(ValidationError):
        DynamicWindowPlan(
            session_identity=_session_identity(),
            cursor_sample=100, start_sample=0, end_sample=100, start_s=1, end_s=2,
            state="insufficient_data",
        )


def test_failed_result_requires_structured_failure_and_matching_units():
    with pytest.raises(ValidationError):
        DynamicWindowResult(
            identity=_identity(), state="failed", output_kind="frequency_series",
            axes={"frequency": [1, 2]}, axis_units={}, arrays={"psd": [1, 2]},
            array_units={"psd": "V^2/Hz"}, quality="failed",
        )
    with pytest.raises(ValidationError):
        DynamicWindowResult(
            identity=_identity(), state="failed", output_kind="frequency_series",
            axes={"frequency": [1, 2]}, axis_units={"frequency": "Hz"},
            arrays={"psd": [1, 2]}, array_units={"psd": "V^2/Hz"}, quality="failed",
        )


def test_response_requires_ready_payload():
    identity = _identity()
    session_identity = _session_identity()
    plan = DynamicWindowPlan(session_identity=session_identity, cursor_sample=1000,
                             start_sample=500, end_sample=1000, start_s=1, end_s=2)
    with pytest.raises(ValidationError):
        DynamicWindowResponse(
            session_id="session", window_id="window", request_id="request",
            identity=identity, state="ready", plan=plan,
        )


def test_ready_window_requires_finite_payload_and_provenance():
    identity = _identity()
    with pytest.raises(ValidationError, match="provenance"):
        DynamicWindowResult(
            identity=identity, state="ready", output_kind="frequency_series",
            axes={"frequency": [1, 2]}, axis_units={"frequency": "Hz"},
            arrays={"psd": [1, 2]}, array_units={"psd": "V^2/Hz"}, quality="clean",
        )
    with pytest.raises(ValidationError, match="non-finite"):
        DynamicWindowResult(
            identity=identity, state="ready", output_kind="frequency_series",
            axes={"frequency": [1, 2]}, axis_units={"frequency": "Hz"},
            arrays={"psd": [1, float("nan")]}, array_units={"psd": "V^2/Hz"}, quality="clean",
            provenance={"scope": "whole_recording"},
        )
