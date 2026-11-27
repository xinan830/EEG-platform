"""Live PSD and STFT using the review spectral gateway."""

import numpy as np
from pydantic import BaseModel, Field

from app.scientific.contracts.preprocessing import live_spectral_filter_spec
from app.scientific.contracts.spectrogram import STFT_DB_UNIT, density_uv_to_db
from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY
from app.services.live_analysis.live_analysis_contracts import LiveWindowPlan
from app.services.live_analysis.live_rbp_algorithm import LiveRbpConfig
from app.services.live_analysis.live_recording_access import LiveRecordingAccessError
from app.services.live_analysis.live_scientific_preprocessing import LiveScientificPreprocessingService


class LiveSpectrumResult(BaseModel):
    scientific_identity: str
    implementation_identity: str
    schema_version: str = "live-spectrum-result-v1"
    state: str
    channels: list[str]
    frequencies_hz: list[float]
    unit: str
    values: list[list[float | None]]
    times_s: list[float] = Field(default_factory=list)
    quality: dict = Field(default_factory=dict)
    failure: dict | None = None


def finite_rows(values: np.ndarray) -> list[list[float | None]]:
    return np.where(np.isfinite(values), values, None).tolist()


class LiveSpectrumAlgorithm:
    def __init__(self, algorithm_id: str, preprocessor: LiveScientificPreprocessingService | None = None):
        self.algorithm_id = algorithm_id
        self.scientific_identity = {"psd": "psd-live-welch-v1", "stft": "stft-live-v1"}[algorithm_id]
        self.implementation_identity = "live-spectrum-window-v1"
        self._preprocessor = preprocessor or LiveScientificPreprocessingService()

    def configure(self, source, config):
        requested = LiveRbpConfig.model_validate(config)
        available = {name.casefold(): name for name in source.channel_names}
        try:
            channels = tuple(available[name.strip().casefold()] for name in requested.channels)
        except KeyError as exc:
            raise LiveRecordingAccessError("LIVE_CHANNEL_NOT_FOUND", f"实时通道不存在：{exc.args[0]}") from exc
        if self.algorithm_id == "stft" and len(channels) != 1:
            raise LiveRecordingAccessError("LIVE_CONFIG_INVALID", "实时 STFT 需要选择一个通道。")
        rate = source.sampling_rate_hz
        if requested.window_s < 4 or rate / 2 <= max(50, requested.notch_hz or 0):
            raise LiveRecordingAccessError("LIVE_CONFIG_INVALID", "实时频谱需要至少 4 秒数据，且滤波频率低于奈奎斯特频率。")
        return requested, LiveWindowPlan(channels, round(requested.window_s * rate), max(1, round(requested.step_s * rate)))

    def calculate(self, session_id, block, config, session_first_sample):
        gateway = DEFAULT_SPECTRAL_GATEWAY
        rate = block.sampling_rate_hz
        spec = live_spectral_filter_spec(config.notch_hz)
        prepared = self._preprocessor.prepare_window(session_id, block, spec).values_v
        times = []
        quality = {}
        channels = list(block.channel_names)
        gate_failed = False
        if self.algorithm_id == "psd":
            spectrum = gateway.welch_annotated_channels(prepared, rate, low_hz=1, high_hz=50)
            frequencies, values, unit = spectrum.freqs, spectrum.psd, "V^2/Hz"
            gate_failed = bool(spectrum.gate_failed)
            quality = dict(zip(channels, spectrum.evidence["channel_quality"], strict=True))
            for index, channel in enumerate(channels):
                if quality[channel]["status"] == "unavailable":
                    values[index] = np.nan
        else:
            spectrum = gateway.welch(prepared, rate, low_hz=1, high_hz=50)
            gate_failed = bool(spectrum.gate_failed)
            centers, frequencies, density, _ = gateway.spectrogram(prepared, rate, low_hz=1, high_hz=50)
            times = (centers + (block.start_sample - session_first_sample) / rate).tolist()
            values, unit = density_uv_to_db(density[:, 0, :] * 1e12), STFT_DB_UNIT
        valid = np.isfinite(values)
        state = "unavailable" if gate_failed or not valid.any() else "ready" if valid.all() else "partial"
        return LiveSpectrumResult(
            scientific_identity=self.scientific_identity, implementation_identity=self.implementation_identity,
            state=state, channels=channels, frequencies_hz=frequencies.tolist(), unit=unit,
            values=finite_rows(values) if state != "unavailable" else [], times_s=times, quality=quality,
            failure={"code": "LIVE_SPECTRUM_QUALITY_UNAVAILABLE", "message": "当前窗口没有通过频谱质量检查。"}
            if state == "unavailable" else None,
        )
