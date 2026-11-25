"""Local research hierarchy and explicitly optional subject metadata contracts."""

from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator


class ProjectCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    name: str = Field(min_length=1, max_length=160)
    description: str = Field(default="", max_length=4000)


class DesktopProjectEnsureRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    name: str = Field(min_length=1, max_length=160)
    description: str = Field(default="", max_length=4000)


class SubjectCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    local_code: str | None = Field(default=None, max_length=160)
    name: str = Field(default="", max_length=160)


class ConditionCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    code: str = Field(min_length=1, max_length=160)
    label: str = Field(default="", max_length=4000)


class SessionCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    subject_id: str | None = Field(default=None, min_length=1)
    recording_id: str = Field(min_length=1)
    condition_id: str | None = None
    label: str = Field(default="", max_length=4000)


class SessionRenameRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    label: str = Field(min_length=1, max_length=4000)
    subject_code: str | None = Field(default=None, max_length=160)
    subject_name: str | None = Field(default=None, max_length=160)

    @model_validator(mode="after")
    def validate_subject_pair(self):
        if len({"subject_code", "subject_name"} & self.model_fields_set) == 1:
            raise ValueError("subject code and name must be submitted together")
        return self

    @field_validator("label")
    @classmethod
    def validate_label(cls, value: str) -> str:
        if not value.strip():
            raise ValueError("recording name must not be blank")
        return value.strip()


class Project(BaseModel):
    project_id: str
    name: str
    description: str
    created_at: str
    updated_at: str


class Subject(BaseModel):
    subject_id: str
    project_id: str
    local_code: str | None
    created_at: str
    name: str = ""


class Condition(BaseModel):
    condition_id: str
    project_id: str
    code: str
    label: str
    created_at: str


class Session(BaseModel):
    session_id: str
    project_id: str
    subject_id: str | None
    recording_id: str
    condition_id: str | None = None
    label: str
    created_at: str
    subject_code: str | None = None
    subject_name: str = ""


class ProjectRecordingDeletionResponse(BaseModel):
    session_id: str
    recording_id: str
    recording_deleted: bool
