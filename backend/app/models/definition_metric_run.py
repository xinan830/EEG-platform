"""Validated static execution configuration for a saved user metric."""

from pydantic import BaseModel, Field

from app.models.analysis_config import AnalysisTimeRange


class DefinitionMetricConfig(BaseModel):
    channel: str = Field(min_length=1, max_length=160)
    time: AnalysisTimeRange
