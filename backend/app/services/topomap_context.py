"""Resolve the immutable signal source carried by a dynamic EEG window."""

from __future__ import annotations

import hashlib
import json
from typing import Any


class DynamicTopomapContextError(ValueError):
    """Raised when a dynamic window has no trustworthy spatial source context."""


def resolve_dynamic_signal_provenance(payload: dict[str, Any], channel_order: list[str]) -> dict[str, Any]:
    configuration_json = payload.get("scientific_configuration_json")
    identity = payload.get("identity") or {}
    if not isinstance(configuration_json, str):
        raise DynamicTopomapContextError("动态窗口缺少科学配置快照。")
    digest = hashlib.sha256(configuration_json.encode("utf-8")).hexdigest()
    if digest != identity.get("configuration_sha256"):
        raise DynamicTopomapContextError("动态窗口科学配置身份校验失败。")
    try:
        configuration = json.loads(configuration_json)
    except json.JSONDecodeError as exc:
        raise DynamicTopomapContextError("动态窗口科学配置不是有效 JSON。") from exc

    context = configuration.get("context") if isinstance(configuration, dict) else None
    signal_mapping = context.get("signal_mapping") if isinstance(context, dict) else None
    if not isinstance(signal_mapping, dict) or signal_mapping.get("channels") != channel_order:
        raise DynamicTopomapContextError("动态窗口导联顺序与 RBP 结果不一致。")
    source = signal_mapping.get("signal_source")
    if not isinstance(source, dict):
        raise DynamicTopomapContextError("动态窗口缺少已验证的信号来源快照。")

    montage = signal_mapping.get("analysis_montage")
    if source.get("signal_mode") == "acquisition_montage" and not isinstance(montage, dict):
        raise DynamicTopomapContextError("动态窗口缺少分析导联快照身份。")
    if montage is not None:
        identities = (
            ("montage_id", "id"),
            ("montage_revision", "revision"),
            ("montage_fingerprint", "fingerprint"),
        )
        if any(
            not source.get(source_key) or not montage.get(montage_key)
            or str(source.get(source_key)) != str(montage.get(montage_key))
            for source_key, montage_key in identities
        ):
            raise DynamicTopomapContextError("动态窗口导联快照身份不一致。")

    if not isinstance(source.get("map_positions", {}), dict) or not isinstance(source.get("formulas", []), list):
        raise DynamicTopomapContextError("动态窗口空间来源数据结构无效。")
    return {**source, "map_positions": source.get("map_positions", {}), "formulas": source.get("formulas", [])}
