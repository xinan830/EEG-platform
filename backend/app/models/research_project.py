"""Local, non-identifying research hierarchy API contracts."""

from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field


class ProjectCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    name: str = Field(min_length=1, max_length=160)
    description: str = Field(default="", max_length=4000)


class SubjectCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    local_code: str = Field(min_length=1, max_length=160)


class ConditionCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    code: str = Field(min_length=1, max_length=160)
    label: str = Field(default="", max_length=4000)


class SessionCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    subject_id: str = Field(min_length=1)
    recording_id: str = Field(min_length=1)
    condition_id: str | None = None
    label: str = Field(default="", max_length=4000)


class Project(BaseModel):
    project_id: str
    name: str
    description: str
    created_at: str
    updated_at: str


class Subject(BaseModel):
    subject_id: str
    project_id: str
    local_code: str
    created_at: str


class Condition(BaseModel):
    condition_id: str
    project_id: str
    code: str
    label: str
    created_at: str


class Session(BaseModel):
    session_id: str
    project_id: str
    subject_id: str
    recording_id: str
    condition_id: str | None = None
    label: str
    created_at: str
