from __future__ import annotations

import numpy as np

from app.models.recording import RecordingSummary
from app.services.playback import PlaybackSession


def test_legacy_playback_keeps_raw_labels_instead_of_global_role_aliases() -> None:
    recording = RecordingSummary(
        id="r1", original_name="sample.edf", stored_name="sample.edf", extension=".edf",
        created_at="2026-09-16T00:00:00Z",
    )

    class Recordings:
        def load_data(self, _recording):
            return np.zeros((4, 3)), 500.0, ["Fz", "Pz", "O2"], []

    _data, _sfreq, names, _events = PlaybackSession(recording, Recordings())._canonical_data()

    assert names == ["Fz", "Pz", "O2"]
