"""A warm analysis catalogue must avoid per-snapshot I/O without hiding edits."""

import copy
import json

import pytest

from app.core.performance_trace import performance_trace
from app.services.recordings import RecordingService
from app.services.wpf_recording import WpfRecordingError
from test_acquisition_analysis_signals import fixture_recording, compatible_average_profile
import app.services.analysis_montage_snapshots as snapshots_module


@pytest.fixture
def catalogue(tmp_path):
    root = tmp_path / "recording"
    _, manifest = fixture_recording(root)
    profiles = []
    for index in range(30):
        profile = compatible_average_profile(manifest)
        profile["Id"] = f"saved-profile-{index}"
        profiles.append(profile)
    service = RecordingService(tmp_path / "stored", tmp_path / "catalog.sqlite3")
    recording = service.register_wpf_recording(
        str(root), signal_mode="acquisition_montage", analysis_montage_profiles=profiles,
    )
    return service, recording, root, manifest


def test_cold_and_warm_catalogues_use_two_connections_for_thirty_snapshots(catalogue, caplog):
    service, recording, _, _ = catalogue
    owner = service._analysis_montage_snapshots
    owner._catalogues.clear()
    owner._resolved_views.clear()
    caplog.set_level("INFO")
    for state in ("cold", "warm"):
        with performance_trace("catalogue_test", state=state) as trace:
            current = service.require_recording(recording.id)
        assert current.montage_catalogue == recording.montage_catalogue
        assert trace.metrics["database_connect_calls"] == 2
        assert trace.metrics["montage_snapshot_rows"] == 30


def test_warm_catalogue_and_resolved_views_are_isolated_from_callers(catalogue, monkeypatch):
    service, recording, root, _ = catalogue
    expected = copy.deepcopy(recording.montage_catalogue)
    returned = service.require_recording(recording.id)
    returned.montage_catalogue[-1]["outputs"][0]["role"] = "damaged-by-caller"

    def unexpected(*_, **__):
        pytest.fail("warm catalogue must not rebuild captured or saved views")

    monkeypatch.setattr(snapshots_module, "historical_montage_catalogue", unexpected)
    monkeypatch.setattr(snapshots_module, "read_acquisition_signal_view", unexpected)
    assert service.require_recording(recording.id).montage_catalogue == expected
    entry = next(item for item in expected if item["snapshot_source"] == "analysis_configuration_snapshot")
    view = service._analysis_montage_snapshots.resolve(recording.id, root, entry["id"])
    view.provenance["formulas"].clear()
    assert service._analysis_montage_snapshots.resolve(recording.id, root, entry["id"]).provenance["formulas"]


def test_saved_snapshot_does_not_duplicate_a_captured_catalogue_id(catalogue, monkeypatch):
    service, recording, _, _ = catalogue
    owner = service._analysis_montage_snapshots
    saved = next(item for item in recording.montage_catalogue
                 if item["snapshot_source"] == "analysis_configuration_snapshot")
    original = snapshots_module.historical_montage_catalogue

    def duplicate_id(directory):
        captured = list(original(directory))
        captured[0]["id"] = saved["id"]
        captured[0]["label"] = "different captured projection"
        return tuple(captured)

    monkeypatch.setattr(snapshots_module, "historical_montage_catalogue", duplicate_id)
    owner._catalogues.clear()
    result = service.require_recording(recording.id).montage_catalogue
    assert sum(item["id"] == saved["id"] for item in result) == 1


@pytest.mark.parametrize("damage", ["digest", "profile", "manifest"])
def test_warm_catalogue_revalidates_changed_content(catalogue, damage):
    service, recording, root, manifest = catalogue
    if damage == "manifest":
        manifest["HardwareConfiguration"]["channel_configuration_snapshot_json"] = "{}"
        (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    else:
        with service._connect() as connection:
            if damage == "digest":
                connection.execute("UPDATE analysis_montage_snapshots SET snapshot_sha256 = 'broken'")
            else:
                connection.execute("UPDATE analysis_montage_snapshots SET profile_json = '{}'")
    with pytest.raises((WpfRecordingError, ValueError, KeyError)):
        service.require_recording(recording.id)


def test_catalogue_reuse_is_bounded_and_new_snapshots_are_visible(catalogue):
    service, recording, root, manifest = catalogue
    owner = service._analysis_montage_snapshots
    owner._catalogue_limit = 2
    original_ids = {entry["id"] for entry in recording.montage_catalogue}
    for revision in (4, 5, 6):
        profile = compatible_average_profile(manifest)
        profile["Revision"] = revision
        pending = owner.prepare(root, [json.dumps(profile)])
        owner.save(recording.id, pending)
        current = service.require_recording(recording.id)
        assert original_ids < {entry["id"] for entry in current.montage_catalogue}
        assert pending[0][0] in {entry["id"] for entry in current.montage_catalogue}
        assert len(owner._catalogues) <= 2
