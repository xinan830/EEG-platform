from __future__ import annotations

from typing import Literal

from pydantic import model_validator

from app.algorithm_runtime.contracts import AlgorithmConfigBase


class ThetaBetaConfig(AlgorithmConfigBase):
    """Single raw-channel TBR configuration with an explicit band definition."""

    frequency_mode: Literal["standard", "individualized"] = "standard"

    @model_validator(mode="after")
    def validate_dynamic_window(self) -> "ThetaBetaConfig":
        config = self
        if config.mode == "dynamic" and config.window_s not in (20.0, 30.0):
            raise ValueError("Theta/Beta 动态窗口仅支持 20 或 30 秒")
        return config
