"""Bounded whole raw WPF recording export using pyEDFlib; source files stay immutable."""
from dataclasses import dataclass
from datetime import datetime, timezone
from fractions import Fraction
import json
import logging
import math
from pathlib import Path
import shutil
import tempfile
from threading import Event, Lock
import warnings
from zipfile import ZipFile, ZIP_STORED

import numpy as np
import pyedflib

from app.services.recording_samples import WpfSampleReader
from app.models.recording import RecordingSummary
from app.services.signal_export_metadata import channel_labels, header_bound, hardware_metadata, recording_events
from app.services.wpf_recording import inspect_wpf_recording, wpf_source_signature

logger = logging.getLogger(__name__)
BLOCK_SAMPLES = 65536


class SignalExportError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


@dataclass
class SignalExportPackage:
    directory: Path
    gate: Lock
    closed: bool = False

    @property
    def path(self) -> Path:
        return self.directory / "export.zip"

    def close(self) -> None:
        if not self.closed:
            self.closed = True
            try:
                shutil.rmtree(self.directory)
            finally:
                self.gate.release()


def record_layout(count: int, rate: float, channel_count: int, bytes_per_sample: int) -> tuple[int, float]:
    """Choose an exact duration and divisor: never pad samples or round sample rate."""
    frequency = Fraction(str(rate))
    # EDFlib uses 10-us ticks and a fixed-point eight-byte duration field.
    quantum = frequency.numerator // math.gcd(frequency.numerator, 100000 * frequency.denominator)
    maximum = min(count, BLOCK_SAMPLES, int(rate * 60), 8 * 1024 * 1024 // (channel_count * 8),
                  (10 * 1024 * 1024 - 8000) // (channel_count * bytes_per_sample))
    for samples in range(maximum // quantum * quantum, 0, -quantum):
        if count % samples:
            continue
        duration = Fraction(samples, 1) / frequency
        text = format(float(duration), ".5f").rstrip("0").rstrip(".")
        if duration >= Fraction(1, 1000) and len(text) <= 8 and Fraction(text) == duration:
            return samples, float(duration)
    raise SignalExportError("RECORDING_EXPORT_TIME_UNREPRESENTABLE", "样本总数与采样率无法无填充地表示为连续 EDF/BDF。")


class RecordingSignalExporter:
    def __init__(self, output_root: Path):
        self.output_root = Path(output_root)
        self.gate = Lock()

    def export(self, recording: RecordingSummary, format: str, cancelled: Event, request_id: str = "unknown") -> SignalExportPackage:
        """Own a dedicated reader and the admission slot through package delivery."""
        if not self.gate.acquire(blocking=False):
            raise SignalExportError("RECORDING_EXPORT_BUSY", "正在导出另一段录音，请稍后重试。")
        package = None
        try:
            self.output_root.mkdir(parents=True, exist_ok=True)
            package = SignalExportPackage(Path(tempfile.mkdtemp(prefix="signal-", dir=self.output_root)), self.gate)
            self._export(recording, format, cancelled, package)
            logger.info("recording.signal_export recording_id=%s request_id=%s format=%s", recording.id, request_id, format)
            return package
        except BaseException:
            if package is not None:
                package.close()
            else:
                self.gate.release()
            raise

    def _export(self, recording, format: str, cancelled: Event, package: SignalExportPackage) -> None:
        def check():
            if cancelled.is_set():
                raise InterruptedError("录音导出已取消。")
        check()
        if format not in {"edf", "bdf"} or not recording.stored_name.startswith("wpf:"):
            raise SignalExportError("RECORDING_EXPORT_SOURCE_UNSUPPORTED", "导出仅支持已登记的原始 WPF 录音与 EDF/BDF 格式。")
        root = Path(recording.stored_name.removeprefix("wpf:"))
        facts = inspect_wpf_recording(root, check)
        if facts.source_sha256 != recording.source_sha256:
            raise SignalExportError("RECORDING_EXPORT_SOURCE_CHANGED", "录音内容与注册时不一致，请重新注册。")
        signature = wpf_source_signature(root)
        if facts.source_signature != signature:
            raise SignalExportError("RECORDING_EXPORT_SOURCE_CHANGED", "校验期间原始录音发生变化，请重试。")
        event_path = root / "events.json"
        event_signature = event_path.read_bytes() if event_path.exists() else None
        manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8-sig"))
        channels = sorted([item for item in manifest["Channels"] if
                           str(item["Kind"]).casefold() in {"1", "2", "reference", "bipolar"}],
                          key=lambda item: item["StreamIndex"])
        if (manifest.get("EegSignalUnit") != "V" or not channels or
                any(item.get("Unit") != "V" for item in channels)):
            raise SignalExportError("RECORDING_EXPORT_UNIT_UNSUPPORTED", "所有原始脑电源通道单位必须为 V。")
        indexes = [facts.eeg_indexes.index(item["StreamIndex"]) for item in channels]
        samples, duration = record_layout(facts.sample_count, facts.sampling_rate_hz, len(channels), 3 if format == "bdf" else 2)
        events = recording_events(root, facts.session_id, facts.sample_count)
        labels = channel_labels(channels)
        reader = WpfSampleReader(facts)
        try:
            if reader.source_gaps(0, facts.sample_count):
                raise SignalExportError("RECORDING_EXPORT_SOURCE_GAP", "录音包含采样缺口，不能导出为连续 EDF/BDF。")
            minima, maxima, extreme_counts = self._ranges(reader, indexes, check)
            digital_min, digital_max = (-8388608, 8388607) if format == "bdf" else (-32768, 32767)
            mappings, headers = [], []
            for index, channel in enumerate(channels):
                minimum, maximum = minima[index], maxima[index]
                if minimum == maximum:
                    minimum, maximum = minimum - .5, maximum + .5
                minimum, maximum = header_bound(minimum, upper=False), header_bound(maximum, upper=True)
                mappings.append({"export_label": labels[index], "source_label": channel.get("Label"),
                                 "stream_index": channel["StreamIndex"], "native_channel_index": channel.get("NativeChannelIndex"),
                                 "kind": channel["Kind"], "source_unit": "V", "export_unit": "uV",
                                 "source_minimum_uv": float(minima[index]), "source_maximum_uv": float(maxima[index]),
                                 "physical_minimum_uv": minimum, "physical_maximum_uv": maximum,
                                 "quantization_step_uv": (maximum - minimum) / (digital_max - digital_min),
                                 "constant": bool(minima[index] == maxima[index]),
                                 "samples_abs_ge_100000_uv": int(extreme_counts[index])})
                headers.append({"label": labels[index], "dimension": "uV", "sample_frequency": facts.sampling_rate_hz,
                                "physical_min": minimum, "physical_max": maximum,
                                "digital_min": digital_min, "digital_max": digital_max,
                                "transducer": "", "prefilter": ""})
            signal = package.directory / f"recording.{format}"
            self._write(signal, format, reader, indexes, samples, duration, headers, events, facts.recording_start_utc, check)
            self._verify_written_header(signal, mappings, facts.sample_count, facts.sampling_rate_hz)
            check()
            if signature != wpf_source_signature(root) or event_signature != (event_path.read_bytes() if event_path.exists() else None):
                raise SignalExportError("RECORDING_EXPORT_SOURCE_CHANGED", "导出期间录音或事件发生变化，请重试。")
            metadata = {"schema_version": "recording-signal-export-v2", "recording_id": recording.id,
                        "source_sha256": facts.source_sha256, "session_id": facts.session_id,
                        "recording_start_utc": facts.recording_start_utc, "first_sample_counter": reader.first_counter,
                        "sample_count": facts.sample_count, "sampling_rate_hz": facts.sampling_rate_hz,
                        "data_record_duration_s": duration, "samples_per_data_record": samples,
                        "processing": {"signal_mode": "raw", "filtered": False, "software_rereferenced": False},
                        "writer": {"library": "pyEDFlib", "version": pyedflib.__version__, "format": format + "+"},
                        "hardware": hardware_metadata(manifest), "channels": mappings, "events": events,
                        "quality_note": "有限极端值原样保留；幅度阈值仅为诊断，不代表已识别无效值。"}
            sidecar = signal.with_name(signal.name + ".labels.json")
            sidecar.write_text(json.dumps(metadata, ensure_ascii=False, indent=2, allow_nan=False), encoding="utf-8")
            with ZipFile(package.path, "w", compression=ZIP_STORED, allowZip64=True) as archive:
                for path in (signal, sidecar):
                    with path.open("rb") as source, archive.open(path.name, "w", force_zip64=True) as destination:
                        while block := source.read(1024 * 1024):
                            check()
                            destination.write(block)
        finally:
            reader.close()

    @staticmethod
    def _verify_written_header(signal: Path, mappings: list[dict], sample_count: int, sampling_rate_hz: float) -> None:
        """Use the encoded EDF header as truth; text fields may round again to eight bytes."""
        with pyedflib.EdfReader(str(signal)) as reader:
            if (reader.signals_in_file != len(mappings) or np.any(reader.getNSamples() != sample_count) or
                    Fraction(sample_count, reader.datarecords_in_file) / Fraction(str(reader.datarecord_duration)) !=
                    Fraction(str(sampling_rate_hz))):
                raise SignalExportError("RECORDING_EXPORT_HEADER_MISMATCH", "EDF/BDF 实际样本数、采样率或通道数不一致，未交付文件。")
            for index, mapping in enumerate(mappings):
                minimum = float(reader.getPhysicalMinimum(index))
                maximum = float(reader.getPhysicalMaximum(index))
                digital_minimum = int(reader.getDigitalMinimum(index))
                digital_maximum = int(reader.getDigitalMaximum(index))
                if (minimum != mapping["physical_minimum_uv"] or maximum != mapping["physical_maximum_uv"] or
                        minimum > mapping["source_minimum_uv"] or maximum < mapping["source_maximum_uv"]):
                    raise SignalExportError("RECORDING_EXPORT_HEADER_MISMATCH", "EDF/BDF 实际物理范围不完整，未交付文件。")
                mapping["physical_minimum_uv"] = minimum
                mapping["physical_maximum_uv"] = maximum
                mapping["quantization_step_uv"] = (maximum - minimum) / (digital_maximum - digital_minimum)

    @staticmethod
    def _ranges(reader, indexes, check):
        minima = np.full(len(indexes), np.inf)
        maxima = np.full(len(indexes), -np.inf)
        extreme_counts = np.zeros(len(indexes), dtype=np.int64)
        for start in range(0, reader.sample_count, BLOCK_SAMPLES):
            check()
            values = reader.read(start, min(start + BLOCK_SAMPLES, reader.sample_count), indexes, check) * 1e6
            if not np.isfinite(values).all():
                raise SignalExportError("RECORDING_EXPORT_NONFINITE", "源数据包含 NaN/Inf，不能静默填零导出。")
            minima = np.minimum(minima, values.min(axis=0))
            maxima = np.maximum(maxima, values.max(axis=0))
            extreme_counts += (np.abs(values) >= 100000).sum(axis=0)
        return minima, maxima, extreme_counts

    @staticmethod
    def _write(path, format, reader, indexes, samples, duration, headers, events, start_utc, check):
        file_type = pyedflib.FILETYPE_BDFPLUS if format == "bdf" else pyedflib.FILETYPE_EDFPLUS
        with pyedflib.EdfWriter(str(path), len(indexes), file_type=file_type) as writer:
            writer.setSignalHeaders(headers)
            with warnings.catch_warnings():
                warnings.filterwarnings("ignore", message="Forcing a specific record_duration.*")
                # The pinned binding truncates seconds * 100000 (e.g. 0.009 becomes 899 ticks).
                writer.setDatarecordDuration(math.nextafter(duration, math.inf) if duration < 60 else duration)
            start = datetime.fromisoformat(start_utc).astimezone(timezone.utc).replace(tzinfo=None)
            writer.setStartdatetime(start.replace(microsecond=0))
            record_count = reader.sample_count // samples
            annotation_signals = max(1, math.ceil(len(events) / record_count))
            if annotation_signals > 64:
                raise ValueError("事件密度超过 EDF/BDF 注释容量。")
            writer.set_number_of_annotation_signals(annotation_signals)
            # pyEDFlib 0.1.42's datetime setter uses x100; EDF's 100-ns ticks require x10.
            if pyedflib.edfwriter.set_starttime_subsecond(writer.handle, start.microsecond * 10) != 0:
                raise ValueError("EDF/BDF 亚秒起始时间写入失败。")
            for offset in range(0, reader.sample_count, samples):
                check()
                values = reader.read(offset, offset + samples, indexes, check)
                if not np.isfinite(values).all():
                    raise SignalExportError("RECORDING_EXPORT_NONFINITE", "写入期间源数据出现 NaN/Inf。")
                if writer.blockWritePhysicalSamples(np.ascontiguousarray(values.T * 1e6).ravel()) != 0:
                    raise OSError("EDF/BDF 数据记录写入失败。")
            for event in events:
                check()
                if writer.writeAnnotation(event["start_sample"] / reader.sfreq,
                                          event["duration_samples"] / reader.sfreq, event["name"]) != 0:
                    raise OSError("EDF/BDF 事件写入失败。")
