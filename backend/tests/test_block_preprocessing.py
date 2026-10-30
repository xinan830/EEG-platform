import gc
from pathlib import Path
import struct

import numpy as np
import pytest

from app.scientific.primitives import preprocess_offline
from app.scientific.primitives.block_filter import preprocess_blocks
from app.services.mapped_preprocessing import mapped_preprocess
from app.services.recording_access import RecordingAccessSession, RecordingSourceIdentity
from app.services.recording_samples import WpfSampleReader
from app.services.recordings import RecordingService
from app.services.run_execution_context import RunCancelledError, RunExecutionContext
from tests.test_wpf_recording import _write_fixture


class _AccessSession(RecordingAccessSession):
    def __init__(self, reader, sfreq, names):
        super().__init__(
            RecordingSourceIdentity("test", "test", "test"), reader, sfreq,
            reader.sample_count, names, (), lambda: None, serialize_reads=False,
        )


@pytest.mark.parametrize("notch", [None, 50, 60])
@pytest.mark.parametrize("block_samples", [17, 997, 65536])
def test_block_zero_phase_matches_full_scipy(notch, block_samples):
    source = np.random.default_rng(2).normal(0, 12e-6, (4000, 3))
    source[1200] += 200e-6
    expected = preprocess_offline(source, 250, low_hz=0.5, high_hz=80, notch_hz=notch)
    actual = np.empty_like(source)
    preprocess_blocks(source, actual, 250, np.empty, low_hz=0.5, high_hz=80,
                      notch_hz=notch, block_samples=block_samples)
    np.testing.assert_allclose(actual, expected, rtol=1e-12, atol=1e-15)


def test_gap_propagation_matches_full_reference():
    source = np.ones((500, 2)) * 1e-6
    source[210:212, 0] = np.nan
    expected = preprocess_offline(source, 250, low_hz=1, high_hz=50)
    actual = np.empty_like(source)
    preprocess_blocks(source, actual, 250, np.empty, low_hz=1, high_hz=50, notch_hz=None, block_samples=29)
    np.testing.assert_allclose(actual, expected, equal_nan=True, rtol=1e-12, atol=1e-15)


def test_wpf_reader_selects_ranges_and_preserves_counter_gaps(tmp_path):
    root = tmp_path / "raw"
    _write_fixture(root)
    with (root / "samples-000001.bin").open("ab") as stream:
        stream.write(struct.pack("<qiiq", 106, 2, 2, 0))
        np.asarray([[7e-6, 106], [8e-6, 107]], dtype="<f8").tofile(stream)
    reader = WpfSampleReader(root)
    values = reader.read(2, 8, [0])[:, 0]
    np.testing.assert_allclose(values, [3e-6, 4e-6, np.nan, np.nan, 7e-6, 8e-6], equal_nan=True)
    reader.close()


def test_wpf_reader_closes_after_cancellation_between_batches(tmp_path):
    root = tmp_path / "raw"
    _write_fixture(root)
    with (root / "samples-000001.bin").open("ab") as stream:
        stream.write(struct.pack("<qiiq", 104, 2, 2, 0))
        np.asarray([[5e-6, 104], [6e-6, 105]], dtype="<f8").tofile(stream)
    reader = WpfSampleReader(root)
    checks = 0

    def check_cancelled():
        nonlocal checks
        checks += 1
        if checks == 2:
            raise RunCancelledError("cancel")

    with pytest.raises(RunCancelledError):
        reader.read(0, 6, [0], check_cancelled=check_cancelled)
    reader.close()


def test_wpf_reader_uses_uniform_payload_view(tmp_path):
    root = tmp_path / "raw"
    _write_fixture(root)
    with (root / "samples-000001.bin").open("ab") as stream:
        stream.write(struct.pack("<qiiq", 104, 4, 2, 0))
        np.asarray([[5e-6, 104], [6e-6, 105], [7e-6, 106], [8e-6, 107]], dtype="<f8").tofile(stream)
    reader = WpfSampleReader(root)
    values = reader.read(0, 8, [0])[:, 0]
    np.testing.assert_allclose(values, [1e-6, 2e-6, 3e-6, 4e-6, 5e-6, 6e-6, 7e-6, 8e-6])
    reader.close()


