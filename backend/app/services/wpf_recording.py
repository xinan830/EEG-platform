from __future__ import annotations

import hashlib
import json
import struct
from dataclasses import dataclass
from pathlib import Path

import numpy as np


HEADER = struct.Struct("<qiiq")


class WpfRecordingError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


@dataclass(frozen=True)
class WpfChannel:
    stream_index: int
    label: str
    kind: str
    unit: str


@dataclass(frozen=True)
class WpfRecordingFacts:
    root: Path
    session_id: str
    recording_start_utc: str
    sampling_rate_hz: float
    channels: tuple[WpfChannel, ...]
    eeg_indexes: tuple[int, ...]
    duration_s: float
    sample_count: int
    source_sha256: str


def inspect_wpf_recording(source_directory: str | Path) -> WpfRecordingFacts:
    root = Path(source_directory).expanduser().resolve()
    if not root.is_dir():
        raise WpfRecordingError("WPF_RECORDING_DIRECTORY_MISSING", "WPF 记录目录不存在。")
    manifest_path = root / "manifest.json"
    summary_path = root / "recording-summary.json"
    if not manifest_path.is_file() or not summary_path.is_file():
        raise WpfRecordingError("WPF_RECORDING_INCOMPLETE", "WPF 记录缺少 manifest.json 或 recording-summary.json。")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        summary = json.loads(summary_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise WpfRecordingError("WPF_RECORDING_MANIFEST_INVALID", "WPF 记录清单无法解析。") from exc
    status = str(summary.get("status", summary.get("Status", ""))).casefold()
    if status != "completed":
        raise WpfRecordingError("WPF_RECORDING_NOT_COMPLETE", "只有已完成的 WPF 记录才能注册分析。")
    session_id = str(manifest.get("SessionId", manifest.get("session_id", "")))
    start_utc = str(manifest.get("RecordingStartUtc", manifest.get("recording_start_utc", "")))
    sfreq = float(manifest.get("SamplingRateHz", manifest.get("sampling_rate_hz", 0)))
    channels_raw = manifest.get("Channels", manifest.get("channels", []))
    if not session_id or not start_utc or not np.isfinite(sfreq) or sfreq <= 0 or not isinstance(channels_raw, list):
        raise WpfRecordingError("WPF_RECORDING_MANIFEST_INVALID", "WPF 记录会话、采样率或通道表无效。")
    channels: list[WpfChannel] = []
    for item in channels_raw:
        try:
            channels.append(WpfChannel(
                int(item.get("StreamIndex", item.get("stream_index"))),
                str(item.get("Label", item.get("label")) or ""),
                str(item.get("Kind", item.get("kind"))),
                str(item.get("Unit", item.get("unit")) or ""),
            ))
        except (TypeError, ValueError) as exc:
            raise WpfRecordingError("WPF_RECORDING_MANIFEST_INVALID", "WPF 通道表包含无效字段。") from exc
    if not channels or len({item.stream_index for item in channels}) != len(channels):
        raise WpfRecordingError("WPF_RECORDING_MANIFEST_INVALID", "WPF 通道索引为空或重复。")
    def kind_name(item: WpfChannel) -> str:
        # System.Text.Json serializes the C# enum numerically by default.
        return {1: "reference", 2: "bipolar", 4: "samplecounter"}.get(int(item.kind), item.kind.casefold()) if item.kind.isdigit() else item.kind.casefold()

    eeg = tuple(item.stream_index for item in channels if kind_name(item) in {"reference", "bipolar"} and item.unit == "V")
    counters = [item.stream_index for item in channels if kind_name(item) == "samplecounter" and item.unit == "count"]
    if not eeg or len(counters) != 1:
        raise WpfRecordingError("WPF_RECORDING_CHANNELS_INVALID", "WPF 记录必须包含 V 单位 EEG 通道和唯一 count sample counter。")
    batches = _index_batches(root, len(channels))
    if not batches:
        raise WpfRecordingError("WPF_RECORDING_EMPTY", "WPF 记录没有可读取的样本分块。")
    first = batches[0][0]
    last = max(item[0] + item[1] for item in batches)
    digest = hashlib.sha256()
    # A chunk can contain thousands of batch headers. Hash each source file
    # once; hashing the same chunk once per batch turns registration into an
    # accidental O(batch_count * file_size) operation.
    source_paths = [manifest_path, summary_path, root / "audit.jsonl"]
    source_paths.extend(item[5] for item in batches)
    unique_paths = list(dict.fromkeys(path for path in source_paths if path.is_file()))
    for path in unique_paths:
        digest.update(path.name.encode())
        digest.update(path.read_bytes())
    return WpfRecordingFacts(root, session_id, start_utc, sfreq, tuple(channels), eeg, (last - first) / sfreq, last - first, digest.hexdigest())


def load_wpf_data(facts: WpfRecordingFacts) -> tuple[np.ndarray, float, list[str], list[dict[str, object]]]:
    batches = _index_batches(facts.root, len(facts.channels))
    first = batches[0][0]
    last = max(item[0] + item[1] for item in batches)
    values = np.full((last - first, len(facts.eeg_indexes)), np.nan, dtype=np.float64)
    names = [facts.channels[index].label for index in facts.eeg_indexes]
    for counter, count, channel_count, _ticks, offset, path in batches:
        with path.open("rb") as stream:
            stream.seek(offset)
            payload = np.fromfile(stream, dtype="<f8", count=count * channel_count)
        if payload.size != count * channel_count:
            raise WpfRecordingError("WPF_RECORDING_CHUNK_TRUNCATED", f"WPF 分块读取不完整：{path.name}。")
        matrix = payload.reshape(count, channel_count)
        values[counter - first:counter - first + count, :] = matrix[:, list(facts.eeg_indexes)]
    return values, facts.sampling_rate_hz, names, []


def _index_batches(root: Path, channel_count: int) -> list[tuple[int, int, int, int, int, Path]]:
    result = []
    for path in sorted(root.glob("samples-*.bin")):
        try:
            with path.open("rb") as stream:
                while True:
                    header = stream.read(HEADER.size)
                    if not header:
                        break
                    if len(header) != HEADER.size:
                        raise WpfRecordingError("WPF_RECORDING_CHUNK_TRUNCATED", f"WPF 分块头不完整：{path.name}。")
                    counter, count, actual_channels, ticks = HEADER.unpack(header)
                    if counter < 0 or count <= 0 or actual_channels != channel_count:
                        raise WpfRecordingError("WPF_RECORDING_CHUNK_INVALID", f"WPF 分块元数据无效：{path.name}。")
                    offset = stream.tell()
                    payload_bytes = count * actual_channels * 8
                    if stream.seek(0, 2) < offset + payload_bytes:
                        raise WpfRecordingError("WPF_RECORDING_CHUNK_TRUNCATED", f"WPF 分块数据不完整：{path.name}。")
                    stream.seek(offset + payload_bytes)
                    result.append((counter, count, actual_channels, ticks, offset, path))
        except OSError as exc:
            raise WpfRecordingError("WPF_RECORDING_CHUNK_UNREADABLE", f"WPF 分块无法读取：{path.name}。") from exc
    result.sort(key=lambda item: item[0])
    if any(result[index][0] < result[index - 1][0] + result[index - 1][1] for index in range(1, len(result))):
        raise WpfRecordingError("WPF_RECORDING_COUNTER_OVERLAP", "WPF 样本计数器存在重叠。")
    return result
