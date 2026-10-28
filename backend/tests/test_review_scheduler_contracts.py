import pytest

from app.services.review_scheduler_contracts import ReviewTargetIdentity


def test_review_target_identity_keeps_scientific_context_separate_from_trace():
    target = ReviewTargetIdentity(
        trace_id="trace-1",
        generation=2,
        recording_id="recording-1",
        algorithm_id="rbp",
        configuration_fingerprint="cfg-1",
        cursor_sample=500,
        sample_rate_hz=250.0,
        channel_order=("Fz-REF", "Pz-REF"),
        lane="window_independent_analysis",
    )

    assert target.scientific_context_key == (
        "recording-1", "rbp", "cfg-1", 250.0, ("Fz-REF", "Pz-REF")
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [("trace_id", ""), ("recording_id", ""), ("algorithm_id", ""), ("configuration_fingerprint", "")],
)
def test_review_target_identity_rejects_missing_required_identity(field, value):
    values = dict(
        trace_id="trace-1",
        generation=0,
        recording_id="recording-1",
        algorithm_id="rbp",
        configuration_fingerprint="cfg-1",
        cursor_sample=0,
        sample_rate_hz=250.0,
        channel_order=("Fz-REF",),
        lane="window_independent_analysis",
    )
    values[field] = value

    with pytest.raises(ValueError):
        ReviewTargetIdentity(**values)
