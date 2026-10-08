"""Read-only real-recording numerical audit in isolated application storage.

Run with PYTHONPATH=. and uv run --frozen --extra dev python -m
scripts.audit_recording_algorithms RECORDING. JSON is printed, not raw EEG.
"""

import argparse
import json
import platform
from pathlib import Path
import tempfile

import numpy as np
from pydantic import ValidationError

from app.algorithms.builtin_modules import build_builtin_modules
from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from app.services.recording_samples import WpfSampleReader
from app.services.recordings import RecordingService
from app.scientific.quality import SpectralQualityGateError
from scripts import recording_audit_reference as ref


class Comparison:
    def __init__(self):
        self.points = self.failed = 0
        self.max_abs = self.max_rel = 0.

    def compare(self, actual, expected, *, atol=1e-12, rtol=1e-9):
        if actual is None or expected is None:
            self.points += 1
            self.failed += int(actual is not expected)
            return
        a, b = np.asarray(actual, float), np.asarray(expected, float)
        self.points += b.size
        if a.shape != b.shape:
            self.failed += b.size
            return
        self.failed += int(np.count_nonzero(~np.isclose(a, b, atol=atol, rtol=rtol, equal_nan=True)))
        mask = np.isfinite(a) & np.isfinite(b)
        if mask.any():
            delta = np.abs(a[mask] - b[mask])
            self.max_abs = max(self.max_abs, float(delta.max()))
            self.max_rel = max(self.max_rel, float((delta / np.maximum(np.abs(b[mask]), 1e-30)).max()))

    def report(self):
        return dict(points=self.points, failed=self.failed, max_abs=self.max_abs, max_rel=self.max_rel)


class AuditContext(_RecordingAlgorithmContext):
    def __init__(self, service, recording, raw, names, cache):
        super().__init__(service, recording)
        self.raw, self.names, self.cache = raw, names, cache
        self.expected = []
        self.spectra, self.quality = Comparison(), Comparison()

    def slice(self, channels, start, duration, low, high, notch):
        key = (low, high, notch)
        if key not in self.cache:
            self.cache[key] = ref.filtered(self.raw, self.sfreq_hz, *key)
        left = int(np.floor(start * self.sfreq_hz))
        right = left + round(duration * self.sfreq_hz)
        indexes = [self.names.index(name) for name in channels]
        return self.cache[key][left:right, :][:, indexes]

    def reference_spectrum(self, kwargs):
        low = kwargs.get("filter_low_hz", kwargs.get("low_hz", 1))
        high = kwargs.get("filter_high_hz", kwargs.get("high_hz", 30))
        out_low, out_high = kwargs.get("output_low_hz", low), kwargs.get("output_high_hz", high)
        values = self.slice(kwargs["channels"], kwargs["start_s"], kwargs["window_s"],
                            low, high, kwargs.get("notch_hz"))
        return ref.welch(values, self.sfreq_hz, out_low, out_high)

    def load_spectrum(self, **kwargs):
        expected = self.reference_spectrum(kwargs)
        self.expected.append(expected)
        try:
            actual = super().load_spectrum(**kwargs)
        except SpectralQualityGateError as exc:
            self.quality.compare([exc.quality["clean_segments"], exc.quality["total_segments"]], expected[2:])
            self.quality.compare(None, None if expected[0] is None else 1)
            raise
        self.quality.compare([actual.clean_epochs, actual.total_epochs], expected[2:])
        self.spectra.compare(actual.freqs, expected[0], atol=0)
        self.spectra.compare(actual.psd * 1e12, expected[1] * 1e12, atol=1e-10)
        return actual

    def load_spectrogram(self, **kwargs):
        freq, density, clean, total = self.reference_spectrum(kwargs)
        try:
            actual = super().load_spectrogram(**kwargs)
        except SpectralQualityGateError:
            self.expected.append(None)
            self.quality.compare(None, None if freq is None else 1)
            raise
        values = self.slice(kwargs["channels"], kwargs["start_s"], kwargs["window_s"],
                            kwargs["low_hz"], kwargs["high_hz"], kwargs.get("notch_hz"))
        fs = self.sfreq_hz
        size = round(4 * fs)
        axis = np.fft.rfftfreq(size, 1 / fs)
        mask = (axis >= kwargs["output_low_hz"]) & (axis <= kwargs["output_high_hz"])
        rows, states = [], []
        for s in range(0, len(values) - size + 1, round(fs)):
            epoch = values[s:s + size]
            valid = ref.clean(epoch)
            states.append(valid)
            rows.append(ref.periodogram(epoch, fs)[0, mask] if valid else np.full(mask.sum(), np.nan))
        rows = np.asarray(rows)
        self.spectra.compare(actual["power_linear"][kwargs["channels"][0]], rows * 1e12, atol=1e-10)
        self.spectra.compare(actual["times_s"], kwargs["start_s"] + 2 + np.arange(len(rows)), atol=1e-12)
        self.quality.compare([q["status"] == "clean" for q in actual["quality"]["windows"]], states, atol=0)
        self.expected.append(rows)
        return actual