def test_mapped_owner_cleanup_after_reader_failure(tmp_path, monkeypatch):
    allocated = []
    from app.services import mapped_preprocessing as module
    original = module.MappedArrays
    class Owner(original):
        def __init__(self):
            super().__init__()
            allocated.append(Path(self.directory.name))
    class Broken:
        names = ["F3"]
        sfreq = 250
        sample_count = 100
        closed = False
        def read(self, *_args):
            raise ValueError("bad block")
        def close(self):
            self.closed = True
    reader = Broken()
    class Recording:
        def open_access_session(self, _recording):
            return _AccessSession(reader, 200, ["F3"])
    monkeypatch.setattr(module, "MappedArrays", Owner)
    with pytest.raises(ValueError, match="bad block"):
        mapped_preprocess(Recording(), object(), low_hz=1, high_hz=50, notch_hz=None)
    gc.collect()
    assert reader.closed
    assert not allocated[0].exists()


def test_block_filter_cancellation_stops_preprocessing():
    checks = []
    def check():
        checks.append(1)
        if len(checks) == 3:
            raise RunCancelledError("cancel")
    with pytest.raises(RunCancelledError):
        preprocess_blocks(np.ones((4000, 2)), np.empty((4000, 2)), 250, np.empty,
                          low_hz=1, high_hz=50, notch_hz=None, block_samples=100, check_cancelled=check)
    assert len(checks) == 3


def test_mapping_reads_bounded_selected_blocks_and_remains_live_after_eviction():
    from app.services.analysis_preprocess_cache import AnalysisPreprocessCache, PreprocessedRecording
    from app.services.recording_samples import ArraySampleReader
    calls = []
    class Reader(ArraySampleReader):
        def read(self, start, stop, channels=None, check_cancelled=None):
            calls.append((start, stop, channels))
            return super().read(start, stop, channels, check_cancelled)
    source = np.random.default_rng(21).normal(0, 3e-6, (140000, 3))
    class Recording:
        def open_access_session(self, _recording):
            return _AccessSession(Reader(source, 250, ["F3", "F4", "O1"]), 250, ["F3", "F4", "O1"])
    lease = mapped_preprocess(Recording(), object(), low_hz=1, high_hz=50,
                              notch_hz=50, channels=["F4", "F3"])
    directory = Path(lease.owner.directory.name)
    expected = preprocess_offline(source[:, [1, 0]], 250, low_hz=1, high_hz=50, notch_hz=50)
    np.testing.assert_allclose(lease.data, expected, rtol=1e-12, atol=1e-15)
    assert max(stop - start for start, stop, _ in calls) <= 65536
    assert all(channels == [1, 0] for _, _, channels in calls)
    assert lease.channel_names == ("F4", "F3")
    cache = AnalysisPreprocessCache(max_entries=1)
    cache.put("recording", "v1", lease)
    cache.put("other", "v1", PreprocessedRecording(np.zeros((10, 1)), 250, ("F3",)))
    assert directory.exists()
    np.testing.assert_allclose(lease.data[-100:], expected[-100:], atol=1e-15)
    del lease
    gc.collect()
    assert not directory.exists()


def test_stft_does_not_require_entry_to_fit_in_memory_cache(tmp_path, monkeypatch):
    from tests.test_spectrum_service import _service
    from app.services.analysis_preprocess_cache import AnalysisPreprocessCache
    service, recording = _service(tmp_path)
    time = np.arange(6000) / 200
    from tests.test_spectrum_service import _provide_data
    _provide_data(monkeypatch, service, (10e-6 * np.sin(2 * np.pi * 10 * time))[:, None], 200, ["F3"])
    service._spectral_analysis.preprocess_cache = AnalysisPreprocessCache(max_resident_bytes=1)
    result = service.load_spectrogram(recording, 0, 30, ["F3"], notch_hz=None)
    assert len(result["times_s"]) == 27


def test_edf_reader_uses_sample_range_and_requested_channel_order():
    from app.services.recording_samples import EdfSampleReader
    class Raw:
        info = {"sfreq": 250}
        ch_names = ["F3", "F4"]
        n_times = 1000
        closed = False
        def get_data(self, *, picks, start, stop):
            assert (picks, start, stop) == ([1, 0], 200, 220)
            return np.vstack([np.full(20, 2e-6), np.full(20, 1e-6)])
        def close(self):
            self.closed = True
    raw = Raw()
    reader = EdfSampleReader(raw)
    values = reader.read(200, 220, [1, 0])
    assert values.shape == (20, 2)
    np.testing.assert_array_equal(values[0], [2e-6, 1e-6])
    reader.close()
    assert raw.closed
