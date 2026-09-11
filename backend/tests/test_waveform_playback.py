import numpy as np
from fastapi.testclient import TestClient

from app.models.recording import RecordingSummary
from app.services.waveform_playback import (
    DisplaySignalFilter,
    WaveformPlaybackSession,
    select_display_channels,
)


def test_display_channel_selection_uses_o2_when_oz_is_not_present():
    indices, names = select_display_channels(["C3", "Fz", "Pz", "O2", "F3", "F4", "C4"])

    assert indices == [1, 2, 3, 4, 5]
    assert names == ["Fz", "Pz", "O2", "F3", "F4"]


def test_display_channel_selection_preserves_user_selected_order():
    indices, names = select_display_channels(
        ["C3", "Fz", "Pz", "O2", "F3"],
        requested_names=["O2", "Fz", "C3"],
    )

    assert indices == [3, 1, 0]
    assert names == ["O2", "Fz", "C3"]


def test_display_filter_removes_a_constant_electrode_dc_offset():
    filtered = DisplaySignalFilter(sfreq=500.0, channel_count=1).process(
        np.full((25, 1), 0.005, dtype=float),
    )

    assert filtered.shape == (25, 1)
    assert np.max(np.abs(filtered)) < 1e-12


def test_display_filter_supports_broad_review_settings_without_notch():
    """阅图默认应允许 .5–70Hz、关闭陷波，而非强制生物反馈的 1–30Hz。"""
    samples = np.sin(np.linspace(0, 2 * np.pi, 100, endpoint=False)).reshape(-1, 1) * 1e-5

    filtered = DisplaySignalFilter(
        sfreq=500.0,
        channel_count=1,
        notch_freq=None,
        bp_low=0.5,
        bp_high=70.0,
    ).process(samples)

    assert filtered.shape == samples.shape
    assert np.isfinite(filtered).all()


def test_display_filter_is_invariant_to_chunk_boundaries():
    samples = np.sin(np.linspace(0, 12 * np.pi, 251, endpoint=False)).reshape(-1, 1) * 1e-5
    whole = DisplaySignalFilter(sfreq=500.0, channel_count=1, notch_freq=50.0).process(samples)
    chunked_filter = DisplaySignalFilter(sfreq=500.0, channel_count=1, notch_freq=50.0)
    chunked = np.concatenate([
        chunked_filter.process(samples[:25]),
        chunked_filter.process(samples[25:73]),
        chunked_filter.process(samples[73:]),
    ])
    np.testing.assert_allclose(whole, chunked, rtol=1e-12, atol=1e-15)


def test_filter_state_warmup_matches_continuous_stream_after_seek():
    samples = np.sin(np.linspace(0, 8 * np.pi, 180, endpoint=False)).reshape(-1, 1) * 1e-5
    continuous = DisplaySignalFilter(sfreq=500.0, channel_count=1).process(samples)
    resumed = DisplaySignalFilter(sfreq=500.0, channel_count=1)
    for cursor in range(0, 100, 25):
        resumed.process(samples[cursor:cursor + 25])
    np.testing.assert_allclose(resumed.process(samples[100:125]), continuous[100:125], rtol=1e-12, atol=1e-15)


def test_filter_snapshot_restore_matches_continuous_stream():
    samples = np.sin(np.linspace(0, 8 * np.pi, 180, endpoint=False)).reshape(-1, 1) * 1e-5
    continuous = DisplaySignalFilter(sfreq=500.0, channel_count=1).process(samples)
    first = DisplaySignalFilter(sfreq=500.0, channel_count=1)
    first.process(samples[:100])
    resumed = DisplaySignalFilter(sfreq=500.0, channel_count=1)
    resumed.restore(first.snapshot())
    np.testing.assert_allclose(resumed.process(samples[100:125]), continuous[100:125], rtol=1e-12, atol=1e-15)


def test_display_filter_rejects_an_invalid_cutoff_range():
    with np.testing.assert_raises_regex(ValueError, "低切"):
        DisplaySignalFilter(sfreq=500.0, channel_count=1, bp_low=70.0, bp_high=0.5)


def test_waveform_session_accepts_direct_import_without_analysis_mapping():
    recording = RecordingSummary(
        id="recording-1", original_name="sample.bdf", stored_name="sample.bdf",
        extension=".bdf", created_at="2026-09-09T00:00:00Z", mapping=None,
    )

    session = WaveformPlaybackSession(recording, recordings=object())

    assert session.recording_id == "recording-1"
    assert session.control("seek", position_s=12.5)["action"] == "seek"
    assert session.control("set_filters", low_cut_hz=0.5, high_cut_hz=70.0, notch_hz=None)["action"] == "set_filters"


