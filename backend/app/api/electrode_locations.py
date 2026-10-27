"""Read-only standard electrode-location resolution API."""

from typing import Literal

from fastapi import APIRouter, Request
from pydantic import BaseModel, ConfigDict, Field

from app.core.api_contract import error_response
from app.services.electrode_locations import resolve_electrode_labels


router = APIRouter(prefix="/api/electrode-locations", tags=["electrode-locations"])


class ElectrodeLocationRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    labels: list[str] = Field(min_length=1, max_length=128)


class ElectrodeLocationItem(BaseModel):
    requested_label: str
    status: Literal["available", "unavailable"]
    canonical_site: str | None
    coordinates: list[float] | None
    coordinate_frame: Literal["mri"] | None
    coordinate_unit: Literal["m"] | None
    source_id: Literal["mne.standard_1020"]
    source_version: str


class ElectrodeLocationResponse(BaseModel):
    montage: Literal["standard_1020"] = "standard_1020"
    locations: list[ElectrodeLocationItem]


@router.post("/resolve", response_model=ElectrodeLocationResponse)
def resolve(payload: ElectrodeLocationRequest, request: Request) -> dict:
    try:
        locations = resolve_electrode_labels(payload.labels)
    except RuntimeError as error:
        return error_response(request, 503, "ELECTRODE_LOCATION_SOURCE_UNAVAILABLE", str(error))
    return {"montage": "standard_1020", "locations": locations}
