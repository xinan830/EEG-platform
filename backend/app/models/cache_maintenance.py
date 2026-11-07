"""Current Settings cache-maintenance HTTP contract; no caller-supplied paths."""

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field


class CacheClearRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")


class CacheClearResponse(BaseModel):
    status: Literal["cleared"] = "cleared"
    deleted_sessions: int = Field(default=0, ge=0)
    deleted_windows: int = Field(default=0, ge=0)
    deleted_attempts: int = Field(default=0, ge=0)
    deleted_subscriptions: int = Field(default=0, ge=0)
    deleted_files: int = Field(default=0, ge=0)
    freed_bytes: int = Field(default=0, ge=0)
