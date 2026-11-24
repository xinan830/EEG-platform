"""Typed contracts for ephemeral, acquisition-scoped live algorithms."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal, Protocol

from pydantic import BaseModel, ConfigDict, Field, JsonValue

from app.services.live_recording_access import LiveRecordingAccessError, LiveRecordingAccessSession
from app.services.sample_access_contract import SampleBlock


@dataclass(frozen=True)
class LiveWindowPlan:
    channels: tuple[str, ...]
    window_samples: int
    step_samples: int


class LiveAnalysisEnvelope(BaseModel):
    model_config = ConfigDict(extra="forbid")

    type: Literal["live_analysis_update"] = "live_analysis_update"
    schema_version: Literal["live-analysis-update-v2"] = "live-analysis-update-v2"
    acquisition_session_id: str
    algorithm_id: str
    revision: int = Field(ge=0)
    montage_revision: int | None = Field(default=None, ge=0)
    state: Literal["warming_up", "ready", "partial", "unavailable", "overloaded"]
    skipped_windows: int = Field(default=0, ge=0)
    scientific_identity: str | None = None
    implementation_identity: str | None = None
    start_sample: int | None = None
    end_sample: int | None = None
    start_s: float | None = None
    end_s: float | None = None
    payload: JsonValue | None = None
    failure: dict[str, JsonValue] | None = None


class LiveAnalysisAlgorithm(Protocol):
    algorithm_id: str

    def configure(
        self, source: LiveRecordingAccessSession, config: dict[str, JsonValue]
    ) -> tuple[BaseModel, LiveWindowPlan]: ...

    def calculate(
        self,
        session_id: str,
        block: SampleBlock,
        config: BaseModel,
        session_first_sample: int,
    ) -> BaseModel: ...


class LiveAnalysisAlgorithmRegistry:
    """Resolves only algorithms explicitly registered for live execution."""

    def __init__(self, algorithms: tuple[LiveAnalysisAlgorithm, ...] = ()) -> None:
        self._algorithms: dict[str, LiveAnalysisAlgorithm] = {}
        for algorithm in algorithms:
            self.register(algorithm)

    def register(self, algorithm: LiveAnalysisAlgorithm) -> None:
        algorithm_id = algorithm.algorithm_id.strip().casefold()
        if not algorithm_id or algorithm_id in self._algorithms:
            raise ValueError("实时算法标识不能为空或重复")
        self._algorithms[algorithm_id] = algorithm

    def resolve(self, algorithm_id: str) -> LiveAnalysisAlgorithm:
        try:
            return self._algorithms[algorithm_id.casefold()]
        except KeyError as exc:
            raise LiveRecordingAccessError(
                "LIVE_ALGORITHM_UNSUPPORTED", f"实时算法未注册：{algorithm_id}"
            ) from exc