def test_waveform_session_rejects_invalid_display_filter_settings():
    recording = RecordingSummary(
        id="recording-1", original_name="sample.bdf", stored_name="sample.bdf",
        extension=".bdf", created_at="2026-09-09T00:00:00Z", mapping=None,
    )
    session = WaveformPlaybackSession(recording, recordings=object())

    with np.testing.assert_raises_regex(ValueError, "低切"):
        session.control("set_filters", low_cut_hz=70.0, high_cut_hz=0.5, notch_hz=None)


def test_waveform_session_rejects_empty_channel_selection():
    recording = RecordingSummary(
        id="recording-1", original_name="sample.bdf", stored_name="sample.bdf",
        extension=".bdf", created_at="2026-09-09T00:00:00Z", mapping=None,
    )
    session = WaveformPlaybackSession(recording, recordings=object())

    with np.testing.assert_raises_regex(ValueError, "至少选择"):
        session.control("set_channels", channels=[])


def test_filter_change_resets_the_sweep_from_file_start():
    recording = RecordingSummary(
        id="recording-1", original_name="sample.bdf", stored_name="sample.bdf",
        extension=".bdf", created_at="2026-09-09T00:00:00Z", mapping=None,
    )
    session = WaveformPlaybackSession(recording, recordings=object())

    session.control("set_filters", low_cut_hz=1.0, high_cut_hz=35.0, notch_hz=50.0)
    reset, seek_to = session._drain_controls()

    assert reset is True
    assert seek_to == 0.0
    assert session._filter_settings == {
        "low_cut_hz": 1.0, "high_cut_hz": 35.0, "notch_hz": 50.0,
        "baseline_stabilization": False,
    }


def test_baseline_stabilization_is_explicit_and_defaults_off():
    samples = np.linspace(0.001, 0.002, 100).reshape(-1, 1)
    default = DisplaySignalFilter(sfreq=500.0, channel_count=1).process(samples)
    enabled = DisplaySignalFilter(
        sfreq=500.0, channel_count=1, baseline_stabilization=True,
    ).process(samples)

    assert not np.allclose(default, enabled, rtol=1e-6, atol=1e-12)


def test_filter_change_resumes_a_paused_session_from_file_start():
    recording = RecordingSummary(
        id="recording-1", original_name="sample.bdf", stored_name="sample.bdf",
        extension=".bdf", created_at="2026-09-09T00:00:00Z", mapping=None,
    )
    session = WaveformPlaybackSession(recording, recordings=object())
    session.control("pause")
    session._drain_controls()
    assert session.status == "paused"

    session.control("set_filters", low_cut_hz=1.0, high_cut_hz=35.0, notch_hz=50.0)
    reset, seek_to = session._drain_controls()

    assert reset is True
    assert seek_to == 0.0
    assert session.status == "running"
    assert not session._paused.is_set()


def test_filter_change_defaults_to_file_start_but_seek_can_still_target_a_position():
    assert WaveformPlaybackSession._resolve_reset_position(4250, None, 10000, 500.0) == 0
    assert WaveformPlaybackSession._resolve_reset_position(4250, 3.5, 10000, 500.0) == 1750


def test_filter_control_ignores_previous_position_and_restarts_from_file_start():
    recording = RecordingSummary(
        id="recording-1", original_name="sample.bdf", stored_name="sample.bdf",
        extension=".bdf", created_at="2026-09-09T00:00:00Z", mapping=None,
    )
    session = WaveformPlaybackSession(recording, recordings=object())
    session.control("set_filters", low_cut_hz=1.0, high_cut_hz=35.0, notch_hz=None, position_s=12.5)
    reset, seek_to = session._drain_controls()
    assert reset is True
    assert seek_to == 0.0


def test_waveform_playback_api_creates_a_session_without_mapping(monkeypatch):
    from app.main import app

    class Session:
        id = "waveform-session"
        status = "running"

    class Service:
        def create(self, recording_id, requested_channels=None):
            assert recording_id == "recording-1"
            assert requested_channels is None
            return Session()

    monkeypatch.setattr(app.state, "waveform_playback_service", Service(), raising=False)

    response = TestClient(app).post("/api/recordings/recording-1/waveform-playback")

    assert response.status_code == 201
    assert response.json() == {
        "session_id": "waveform-session",
        "recording_id": "recording-1",
        "status": "running",
        "websocket_url": "/api/waveform-playback/waveform-session/events",
    }


def test_waveform_playback_create_passes_initial_channels_to_the_session(monkeypatch):
    from app.main import app

    class Session:
        id = "waveform-session"
        status = "running"

    class Service:
        def create(self, recording_id, requested_channels=None):
            assert recording_id == "recording-1"
            assert requested_channels == ["O2", "Fz"]
            return Session()

    monkeypatch.setattr(app.state, "waveform_playback_service", Service(), raising=False)

    response = TestClient(app).post(
        "/api/recordings/recording-1/waveform-playback",
        json={"channels": ["O2", "Fz"]},
    )

    assert response.status_code == 201


