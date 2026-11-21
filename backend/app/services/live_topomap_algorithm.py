"""Project live spectral windows with the same fields and grid as review."""

from pydantic import BaseModel, ConfigDict, Field, FiniteFloat

from app.models.topomap import TopomapField
from app.services.acquisition_analysis_signals import _materialize_template_positions
from app.services.live_rbp_algorithm import LiveRbpAlgorithm, LiveRbpConfig
from app.services.live_recording_access import LiveRecordingAccessError
from app.services.live_spectral_algorithms import LiveSpectrumAlgorithm
from app.services.result_scalar_inputs import RESULT_SCALAR_ADAPTERS
from app.services.topomap import TopomapService


class LiveElectrodePosition(BaseModel):
    model_config = ConfigDict(extra="forbid")
    x: FiniteFloat | None = None
    y: FiniteFloat | None = None
    enabled: bool = True


class LiveMapRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    spectrum: LiveRbpConfig
    field_id: str
    positions: dict[str, LiveElectrodePosition] = Field(default_factory=dict, max_length=256)


class LiveMapConfig(BaseModel):
    spectrum: BaseModel
    field_id: str
    positions: dict


class LiveMapResult(BaseModel):
    schema_version: str = "live-topomap-result-v1"
    scientific_identity: str
    implementation_identity: str = "live-topomap-window-v1"
    state: str
    field: TopomapField | None = None
    failure: dict | None = None


class LiveTopomapAlgorithm:
    def __init__(self, metric: str):
        self.algorithm_id = f"{metric}-topomap"
        self.spectrum = {"rbp": LiveRbpAlgorithm, "psd": lambda: LiveSpectrumAlgorithm("psd")}[metric]()
        self.scientific_identity = self.spectrum.scientific_identity
        self.implementation_identity = "live-topomap-window-v1"
        self.adapter = RESULT_SCALAR_ADAPTERS[metric]
        self.projector = TopomapService(None, None)

    def configure(self, source, config):
        requested = LiveMapRequest.model_validate(config)
        spectrum, plan = self.spectrum.configure(source, requested.spectrum.model_dump())
        if self.spectrum.algorithm_id == "rbp":
            allowed = {f"rbp.{kind}.{band}" for kind in ("relative", "absolute", "amplitude") for band in self.adapter.metrics}
        else:
            allowed = {f"psd.band-mean-density.{band}" for band in self.adapter.bands}
        if requested.field_id not in allowed:
            raise LiveRecordingAccessError("LIVE_TOPOMAP_FIELD_INVALID", "实时地形图字段无效。")
        requested_positions = {name.casefold(): pos for name, pos in requested.positions.items()}
        positions = {}
        for channel in plan.channels:
            entry = requested_positions.get(channel.casefold())
            if entry is None:
                continue
            position = entry.model_dump()
            position["status"] = "available" if entry.x is not None and entry.y is not None else "unavailable"
            positions[channel] = position
        _materialize_template_positions(positions)
        return LiveMapConfig(spectrum=spectrum, field_id=requested.field_id, positions=positions), plan

    def calculate(self, session_id, block, config, session_first_sample):
        spectrum = self.spectrum.calculate(session_id, block, config.spectrum, session_first_sample)
        if spectrum.state == "unavailable":
            return LiveMapResult(scientific_identity=self.scientific_identity, state="unavailable", failure=spectrum.failure)
        quality = {"extensions": {"channel_quality": {"by_channel": spectrum.quality}}}
        payload = {"channel_order": spectrum.channels, "evidence": quality}
        if self.spectrum.algorithm_id == "rbp":
            payload.update(arrays=spectrum.values, array_units=spectrum.units)
        else:
            payload.update(
                axes={"frequency_hz": spectrum.frequencies_hz}, axis_units={"frequency_hz": "Hz"},
                arrays={"psd": spectrum.values}, array_units={"psd": spectrum.unit},
            )
        spatial_input = self.adapter.build_input(
            recording_id=session_id, payload=payload, field_ids=[config.field_id],
            window_start_s=(block.start_sample - session_first_sample) / block.sampling_rate_hz,
            window_end_s=(block.end_sample - session_first_sample) / block.sampling_rate_hz,
        )
        response = self.projector.project(
            session_id, spatial_input, signal_provenance={"map_positions": config.positions},
        )
        field = response.fields[0]
        return LiveMapResult(scientific_identity=self.scientific_identity, state=field.status, field=field, failure=field.failure)
