import pytest

from app.services.live_analysis_contracts import LiveAnalysisAlgorithmRegistry, LiveAnalysisEnvelope
from app.services.live_recording_access import LiveRecordingAccessError


class _Algorithm:
    def __init__(self, algorithm_id: str):
        self.algorithm_id = algorithm_id


def test_registry_resolves_ids_case_insensitively_and_rejects_duplicates():
    algorithm = _Algorithm("RBP")
    registry = LiveAnalysisAlgorithmRegistry((algorithm,))

    assert registry.resolve("rbp") is algorithm
    with pytest.raises(ValueError, match="不能为空或重复"):
        registry.register(_Algorithm("rbp"))


def test_registry_returns_stable_failure_for_unknown_algorithm():
    registry = LiveAnalysisAlgorithmRegistry()

    with pytest.raises(LiveRecordingAccessError) as error:
        registry.resolve("unknown")

    assert error.value.code == "LIVE_ALGORITHM_UNSUPPORTED"


def test_live_update_envelope_rejects_unversioned_or_untyped_payload_fields():
    update = LiveAnalysisEnvelope(
        acquisition_session_id="session-1",
        algorithm_id="rbp",
        revision=3,
        state="ready",
        payload={"values": [0.2, 0.8]},
    )
    assert update.schema_version == "live-analysis-update-v1"
    with pytest.raises(ValueError):
        LiveAnalysisEnvelope(
            acquisition_session_id="session-1",
            algorithm_id="rbp",
            revision=3,
            state="ready",
            undocumented=True,
        )
