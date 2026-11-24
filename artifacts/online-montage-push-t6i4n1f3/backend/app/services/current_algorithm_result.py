"""Validate the single supported persisted official result before consumption."""

from pydantic import ValidationError
from app.models.current_algorithm_result import CurrentAlgorithmResultSummary
from app.models.run import AnalysisRun, RunStatus


class InvalidCurrentResultError(Exception):
    """A completed official result violates the supported schema."""


async def invalid_current_result_handler(request, exc):
    from app.core.api_contract import error_response

    return error_response(request, 409, "ALGORITHM_RESULT_INVALID", str(exc))


def require_current_result(run: AnalysisRun) -> None:
    if run.analysis_type == "official_algorithm" and run.status is RunStatus.COMPLETED:
        try:
            CurrentAlgorithmResultSummary.model_validate(run.result_summary)
        except ValidationError as exc:
            raise InvalidCurrentResultError("Result violates the current official algorithm contract") from exc
