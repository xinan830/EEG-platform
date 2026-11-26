"""Reference roles take precedence over EEG bad-channel handling at the source boundary."""
from dataclasses import replace

import numpy as np
import pytest

from app.scientific.contracts.preprocessing import BadChannelSpec
from app.services.acquisition_analysis_signals import AcquisitionSignal, AcquisitionSignalView
from test_artifact_reference_channels import make_recording


@pytest.mark.parametrize("montage", [False, True])
@pytest.mark.parametrize("policy", ["exclude", "interpolate"])
def test_reference_role_removes_overlapping_bad_targets_without_hiding_other_errors(tmp_path, montage, policy):
    service, recording, values = make_recording(tmp_path)
    recording = replace(recording, channel_identities=(("F3", "eog"),))
    if montage:
        names = recording.channels
        signals = tuple(AcquisitionSignal(n+"-AVG", n, names, "mean") for n in names)
        view = AcquisitionSignalView(signals, {"snapshot_sha256": "car", "formulas": [
            dict(name=s.name, positive=s.positive, negative_kind=s.kind, negative_labels=list(s.negatives))
            for s in signals]})
        service._recording_access._montage_snapshots.resolve = lambda *args: view
        recording = replace(recording, stored_name=recording.stored_name.replace("wpf:", "wpf-montage:"),
            channels=tuple(s.name for s in signals))
    with service.open_access_session(recording, artifact_reference_channels=("F3", "AUX_Heart"),
            bad_channel_spec=BadChannelSpec(policy=policy, channels=("f3", "aux_heart"))) as session:
        assert session.applied_bad_channel_spec == BadChannelSpec()
        expected_names = tuple(n+("-AVG" if montage else "") for n in ("F4", "C3", "C4"))
        assert session.channel_names == expected_names
        expected = values[:100, 1:4].copy()
        if montage:
            expected -= expected.mean(axis=1, keepdims=True)
        np.testing.assert_allclose(session.read_samples(0, 100).values_v, expected, atol=1e-18)
    with pytest.raises(ValueError, match="BAD_CHANNEL_NOT_FOUND"):
        service.open_access_session(recording, artifact_reference_channels=("F3",),
            bad_channel_spec=BadChannelSpec(policy="interpolate", channels=("F3", "NO_SUCH_CHANNEL")))


@pytest.mark.parametrize("policy", ["exclude", "interpolate"])
def test_auxiliary_overlap_keeps_unrelated_eeg_bad_policy(tmp_path, policy):
    service, recording, _ = make_recording(tmp_path)
    bad = BadChannelSpec(policy=policy, channels=("AUX_Eye", "C4"))
    with service.open_access_session(recording, artifact_reference_channels=("AUX_Eye",),
            bad_channel_spec=bad) as session:
        assert session.applied_bad_channel_spec == BadChannelSpec(policy=policy, channels=("C4",))
        assert ("C4" in session.channel_names) == (policy == "interpolate")
        assert np.isfinite(session.read_samples(0, 100).values_v).all()
