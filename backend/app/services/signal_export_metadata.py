"""EDF header representation and raw-recording export provenance (uV, samples)."""
from decimal import Decimal, ROUND_CEILING, ROUND_FLOOR
import json
from pathlib import Path


def header_bound(value: float, *, upper: bool) -> int:
    """Use outward integer uV bounds: EDFlib's decimal formatter can truncate binary floats."""
    rounded = int(Decimal(str(value)).to_integral_value(rounding=ROUND_CEILING if upper else ROUND_FLOOR))
    if len(str(rounded)) <= 8:
        return rounded
    raise ValueError("源信号范围超出 EDF/BDF 物理量头字段可表示范围。")


def channel_labels(channels: list[dict]) -> list[str]:
    def valid(label):
        return isinstance(label, str) and bool(label.strip()) and len(label) <= 16 and all(
            32 <= ord(character) <= 126 for character in label)
    reserved = {item["Label"].casefold() for item in channels if valid(item.get("Label"))}
    used, labels = set(), []
    for index, item in enumerate(channels):
        label = item.get("Label")
        if not valid(label) or label.casefold() in used:
            alias = index + 1
            label = f"CH{alias:04d}"
            while label.casefold() in reserved | used:
                alias += 1
                label = f"CH{alias:04d}"
        used.add(label.casefold())
        labels.append(label)
    return labels


def recording_events(root: Path, session_id: str, count: int) -> list[dict]:
    path = root / "events.json"
    if not path.exists():
        return []
    payload = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(payload, dict) or payload.get("SchemaVersion", 0) not in (0, 1, 2) or not isinstance(payload.get("Items"), list):
        raise ValueError("不支持的事件文件结构或版本。")
    result = []
    for item in payload.get("Items", []):
        if item.get("CoordinateStatus", 0) not in (0, "Resolved"):
            continue
        if str(item.get("RecordingId", "")).replace("-", "").casefold() != session_id.replace("-", "").casefold():
            continue
        start = int(item["StartSample"])
        duration = int(item["DurationSamples"])
        if not 0 <= start < count or duration < 0:
            continue
        name = item["DefinitionSnapshot"]["Name"]
        # EDFlib limits UTF-8 descriptions to 40 bytes; never silently truncate.
        if not isinstance(name, str) or not name or len(name.encode("utf-8")) > 40 or any(c in name for c in "\x00\x14\x15"):
            raise ValueError("事件名称无法完整写入 EDF/BDF（UTF-8 最多 40 字节）。")
        result.append({"start_sample": start, "duration_samples": min(duration, count - start), "name": name})
    return sorted(result, key=lambda item: item["start_sample"])


def hardware_metadata(manifest: dict) -> dict:
    hardware = manifest.get("HardwareConfiguration", {})
    allowed = {"hardware_reference_electrode_location", "hardware_ground_electrode_location",
               "channel_configuration_snapshot_json", "acquisition_montage_snapshot_json",
               "montage_configuration_snapshot_json", "channel_configuration_id", "channel_configuration_name",
               "montage_configuration_id", "montage_configuration_name", "hardware_reference_role",
               "hardware_ground_role", "reference_range_v", "bipolar_range_v", "sdk_version", "vendor", "model",
               "sampling_rate_hz", "reference", "ground"}

    def sanitize(value):
        if isinstance(value, dict):
            return {key: sanitize(item) for key, item in value.items() if
                    key.replace("_", "").casefold() not in
                    {"serialnumber", "deviceinstanceid", "devicedisplayname", "patient", "subject"}}
        if isinstance(value, list):
            return [sanitize(item) for item in value]
        return value

    result = {}
    for key, value in hardware.items():
        if key in allowed:
            if key.endswith("_json") and isinstance(value, str):
                value = json.loads(value)
            result[key] = sanitize(value)
    return result
