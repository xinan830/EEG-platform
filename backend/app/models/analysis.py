from dataclasses import dataclass


@dataclass(frozen=True)
class AnalysisSummary:
    analysis_id: str
    recording_id: str
    status: str
    result_url: str
    locked_iapf: float | None = None
