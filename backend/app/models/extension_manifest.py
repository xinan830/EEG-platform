"""Non-executable governance metadata for local research modules."""

import re
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator


MODULE_ID_PATTERN = re.compile(r"^[a-z][a-z0-9-]{2,79}$")
ModuleSource = Literal["local_official", "research_template", "user_private"]
ExecutionMode = Literal["closed_definition_graph", "metadata_only"]
ValidationState = Literal["draft", "engineering_verified", "research_use", "deprecated"]
DeclaredPermission = Literal["read_recording_metadata", "read_derived_artifacts", "write_derived_artifacts"]


class ExtensionManifest(BaseModel):
    """A declaration only; it cannot transport or load executable code."""

    model_config = ConfigDict(extra="forbid")

    schema_version: Literal["local-module-manifest-v1"] = "local-module-manifest-v1"
    module_id: str = Field(min_length=3, max_length=80)
    name: str = Field(min_length=1, max_length=160)
    source: ModuleSource
    description: str = Field(default="", max_length=4000)
    platform_compatibility: str = Field(pattern=r"^brain-platform-api>=[0-9]+\.[0-9]+\.[0-9]+,<[0-9]+\.[0-9]+\.[0-9]+$")
    validation_state: ValidationState = "draft"
    execution_mode: ExecutionMode = "closed_definition_graph"
    permissions: list[DeclaredPermission] = Field(default_factory=list)
    input_types: list[str] = Field(min_length=1)
    output_types: list[str] = Field(min_length=1)

    @field_validator("module_id")
    @classmethod
    def validate_module_id(cls, value: str) -> str:
        if not MODULE_ID_PATTERN.fullmatch(value):
            raise ValueError("module_id must be lowercase kebab-case")
        return value

    @field_validator("permissions", "input_types", "output_types")
    @classmethod
    def reject_duplicates(cls, values: list[str]) -> list[str]:
        if len(set(values)) != len(values):
            raise ValueError("manifest lists must not contain duplicates")
        return values