def test_waveform_playback_control_does_not_pass_action_twice(monkeypatch):
    from app.main import app

    class Session:
        def control(self, action, **payload):
            assert action == "seek"
            assert payload == {"position_s": 12.5}
            return {"action": action}

    class Service:
        def require(self, session_id):
            assert session_id == "waveform-session"
            return Session()

    monkeypatch.setattr(app.state, "waveform_playback_service", Service(), raising=False)

    response = TestClient(app).post(
        "/api/waveform-playback/waveform-session/control",
        json={"action": "seek", "position_s": 12.5},
    )

    assert response.status_code == 200
    assert response.json() == {"action": "seek"}


def test_waveform_filter_control_keeps_an_explicit_notch_off_value(monkeypatch):
    from app.main import app

    class Session:
        def control(self, action, **payload):
            assert action == "set_filters"
            assert payload == {"low_cut_hz": 0.5, "high_cut_hz": 70.0, "notch_hz": None}
            return {"action": action}

    class Service:
        def require(self, session_id):
            assert session_id == "waveform-session"
            return Session()

    monkeypatch.setattr(app.state, "waveform_playback_service", Service(), raising=False)

    response = TestClient(app).post(
        "/api/waveform-playback/waveform-session/control",
        json={"action": "set_filters", "low_cut_hz": 0.5, "high_cut_hz": 70.0, "notch_hz": None},
    )

    assert response.status_code == 200
    assert response.json() == {"action": "set_filters"}


def test_waveform_filter_control_forwards_baseline_stabilization(monkeypatch):
    from app.main import app

    class Session:
        def control(self, action, **payload):
            assert action == "set_filters"
            assert payload == {"baseline_stabilization": True}
            return {"action": action}

    class Service:
        def require(self, _session_id):
            return Session()

    monkeypatch.setattr(app.state, "waveform_playback_service", Service(), raising=False)
    response = TestClient(app).post(
        "/api/waveform-playback/waveform-session/control",
        json={"action": "set_filters", "baseline_stabilization": True},
    )
    assert response.status_code == 200


def test_waveform_channel_control_forwards_the_selected_channel_list(monkeypatch):
    from app.main import app

    class Session:
        def control(self, action, **payload):
            assert action == "set_channels"
            assert payload == {"channels": ["O2", "Fz", "C3"]}
            return {"action": action}

    class Service:
        def require(self, session_id):
            assert session_id == "waveform-session"
            return Session()

    monkeypatch.setattr(app.state, "waveform_playback_service", Service(), raising=False)

    response = TestClient(app).post(
        "/api/waveform-playback/waveform-session/control",
        json={"action": "set_channels", "channels": ["O2", "Fz", "C3"]},
    )

    assert response.status_code == 200
    assert response.json() == {"action": "set_channels"}


def test_waveform_event_socket_sends_binary_waveform_frames(monkeypatch):
    from app.main import app

    class Session:
        async def next_message(self):
            return b"binary-waveform-frame"

    class Service:
        def require(self, session_id):
            assert session_id == "waveform-session"
            return Session()

    monkeypatch.setattr(app.state, "waveform_playback_service", Service(), raising=False)

    with TestClient(app).websocket_connect("/api/waveform-playback/waveform-session/events") as websocket:
        assert websocket.receive_bytes() == b"binary-waveform-frame"


def test_waveform_session_reads_raw_data_in_display_chunks():
    class Annotations:
        onset = []
        description = []

    class Raw:
        ch_names = ["Fz"]
        n_times = 20
        info = {"sfreq": 200.0}
        annotations = Annotations()

        def __init__(self):
            self.calls = []
            self.closed = False

        def get_data(self, picks, start, stop):
            self.calls.append((picks, start, stop))
            return np.zeros((1, stop - start), dtype=float)

        def close(self):
            self.closed = True

    class Readers:
        def __init__(self, raw):
            self.raw = raw

        def open_data_reader(self, _recording):
            return self.raw, 200.0, ["Fz"], []

    raw = Raw()
    recording = RecordingSummary(
        id="recording-1", original_name="sample.bdf", stored_name="sample.bdf",
        extension=".bdf", created_at="2026-09-09T00:00:00Z", mapping=None,
    )
    session = WaveformPlaybackSession(recording, Readers(raw), requested_channels=["Fz"])
    session.start()
    messages = []
    while True:
        message = session._outbound.get(timeout=2)
        messages.append(message)
        if isinstance(message, dict) and message.get("type") == "completed":
            break
    assert messages[0]["type"] == "info"
    assert any(isinstance(message, bytes) for message in messages)
    assert all(stop - start <= 10 for _, start, stop in raw.calls)
    assert raw.closed is True