def config_for(module, mode, channel, duration, **extra):
    identifier = module.manifest.algorithm_id
    values = dict(channel=channel, mode=mode, start_s=0, end_s=duration,
                  window_s=20 if identifier == "faa" else 5, step_s=1)
    if identifier in {"psd", "stft", "faa"}:
        values.update(low_hz=1, high_hz=50, notch_hz=50)
    if identifier == "peak_frequency":
        values.update(low_hz=8, high_hz=13)
    if identifier == "band_ratio":
        values.update(numerator_low_hz=4, numerator_high_hz=8, denominator_low_hz=13, denominator_high_hz=30)
    if identifier == "faa":
        values.update(channel="F3", f4_channel="F4")
    if identifier == "brainbeat":
        values.update(channel="Fz", secondary_channel="Pz")
    return module.config_model.model_validate(values | extra)


def audit_result(module, config, result, context):
    identifier = module.manifest.algorithm_id
    dynamic = config.mode == "dynamic"
    comparison, alternative = Comparison(), Comparison()
    unavailable = 0
    count = len(result.windows) if dynamic else 1
    for index in range(count):
        if identifier == "faa":
            bounds = result.windows[index] if dynamic else result.actual_range
            pair = context.slice(["F3", "F4"], bounds["start_s"], bounds["end_s"] - bounds["start_s"],
                                 config.low_hz, config.high_hz, config.notch_hz)
            expected = ref.faa(pair, context.sfreq_hz)
        elif identifier == "stft":
            expected = context.expected[index]
            actual = result.arrays["power_linear"][index] if dynamic else result.arrays["power_linear"]
            if expected is None:
                comparison.compare(np.isnan(actual).all(), True, atol=0)
                unavailable += 1
                continue
            if dynamic:
                expected = np.pad(expected, ((0, len(actual) - len(expected)), (0, 0)), constant_values=np.nan)
            comparison.compare(actual * 1e12, expected * 1e12, atol=1e-10)
            db_expected = 10 * np.log10(np.maximum(expected * 1e12, np.finfo(float).tiny))
            db_actual = result.arrays["power_db"][index] if dynamic else result.arrays["power_db"]
            comparison.compare(db_actual, db_expected, atol=1e-8)
            continue
        else:
            freq, density, _, _ = context.expected[index]
            if identifier == "psd":
                actual = result.arrays["psd"][index] if dynamic else result.arrays["psd"]
                comparison.compare(actual * 1e12, density[0] * 1e12 if density is not None else np.full(actual.shape, np.nan), atol=1e-10)
                if density is not None:
                    share = result.arrays["band_share"][index] if dynamic else result.arrays["band_share"]
                    comparison.compare(share, ref.shares(freq, density))
                continue
            if identifier == "rbp":
                outputs = result.output_values[index] if dynamic else result.output_values
                expected = ref.shares(freq, density) if density is not None else [None] * 5
                for actual, value in zip([outputs[k] for k in ("delta", "theta", "alpha", "beta", "gamma")], expected):
                    comparison.compare(actual, value)
                unavailable += int(density is None)
                continue
            expected = ref.scalar(identifier, freq, density, config)
        actual = result.values[index] if dynamic else result.value
        comparison.compare(actual, expected)
        unavailable += int(expected is None)
        if identifier == "brainbeat" and expected is not None:
            alternative.compare(actual, ref.scalar(identifier, freq, density, config, brainbeat_interpolate=True))
    timing = Comparison()
    if dynamic:
        bounds = [(w.start_s, w.end_s) if hasattr(w, "start_s") else (w["start_s"], w["end_s"])
                  for w in result.windows]
        for left, right in bounds:
            timing.compare(left, max(config.start_s, right - config.window_s), atol=1e-9)
        if hasattr(result, "time_s"):
            timing.compare(result.time_s, [right for _, right in bounds], atol=1e-9)
        timing.compare([right for _, right in bounds],
                       np.arange(bounds[0][1], config.end_s + 1e-9, config.step_s), atol=1e-9)
    return dict(windows=count, unavailable=unavailable, result=comparison.report(), timing=timing.report(),
                spectrum=context.spectra.report(), quality=context.quality.report(),
                brainbeat_interpolated_boundary_comparison=alternative.report())


