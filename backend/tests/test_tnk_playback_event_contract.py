from datetime import datetime, timezone

import pytest
from pydantic import ValidationError

from app.algorithm_runtime.on_demand import (
    DynamicWindowIdentity, DynamicWindowResult, window_identity_key,
)
from app.algorithms.tnk.playback_delivery import TnkPlaybackEventBuffer
from app.algorithms.tnk.playback_event import TnkPlaybackFrameEvent


def _identity(**overrides) -> DynamicWindowIdentity:
    values = {
        "recording_id": "recording-1",
        "source_sha256": "source-hash",
        "algorithm_id": "tnk_biofeedback",
        "definition_id": "tnk-definition",
        "definition_version": "1",
        "scientific_version": "tnk-v1",
        "implementation_identity": "tnk-runner",
        "configuration_sha256": "config-hash",
        "window_policy_version": "tnk-dynamic-v1",
        "start_sample": 1000,
        "end_sample": 5000,
    }
    values.update(overrides)
    return DynamicWindowIdentity(**values)


def _result(identity: DynamicWindowIdentity) -> DynamicWindowResult:
    return DynamicWindowResult(
        identity=identity,
        state="ready",
        output_kind="algorithm_group",
        axes={"metric_index": [0]},
        axis_units={"metric_index": "index"},
        arrays={"tnk_score": [0.5]},
        array_units={"tnk_score": "score"},
        quality="clean",
        provenance={"preprocessing_scope": "configured_continuous_before_window_slice"},
        evidence={"extensions": {"tnk_evidence": {"children": {}}}},
    )


def _event(**overrides) -> TnkPlaybackFrameEvent:
    identity = overrides.pop("identity", _identity())
    start_s = identity.start_sample / 1000.0
    end_s = identity.end_sample / 1000.0
    values = {
        "delivery_sequence": 1,
        "session_id": "session-1",
        "delivery_id": "session-1",
        "window_id": window_identity_key(identity),
        "identity": identity,
        "window_start_s": start_s,
        "window_end_s": end_s,
        "sampling_rate_hz": 1000.0,
        "published_at_utc": datetime(2026, 10, 27, tzinfo=timezone.utc),
        "result": _result(identity),
    }
    values.update(overrides)
    return TnkPlaybackFrameEvent(**values)


def test_tnk_frame_event_carries_identity_units_quality_evidence_and_order():
    event = _event()

    assert event.delivery_sequence == 1
    assert event.identity.start_sample == 1000
    assert event.identity.end_sample == 5000
    assert event.result.array_units == {"tnk_score": "score"}
    assert event.result.quality == "clean"
    assert event.result.evidence["extensions"]["tnk_evidence"] == {"children": {}}
    with pytest.raises(ValidationError):
        event.delivery_sequence = 2


@pytest.mark.parametrize(
    "overrides, message",
    [
        ({"delivery_sequence": 0}, "greater than 0"),
        ({"window_end_s": 4.9}, "end time must match"),
        ({"window_start_s": 1.1}, "start time must match"),
        ({"published_at_utc": datetime(2026, 10, 27)}, "must include a timezone"),
    ],
)
def test_tnk_frame_event_rejects_invalid_delivery_or_time_metadata(overrides, message):
    with pytest.raises(ValidationError, match=message):
        _event(**overrides)


def test_tnk_frame_event_rejects_result_from_another_scientific_window():
    other_identity = _identity(start_sample=2000, end_sample=6000)

    with pytest.raises(ValidationError, match="result identity"):
        _event(result=_result(other_identity))


def test_tnk_frame_event_does_not_masquerade_as_a_persisted_window_id():
    with pytest.raises(ValidationError, match="stable scientific identity key"):
        _event(window_id="sqlite-window-row-id")


def test_event_buffer_assigns_monotonic_sequences_and_deduplicates_recent_windows():
    buffer = TnkPlaybackEventBuffer("session-1", capacity=3)
    first = buffer.publish(lambda sequence: _event(delivery_sequence=sequence))
    duplicate = buffer.publish(lambda sequence: _event(delivery_sequence=sequence))

    assert first.delivery_sequence == 1
    assert duplicate == first
    assert buffer.read_after(0).latest_sequence == 1


def test_event_buffer_reports_exact_overflow_range_and_resumes_from_oldest_event():
    buffer = TnkPlaybackEventBuffer("session-1", capacity=2)
    for start_sample in (0, 1000, 2000):
        identity = _identity(start_sample=start_sample, end_sample=start_sample + 4000)
        buffer.publish(lambda sequence, current=identity: _event(
            delivery_sequence=sequence, identity=current,
        ))

    missed = buffer.read_after(0)
    resumed = buffer.read_after(1)

    assert missed.coverage == "incomplete"
    assert missed.discontinuity is not None
    assert missed.discontinuity.first_missing_sequence == 1
    assert missed.discontinuity.last_missing_sequence == 1
    assert missed.discontinuity.affected_start_sample == 0
    assert missed.discontinuity.affected_end_sample == 4000
    assert missed.discontinuity.sample_range_exact
    assert [event.delivery_sequence for event in missed.events] == [2, 3]
    assert resumed.coverage == "complete"
    assert [event.delivery_sequence for event in resumed.events] == [2, 3]


def test_event_buffer_rejects_out_of_order_publication_and_invalid_resume_cursor():
    buffer = TnkPlaybackEventBuffer("session-1", capacity=2)
    identity = _identity(start_sample=2000, end_sample=6000)
    buffer.publish(lambda sequence: _event(delivery_sequence=sequence, identity=identity))
    earlier = _identity(start_sample=1000, end_sample=5000)

    with pytest.raises(ValueError, match="canonical window order"):
        buffer.publish(lambda sequence: _event(delivery_sequence=sequence, identity=earlier))
    with pytest.raises(ValueError, match="exceeds the latest"):
        buffer.read_after(2)
