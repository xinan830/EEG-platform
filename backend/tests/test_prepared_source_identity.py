from dataclasses import replace

import pytest

from app.models.recording import RecordingSummary
from app.services.prepared_source_identity import prepared_source_identity


class Source:
    def analysis_signal_provenance(self, recording):
        return {"signal_mode": "raw", "analysis_reference": "original"}


def key(recording, **options):
    return prepared_source_identity(Source(), recording, low_hz=1, high_hz=30,
                                    notch_hz=options.get("notch"), notch_q=30)


@pytest.mark.parametrize("field,value", [("source_sha256", "different"),
    ("channels", ("F7", "F3")), ("channel_units", ("uV", "V")),
    ("channel_types", ("EOG", "EEG")), ("sfreq", 250), ("import_version", "next")])
def test_scientific_source_changes_cannot_share_continuous_preparation(field, value):
    recording = RecordingSummary("r", "r", "r", ".edf", "now", sfreq=200,
        duration_s=20, channels=("F3", "F7"), source_sha256="source")
    assert key(recording) != key(replace(recording, **{field: value}))
    assert key(recording) == key(replace(recording, original_name="display-only"))


def test_filter_and_build_changes_do_not_share_preparation(monkeypatch):
    recording = RecordingSummary("r", "r", "r", ".edf", "now", source_sha256="source")
    original = key(recording)
    assert key(recording, notch=50) != original
    monkeypatch.setenv("BRAIN_PLATFORM_BUILD_VERSION", "next-build")
    assert key(recording) != original