def audit(directory):
    reference_raw, reference_names, reference_fs, gaps = ref.read_source(directory)
    reader = WpfSampleReader(directory)
    raw = reader.read(0, reader.sample_count, list(range(len(reader.names))))
    names = list(reader.names)
    assert names == reference_names and reader.sfreq == reference_fs
    np.testing.assert_array_equal(raw, reference_raw)
    raw = reference_raw
    reader.close()
    import scipy
    report = {"checks": [], "probes": [], "environment": dict(python=platform.python_version(),
              numpy=np.__version__, scipy=scipy.__version__),
              "tolerances": dict(rtol=1e-9, scalar_atol=1e-12, density_uv2_atol=1e-10, db_atol=1e-8)}
    cache = {}
    with tempfile.TemporaryDirectory(prefix="brain-numeric-audit-", ignore_cleanup_errors=True) as temp:
        service = RecordingService(Path(temp) / "recordings", Path(temp) / "audit.sqlite3")
        recording = service.register_wpf_recording(str(directory))
        report["source"] = dict(sha256=recording.source_sha256, duration_s=recording.duration_s,
                                sfreq_hz=recording.sfreq, channels=len(names), samples=len(raw),
                                nonfinite=int((~np.isfinite(raw)).sum()), gap_samples=gaps,
                                independent_raw_reader_exact_match=True)
        modules = build_builtin_modules()
        for module in modules:
            identifier = module.manifest.algorithm_id
            channels = ["F3"] if identifier in {"faa", "brainbeat"} else ["F3", "O1"]
            for channel in channels:
                for mode in ("static", "dynamic"):
                    config = config_for(module, mode, channel, recording.duration_s)
                    context = AuditContext(service, recording, raw, names, cache)
                    try:
                        inputs = module.resolve_inputs(context, config)
                        result = getattr(module, "execute_" + mode)(inputs, config)
                        check = audit_result(module, config, result, context)
                        if mode == "static":
                            check["static_value"] = getattr(result, "value", None)
                        check.update(algorithm=identifier, mode=mode, channel=config.channel,
                                     scientific_version=module.manifest.scientific_version,
                                     implementation=module.manifest.implementation_identity,
                                     snapshot=module.execution_snapshot(config).model_dump(mode="json"),
                                     actual_spectral_filter=(result.evidence.get("spectral_evidence", {}).get("filter_contract")
                                         if mode == "static" else None),
                                     config=config.model_dump(mode="json"))
                        report["checks"].append(check)
                        print(json.dumps(check, ensure_ascii=True), flush=True)
                    finally:
                        context.close()
        probe_configs = [
            ("peak_frequency", {"low_hz": 35, "high_hz": 40}),
            ("peak_frequency", {"low_hz": 25, "high_hz": 40}),
            ("band_ratio", {"numerator_low_hz": 4, "numerator_high_hz": 8,
                            "denominator_low_hz": 30, "denominator_high_hz": 50}),
            ("psd", {"notch_hz": None}), ("psd", {"notch_hz": 60}),
        ]
        for identifier, extra in probe_configs:
            module = next(m for m in modules if m.manifest.algorithm_id == identifier)
            try:
                config = config_for(module, "static", "F3", recording.duration_s, **extra)
            except ValidationError as exc:
                probe = dict(algorithm=identifier, requested_bands=extra, rejected_before_loading=True,
                             errors=exc.errors(include_url=False, include_context=False))
                report["probes"].append(probe)
                print(json.dumps(probe, ensure_ascii=True), flush=True)
                continue
            context = AuditContext(service, recording, raw, names, cache)
            try:
                result = module.execute_static(module.resolve_inputs(context, config), config)
                spectral = result.evidence.get("spectral_evidence", {})
                probe = dict(algorithm=identifier, config=config.model_dump(mode="json"),
                             value=getattr(result, "value", None), quality=result.quality,
                             failure=result.failure.model_dump(mode="json") if result.failure else None,
                             snapshot=module.execution_snapshot(config).model_dump(mode="json"),
                             actual_filter=spectral.get("filter_contract"),
                             actual_axis=spectral.get("output_frequency_range_hz"))
                if identifier != "psd":
                    values = context.slice(["F3"], 0, recording.duration_s, 1, 30, None)
                    freq, density, _, _ = ref.welch(values, recording.sfreq, 1, 50)
                    probe["reference_with_extended_output_same_filter"] = ref.scalar(identifier, freq, density, config)
                else:
                    probe["comparison"] = audit_result(module, config, result, context)
                report["probes"].append(probe)
                print(json.dumps(probe, ensure_ascii=True), flush=True)
            finally:
                context.close()
        service._analysis_preprocess_cache.clear_recording(recording.id)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--expect-fixed", action="store_true")
    arguments = parser.parse_args()
    report = audit(arguments.directory)
    report["totals"] = {key: {field: sum(c[key][field] for c in report["checks"])
                       for field in ("points", "failed")} for key in ("result", "spectrum", "quality", "timing")}
    if arguments.expect_fixed:
        assert all(item["failed"] == 0 for item in report["totals"].values())
        assert all(probe.get("rejected_before_loading") for probe in report["probes"][:3])
        for probe in report["probes"][3:]:
            assert probe["snapshot"]["filters"]["notch_hz"] == probe["actual_filter"]["notch_hz"]
            assert probe["comparison"]["result"]["failed"] == 0
        for check in report["checks"]:
            if check["algorithm"] == "peak_frequency":
                assert check["snapshot"]["filters"]["bandpass_hz"] == [1, 30]
        report["fixed_contract_checks_passed"] = True
    if arguments.output:
        arguments.output.write_text(json.dumps(report, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"source": report["source"], "totals": report["totals"]}, indent=2))
