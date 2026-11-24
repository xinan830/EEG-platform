"""本地审计查询 API；身份权限接入前仅用于开发验证。"""

from fastapi import APIRouter, Query, Request
from pydantic import BaseModel


router = APIRouter(prefix="/api/audit", tags=["audit"])


class AuditEventResponse(BaseModel):
    id: str
    occurred_at: str
    action: str
    outcome: str
    recording_id: str | None
    session_id: str | None
    request_id: str
    actor_id: str | None
    parameters: dict[str, object]


@router.get("/events", response_model=list[AuditEventResponse])
def list_audit_events(request: Request, recording_id: str | None = None, limit: int = Query(100, ge=1, le=1000)):
    events = request.app.state.audit_service.list_events(recording_id=recording_id, limit=limit)
    for event in events:
        event.pop("parameters_json", None)
    return events
