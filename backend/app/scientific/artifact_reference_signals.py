"""Aligned voltage references kept outside EEG fitting and output matrices."""
from dataclasses import dataclass

import mne
import numpy as np


@dataclass(frozen=True)
class ArtifactReferenceSignals:
    values_v: np.ndarray
    channel_names: tuple[str, ...]
    eog_channels: tuple[str, ...]
    ecg_channels: tuple[str, ...]

    def validate(self, sample_count: int) -> None:
        if self.values_v.shape != (sample_count, len(self.channel_names)):
            raise ValueError("ARTIFACT_REFERENCE_ALIGNMENT_INVALID: 辅助信号样本轴不一致")
        if len({name.casefold() for name in self.channel_names}) != len(self.channel_names):
            raise ValueError("ARTIFACT_REFERENCE_INVALID: 辅助通道重名")
        roles = (*self.eog_channels, *self.ecg_channels)
        if len(set(roles)) != len(roles) or set(roles) != set(self.channel_names):
            raise ValueError("ARTIFACT_REFERENCE_INVALID: 辅助通道用途与数据列不一致")

    def scoring_raw(self, eeg_values, eeg_names, sfreq, finite_rows, *, minimum_seconds=5):
        """Append references only to scoring Raw, with unique names and non-EEG types."""
        self.validate(len(eeg_values))
        mask = finite_rows & np.all(np.isfinite(self.values_v), axis=1)
        if mask.sum() < max(64, int(minimum_seconds * sfreq)):
            raise ValueError(f"ARTIFACT_REFERENCE_INSUFFICIENT_SAMPLES: 辅助信号共同有效数据不足 {minimum_seconds} 秒")
        prefix = "__artifact_reference_"
        while any(name.startswith(prefix) for name in eeg_names):
            prefix = "_" + prefix
        internal = tuple(f"{prefix}{i}" for i in range(len(self.channel_names)))
        types = ["eog" if name in self.eog_channels else "ecg" for name in self.channel_names]
        info = mne.create_info([*eeg_names, *internal], sfreq, ["eeg"] * len(eeg_names) + types)
        values = np.column_stack((eeg_values[mask], self.values_v[mask]))
        return mne.io.RawArray(np.ascontiguousarray(values.T), info, verbose=False), dict(zip(self.channel_names, internal))
