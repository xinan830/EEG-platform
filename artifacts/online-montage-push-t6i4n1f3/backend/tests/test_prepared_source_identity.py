from dataclasses import replace

import pytest

from app.models.recording import RecordingSummary
from app.services.prepared_source_identity import prepared_source_identity


class Source:
    signal_source = {"signal_mode": "raw", "analysis_reference": "original"}

    def analysis_signal_provenance(self, recording):
        return self.signal_source


def key(recording, source=None, **options):
    return prepared_source_identity(source or Source(), recording,
        low_hz=options.get("low_hz", 1), high_hz=options.get("high_hz", 30),
        notch_hz=options.get("notch"), notch_q=options.get("notch_q", 30),
        channel_names=options.get("selected_channels"))


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


@pytest.mark.parametrize("options", [
    {"low_hz": 0.5}, {"high_hz": 40}, {"notch": 50}, {"notch_q": 25},
    {"selected_channels": ("F7",)},
])
def test_preprocessing_and_channel_selection_changes_invalidate_prepared_identity(options):
    recording = RecordingSummary("r", "r", "r", ".edf", "now", sfreq=200,
        duration_s=20, channels=("F3", "F7"), source_sha256="source")
    assert key(recording, **options) != key(recording)


def test_montage_reference_and_projection_order_invalidate_prepared_identity():
    recording = RecordingSummary("r", "r", "r", ".edf", "now", sfreq=200,
        duration_s=20, channels=("F3", "F7"), source_sha256="source")
    source = Source()
    baseline = key(recording, source)
    source.signal_source = {
        "signal_mode": "analysis_montage",
        "analysis_reference": "average_reference",
        "montage_id": "average-v2",
        "montage_fingerprint": "sha256:profile-a",
        "outputs": ["F3-AV", "F7-AV"],
    }
    projected = key(recording, source)
    assert projected != baseline
    source.signal_source = {**source.signal_source, "outputs": ["F7-AV", "F3-AV"]}
    assert key(recording, source) != projected


def test_source_unit_metadata_invalidates_prepared_identity():
    recording = RecordingSummary("r", "r", "r", ".edf", "now", sfreq=200,
        duration_s=20, channels=("F3", "F7"), channel_units=("V", "V"), source_sha256="source")
    assert key(recording) != key(replace(recording, channel_units=("uV", "V")))
