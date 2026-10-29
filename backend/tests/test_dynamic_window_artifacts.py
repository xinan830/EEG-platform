import pytest

from app.algorithm_runtime.errors import DynamicWindowCapacityExceededError
from app.services.dynamic_window_artifacts import DynamicWindowArtifactStore


def test_directory_usage_tracks_published_and_removed_files(tmp_path):
    store = DynamicWindowArtifactStore(tmp_path, repository=object())
    artifact = tmp_path / "result.npz"
    artifact.write_bytes(b"artifact")
    store._track_file(artifact)
    reservation = store.MAX_PAYLOAD_BYTES + store.MAX_METADATA_BYTES

    with pytest.raises(DynamicWindowCapacityExceededError) as error:
        store.check_admission_budget(reservation + artifact.stat().st_size - 1, 1)
    assert error.value.detail["used_bytes"] == artifact.stat().st_size

    store._unlink_file(artifact)
    store._unlink_file(artifact)
    store.check_admission_budget(reservation, 1)
