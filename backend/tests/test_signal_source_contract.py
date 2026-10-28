import numpy as np
import pytest
from types import SimpleNamespace

from app.algorithm_runtime.signal_source import SignalSourceData, SignalSourceRequest, SpectralSourceRequest
from app.algorithms.builtin_modules import build_builtin_modules
from app.services.algorithm_recording_context import _RecordingAlgorithmContext


def test_signal_source_request_requires_explicit_ordered_identity_and_preprocessing():
    request = SignalSourceRequest(
        roles=("left", "right"), start_sample=10, end_sample=20,
        expected_sfreq_hz=256.0, montage_id="montage-a", montage_fingerprint="sha256:a",
        preprocessing_scope="continuous_before_slice", low_hz=1.0, high_hz=30.0,
        notch_hz=50.0,
    )

    assert request.roles == ("left", "right")
    assert request.end_sample - request.start_sample == 10


def test_spectral_source_request_freezes_sample_order_filters_and_montage():
    request = SpectralSourceRequest(
        roles=("channel",), start_sample=100, end_sample=1124,
        expected_sfreq_hz=256.0, montage_id="montage-a",
        montage_fingerprint="sha256:a", filter_low_hz=1.0,
        filter_high_hz=30.0, output_low_hz=1.0, output_high_hz=30.0,
        notch_hz=50.0,
    )

    assert request.end_sample - request.start_sample == 4 * 256
    assert request.preprocessing_scope == "configured_continuous_before_slice"


def test_spectral_source_request_rejects_non_finite_frequency_ranges():
    with pytest.raises(ValueError, match="must be finite"):
        SpectralSourceRequest(
            roles=("channel",), start_sample=0, end_sample=1024,
            expected_sfreq_hz=256.0, montage_id="montage-a",
            montage_fingerprint="sha256:a", filter_low_hz=1.0,
            filter_high_hz=float("inf"), output_low_hz=1.0,
            output_high_hz=30.0, notch_hz=None,
        )


@pytest.mark.parametrize("values", [
    np.zeros((10, 2), dtype=np.float32),
    np.zeros((2, 10), dtype=np.float64),
    np.zeros((9, 2), dtype=np.float64),
])
def test_signal_source_data_rejects_dtype_shape_or_bounds_mismatch(values):
    with pytest.raises(ValueError):
        SignalSourceData(
            values_v=values, role_to_output={"left": "F3", "right": "F4"},
            channel_order=("F3", "F4"), sfreq_hz=256.0, start_sample=0,
            end_sample=10, unit="V", missing_sample_policy="preserve_nan",
            montage_id="montage-a", montage_fingerprint="sha256:a",
            preprocessing_scope="none", preprocessing={}, provenance={"source": "test"},
        )


def test_signal_source_reject_policy_rejects_gaps():
    values = np.zeros((10, 1), dtype=np.float64)
    values[4, 0] = np.nan
    with pytest.raises(ValueError, match="missing or non-finite"):
        SignalSourceData(
            values_v=values, role_to_output={"channel": "F3"}, channel_order=("F3",),
            sfreq_hz=256.0, start_sample=0, end_sample=10, unit="V",
            missing_sample_policy="reject", montage_id="montage-a",
            montage_fingerprint="sha256:a", preprocessing_scope="none",
            preprocessing={}, provenance={"source": "test"},
        )


def test_signal_source_preserves_declared_channel_and_role_order():
    values = np.asarray([[1.0, 2.0], [3.0, 4.0]], dtype=np.float64)
    source = SignalSourceData(
        values_v=values, role_to_output={"right": "F4", "left": "F3"},
        channel_order=("F4", "F3"), sfreq_hz=256.0, start_sample=4,
        end_sample=6, unit="V", missing_sample_policy="preserve_nan",
        montage_id="montage-a", montage_fingerprint="sha256:a",
        preprocessing_scope="none", preprocessing={}, provenance={"source_sha256": "abc"},
    )
    assert source.channel_order == ("F4", "F3")
    assert source.role_to_output == {"right": "F4", "left": "F3"}
    np.testing.assert_array_equal(source.values_v[:, 0], [1.0, 3.0])


@pytest.mark.parametrize("mapping", [
    {"left": "F3", "right": "F3"},
    {"left": "F3", "right": "F4", "extra": "F7"},
    {"left": "F3"},
])
def test_signal_source_rejects_incomplete_or_ambiguous_role_mapping(mapping):
    with pytest.raises(ValueError, match="role mapping"):
        SignalSourceData(
            values_v=np.zeros((4, 2), dtype=np.float64), role_to_output=mapping,
            channel_order=("F3", "F4"), sfreq_hz=256.0, start_sample=0,
            end_sample=4, unit="V", missing_sample_policy="preserve_nan",
            montage_id="montage-a", montage_fingerprint="sha256:a",
            preprocessing_scope="none", preprocessing={}, provenance={"source": "test"},
        )


def test_signal_source_preserves_nan_only_under_explicit_preserve_policy():
    values = np.zeros((4, 1), dtype=np.float64)
    values[1, 0] = np.nan
    preserved = SignalSourceData(
        values_v=values, role_to_output={"channel": "F3"}, channel_order=("F3",),
        sfreq_hz=256.0, start_sample=0, end_sample=4, unit="V",
        missing_sample_policy="preserve_nan", montage_id="montage-a",
        montage_fingerprint="sha256:a", preprocessing_scope="none",
        preprocessing={}, provenance={"source_sha256": "abc"},
    )
    assert np.isnan(preserved.values_v[1, 0])


