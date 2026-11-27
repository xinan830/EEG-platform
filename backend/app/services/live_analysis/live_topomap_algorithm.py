"""Project live spectral windows with the same fields and grid as review."""

from pydantic import BaseModel, ConfigDict, Field, FiniteFloat, model_validator

from app.models.topomap import TopomapField
from app.services.recordings.acquisition_analysis_signals import _materialize_template_positions
from app.services.live_analysis.live_rbp_algorithm import LiveRbpAlgorithm, LiveRbpConfig
from app.services.live_analysis.live_recording_access import LiveRecordingAccessError
from app.services.live_analysis.live_scientific_preprocessing import LiveScientificPreprocessingService
from app.services.live_analysis.live_spectral_algorithms import LiveSpectrumAlgorithm
from app.services.analysis.results.result_scalar_inputs import RESULT_SCALAR_ADAPTERS
from app.services.analysis.results.topomap import TopomapService


class LiveElectrodePosition(BaseModel):
    model_config = ConfigDict(extra="forbid")
    x: FiniteFloat | None = None
    y: FiniteFloat | None = None
    enabled: bool = True


class LiveMapRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    spectrum: LiveRbpConfig
    field_id: str | None = None
    field_ids: list[str] | None = Field(default=None, min_length=1, max_length=15)
    positions: dict[str, LiveElectrodePosition] = Field(default_factory=dict, max_length=256)

    @model_validator(mode="after")
    def require_fields(self) -> "LiveMapRequest":
        if not self.field_ids and not self.field_id:
            raise ValueError("实时地形图至少需要一个字段。")
        return self


class LiveMapConfig(BaseModel):
    spectrum: BaseModel
    field_ids: tuple[str, ...]
    positions: dict


class LiveMapResult(BaseModel):
    schema_version: str = "live-topomap-result-v1"
    scientific_identity: str
    implementation_identity: str = "live-topomap-window-v1"
    state: str
    field: TopomapField | None = None
    fields: list[TopomapField] = Field(default_factory=list)
    failure: dict | None = None


class LiveTopomapAlgorithm:
    def __init__(self, metric: str, preprocessor: LiveScientificPreprocessingService | None = None):
        self.algorithm_id = f"{metric}-topomap"
        self.spectrum = (
            LiveRbpAlgorithm(preprocessor=preprocessor)
            if metric == "rbp"
            else LiveSpectrumAlgorithm("psd", preprocessor=preprocessor)
        )
        self.scientific_identity = self.spectrum.scientific_identity
        self.implementation_identity = "live-topomap-window-v1"
        self.adapter = RESULT_SCALAR_ADAPTERS[metric]
        self.projector = TopomapService(None, None)

    def configure(self, source, config):
        requested = LiveMapRequest.model_validate(config)
        spectrum, plan = self.spectrum.configure(source, requested.spectrum.model_dump())
        requested_field_ids = tuple(requested.field_ids or [requested.field_id])
        if self.spectrum.algorithm_id == "rbp":
            allowed = {f"rbp.{kind}.{band}" for kind in ("relative", "absolute", "amplitude") for band in self.adapter.metrics}
        else:
            allowed = {f"psd.band-mean-density.{band}" for band in self.adapter.bands}
        if any(field_id not in allowed for field_id in requested_field_ids):
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
        return LiveMapConfig(spectrum=spectrum, field_ids=requested_field_ids, positions=positions), plan

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
        groups: dict[str, list[str]] = {}
        for field_id in config.field_ids:
            group = field_id.split(".")[1] if self.spectrum.algorithm_id == "rbp" else "psd"
            groups.setdefault(group, []).append(field_id)
        fields_by_id: dict[str, TopomapField] = {}
        for field_ids in groups.values():
            spatial_input = self.adapter.build_input(
                recording_id=session_id, payload=payload, field_ids=field_ids,
                window_start_s=(block.start_sample - session_first_sample) / block.sampling_rate_hz,
                window_end_s=(block.end_sample - session_first_sample) / block.sampling_rate_hz,
            )
            response = self.projector.project(
                session_id, spatial_input, signal_provenance={"map_positions": config.positions},
            )
            fields_by_id.update({field.field_id: field for field in response.fields})
        fields = [fields_by_id[field_id] for field_id in config.field_ids]
        field = fields[0] if fields else None
        return LiveMapResult(scientific_identity=self.scientific_identity, state=field.status if field else "unavailable",
                             field=field, fields=fields, failure=field.failure if field else None)
