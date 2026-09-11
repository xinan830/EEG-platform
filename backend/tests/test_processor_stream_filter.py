import numpy as np

from app.eeg_core.processor import EEGProcessor


CHANNELS = ["Fz", "Pz", "Oz"]


def source_signal(sfreq=100.0, duration_s=7.0):
    times = np.arange(int(sfreq * duration_s)) / sfreq
    return np.column_stack(
        [
            200e-6 + 10e-6 * np.sin(2 * np.pi * 6 * times),
            -100e-6 + 20e-6 * np.sin(2 * np.pi * 10 * times),
            50e-6 + 5e-6 * np.sin(2 * np.pi * 20 * times),
        ]
    )


def new_processor(**kwargs):
    return EEGProcessor(100.0, CHANNELS, **kwargs)


def test_stream_filter_is_identical_for_whole_and_split_chunks():
    source = source_signal()
    whole = new_processor()
    split = new_processor()

    whole.push_chunk(source)
    for chunk in np.array_split(source, 14):
        split.push_chunk(chunk)

    np.testing.assert_allclose(split.buffer, whole.buffer, rtol=1e-12, atol=1e-15)
    assert split.total_samples_seen == whole.total_samples_seen == len(source)
    assert split.should_update() is whole.should_update() is True


def test_reset_restarts_filter_like_a_new_processor():
    source = source_signal()
    restarted = new_processor()
    restarted.push_chunk(source)
    restarted.reset_buffers()
    restarted.push_chunk(source)

    fresh = new_processor()
    fresh.push_chunk(source)

    np.testing.assert_allclose(restarted.buffer, fresh.buffer, rtol=1e-12, atol=1e-15)
    assert restarted.total_samples_seen == fresh.total_samples_seen == len(source)


def test_filter_change_rebuilds_retained_window_like_fresh_processing():
    source = source_signal()
    rebuilt = new_processor()
    rebuilt.push_chunk(source)
    rebuilt.set_filters(50.0, 2.0, 30.0)

    fresh = new_processor(notch_freq=50.0, bp_low=2.0, bp_high=30.0)
    fresh.push_chunk(source)

    np.testing.assert_allclose(rebuilt.buffer, fresh.buffer, rtol=1e-12, atol=1e-15)
    assert rebuilt.metrics_bp_low == fresh.metrics_bp_low == 2.0


def test_should_update_waits_for_window_settle_and_update_interval():
    processor = new_processor()
    source = source_signal(duration_s=7.0)

    processor.push_chunk(source[:-1])
    assert processor.should_update() is False
    processor.push_chunk(source[-1:])
    assert processor.should_update() is True