class _RecordingStub:
    def __init__(self, *, montage=None, loader=None):
        self.id = "recording-a"
        self.channels = ("F3", "F4")
        self.sfreq_hz = 10.0
        self.duration_s = 10.0
        self.source_sha256 = "source-a"
        self.montage = montage or {
            "montage_id": "montage-a", "montage_fingerprint": "sha256:a",
        }
        self.loader = loader

    def analysis_signal_provenance(self, _recording):
        return self.montage

    def load_spectrum(self, *args, **kwargs):
        if self.loader:
            return self.loader(*args, **kwargs)
        raise RuntimeError("filter execution failed")


def _spectral_request(**changes):
    values = {
        "roles": ("F4", "F3"), "start_sample": 11, "end_sample": 21,
        "expected_sfreq_hz": 10.0, "montage_id": "montage-a",
        "montage_fingerprint": "sha256:a", "filter_low_hz": 1.0,
        "filter_high_hz": 4.0, "output_low_hz": 1.0,
        "output_high_hz": 4.0, "notch_hz": None,
    }
    values.update(changes)
    return SpectralSourceRequest(**values)


@pytest.mark.parametrize("mapping", [
    {"F4": "F4"},
    {"F4": "F4", "F3": "F3", "other": "F3"},
])
def test_spectral_loader_rejects_missing_or_extra_roles(mapping):
    context = _RecordingAlgorithmContext(_RecordingStub(), _RecordingStub())
    with pytest.raises(ValueError, match="exactly the requested channels"):
        context.load_spectral_source(_spectral_request(), role_to_channel=mapping)


@pytest.mark.parametrize("changes", [
    {"montage_fingerprint": "sha256:stale"},
    {"expected_sfreq_hz": 20.0},
    {"end_sample": 101},
])
def test_spectral_loader_rejects_stale_montage_rate_or_sample_bounds(changes):
    recording = _RecordingStub()
    context = _RecordingAlgorithmContext(recording, recording)
    with pytest.raises(ValueError, match="changed after request planning|exceed the recording"):
        context.load_spectral_source(_spectral_request(**changes), role_to_channel={"F4": "F4", "F3": "F3"})


def test_spectral_loader_rounds_seconds_to_floor_start_and_rounded_duration():
    calls = []

    def fail_after_capture(recording, start_s, window_s, channels, **kwargs):
        calls.append((start_s, window_s, channels))
        raise RuntimeError("stop after observing boundary conversion")

    recording = _RecordingStub(loader=fail_after_capture)
    context = _RecordingAlgorithmContext(recording, recording)
    with pytest.raises(RuntimeError, match="stop after observing"):
        context.load_spectrum(start_s=1.29, window_s=1.06, channels=["F4", "F3"],
                              low_hz=1, high_hz=4)
    assert calls == [(1.2, 1.1, ["F4", "F3"])]


def test_spectral_loader_propagates_filter_failure_without_fabricating_spectrum():
    recording = _RecordingStub()
    context = _RecordingAlgorithmContext(recording, recording)
    with pytest.raises(RuntimeError, match="filter execution failed"):
        context.load_spectral_source(
            _spectral_request(), role_to_channel={"F4": "F4", "F3": "F3"},
        )


def test_spectral_loader_passes_execution_control_to_preprocessing_service():
    calls = []
    control = object()

    def capture_control(*args, **kwargs):
        calls.append(kwargs["execution_context"])
        raise RuntimeError("stop after observing execution control")

    recording = _RecordingStub(loader=capture_control)
    context = _RecordingAlgorithmContext(recording, recording, control)
    with pytest.raises(RuntimeError, match="stop after observing"):
        context.load_spectral_source(
            _spectral_request(), role_to_channel={"F4": "F4", "F3": "F3"},
        )
    assert calls == [control]


def test_typed_spectral_request_provenance_is_complete_and_ordered():
    request = _spectral_request()
    evidence = _RecordingAlgorithmContext._spectral_request_evidence(request, ["F4", "F3"])
    assert evidence == {
        "roles": ["F4", "F3"], "channel_order": ["F4", "F3"],
        "start_sample": 11, "end_sample": 21, "sfreq_hz": 10.0,
        "signal_input_unit": "V", "service_density_unit": "uV^2/Hz",
        "algorithm_density_unit": "V^2/Hz", "montage_id": "montage-a",
        "montage_fingerprint": "sha256:a",
        "preprocessing_scope": "configured_continuous_before_slice",
        "filter_low_hz": 1.0, "filter_high_hz": 4.0,
        "output_low_hz": 1.0, "output_high_hz": 4.0,
        "notch_hz": None, "representation": "spectrum",
    }


def test_every_official_algorithm_declares_signal_requirements():
    modules = build_builtin_modules()

    assert len(modules) == 10
    for module in modules:
        requirements = module.manifest.signal_requirements
        assert requirements is not None, module.manifest.algorithm_id
        assert requirements.input_roles
        assert requirements.representation in {"time_series", "spectrum", "spectrogram", "mixed"}
