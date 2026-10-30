from app.services.recording_access import RecordingAccessSession, RecordingSourceIdentity
from app.services.recording_samples import ArraySampleReader


def array_access_session(recording, values, sampling_rate_hz, channel_names):
    reader = ArraySampleReader(values, sampling_rate_hz, channel_names)
    return RecordingAccessSession(
        RecordingSourceIdentity(recording.id, "synthetic", "test"),
        reader,
        sampling_rate_hz,
        len(values),
        reader.names,
        (),
        lambda: None,
        serialize_reads=False,
    )


def data_provider(monkeypatch, service, values, sampling_rate_hz, channel_names):
    monkeypatch.setattr(
        service,
        "open_access_session",
        lambda recording, **_kwargs: array_access_session(
            recording, values, sampling_rate_hz, channel_names,
        ),
    )
