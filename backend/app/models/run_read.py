"""Public Run schema: official results expose the current typed contract.

The dictionary branch belongs to non-official spectrum/spectrogram Runs.
Official completed results are validated before serialization, without rewriting
their stored summary or injecting optional fields into the response.
"""

from typing import Any

from pydantic import model_validator

from app.models.current_algorithm_result import CurrentAlgorithmResultSummary
from app.models.run import AnalysisRun


class AnalysisRunRead(AnalysisRun):
    result_summary: dict[str, Any] | CurrentAlgorithmResultSummary | None = None
    analysis_provenance: dict[str, Any]

    @model_validator(mode="after")
    def current_official_summary(self):
        if self.analysis_type == "official_algorithm" and self.status.value == "completed":
            CurrentAlgorithmResultSummary.model_validate(self.result_summary)
        return self
