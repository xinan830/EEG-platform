"""Recording-bound scalp geometry in head-frame meters; never derived from diagrams."""
from __future__ import annotations

import hashlib
import json
import math
from collections.abc import Mapping

import mne
import numpy as np

from app.scientific.contracts.preprocessing import STANDARD_MONTAGE_NAME


def validate_geometry(payload: object) -> dict[str, object]:
    """Validate a supplied geometry without unit, frame or electrode-name guessing."""
    def invalid(detail: str):
        raise ValueError(f"ELECTRODE_GEOMETRY_INVALID: {detail}")

    if not isinstance(payload, dict):
        invalid("坐标快照必须为对象")
    if (payload.get("schema_version") != "recording-electrode-geometry-v1"
            or payload.get("coordinate_unit") != "m" or payload.get("coordinate_frame") != "head"):
        invalid("坐标必须声明 v1 契约、head 坐标系和米单位")
    for key in ("source_id", "coordinate_transform"):
        if not isinstance(payload.get(key), str) or not payload[key].strip() or len(payload[key]) > 2048:
            invalid(f"缺少或无效的坐标来源字段 {key}")
    if not isinstance(payload.get("source_kind"), str) or payload["source_kind"] not in {"source_provided", "standard_template", "measured"}:
        invalid("坐标来源类型无效")
    digest = payload.get("source_sha256")
    if not isinstance(digest, str) or len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
        invalid("坐标来源 SHA256 无效")
    entries = payload.get("electrodes")
    if not isinstance(entries, list) or not entries or len(entries) > 4096:
        invalid("坐标电极表为空或超限")
    labels, natives, positions, normalized = set(), set(), set(), []
    for row in entries:
        if not isinstance(row, dict):
            invalid("电极坐标条目无效")
        label, native, xyz = row.get("label"), row.get("native_channel_index"), row.get("position_m")
        if (not isinstance(label, str) or not label.strip() or label != label.strip()
                or type(native) is not int or native < 0 or native in natives or label.casefold() in labels):
            invalid("电极标签或原始通道索引重复/无效")
        if (not isinstance(xyz, (list, tuple)) or len(xyz) != 3
                or any(type(v) not in (int, float) or not math.isfinite(v) for v in xyz)):
            invalid(f"电极 {label} 必须具有三个有限坐标")
        # Head-frame distances are meters, not unit-sphere or millimeter coordinates.
        if not 0.005 <= math.hypot(*xyz) <= 0.5:
            invalid(f"电极 {label} 坐标尺度无效，请核对单位")
        position_key = tuple(round(float(v), 9) for v in xyz)
        if position_key in positions:
            invalid("多个输入不能占用同一科学电极坐标")
        labels.add(label.casefold())
        natives.add(native)
        positions.add(position_key)
        normalized.append(dict(label=label, native_channel_index=native, position_m=[float(v) for v in xyz]))
    return {key: payload[key] for key in (
        "schema_version", "coordinate_unit", "coordinate_frame", "source_id",
        "source_kind", "source_sha256", "coordinate_transform",
    )} | {"electrodes": sorted(normalized, key=lambda row: row["native_channel_index"])}


def geometry_from_channel_snapshot(snapshot: Mapping[str, object]) -> dict[str, object] | None:
    """Bind optional geometry to immutable native input identities, not display order."""
    if "ElectrodeGeometry" not in snapshot:
        return None
    geometry = validate_geometry(snapshot["ElectrodeGeometry"])
    channels = snapshot.get("Channels")
    if not isinstance(channels, list) or any(not isinstance(row, dict) for row in channels):
        raise ValueError("ELECTRODE_GEOMETRY_INVALID: 缺少原始通道绑定")
    if any(type(row.get("NativeChannelIndex")) is not int for row in channels):
        raise ValueError("ELECTRODE_GEOMETRY_INVALID: 原始通道索引类型无效")
    native = {row["NativeChannelIndex"]: row for row in channels}
    if len(native) != len(channels):
        raise ValueError("ELECTRODE_GEOMETRY_INVALID: 原始通道索引重复")
    for entry in geometry["electrodes"]:
        bound = native.get(entry["native_channel_index"], {})
        if (str(bound.get("ElectrodeLabel", "")).strip().casefold() != entry["label"].casefold()
                or type(bound.get("ExpectedKind")) is not int or bound["ExpectedKind"] != 1):
            raise ValueError("ELECTRODE_GEOMETRY_INVALID: 三维电极与原始单极通道身份不匹配")
    return geometry


def geometry_fingerprint(geometry: Mapping[str, object]) -> str:
    payload = json.dumps(validate_geometry(dict(geometry)), sort_keys=True, separators=(",", ":"), allow_nan=False)
    return hashlib.sha256(payload.encode()).hexdigest()


def geometry_montage(geometry: Mapping[str, object] | None, *, montage_name: str = STANDARD_MONTAGE_NAME):
    """Use all supplied custom sites or the existing template, never a mixture."""
    if geometry is None:
        return mne.channels.make_standard_montage(montage_name)
    validated = validate_geometry(dict(geometry))
    return mne.channels.make_dig_montage(
        ch_pos={row["label"]: row["position_m"] for row in validated["electrodes"]}, coord_frame="head",
    )


def geometry_positions(geometry: Mapping[str, object] | None, *, montage_name: str = STANDARD_MONTAGE_NAME):
    positions = geometry_montage(geometry, montage_name=montage_name).get_positions()["ch_pos"]
    return {label.casefold(): (label, np.asarray(xyz, dtype=np.float64)) for label, xyz in positions.items()}
