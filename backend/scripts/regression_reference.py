"""Independent raw-signal expectations; never import production science/runtime."""

from types import SimpleNamespace

import numpy as np

from scripts import recording_audit_reference as ref


BANDS = dict(delta=(1, 4), theta=(4, 8), alpha=(8, 13), beta=(13, 30), gamma=(30, 50))


def schedule(algorithm, mode):
    if mode == "static":
        return [(0, 30)]
    width, first = (20, 20) if algorithm == "faa" else (5, 4)
    return [(max(0, end - width), end) for end in range(first, 31)]


def faa_reference(values, fs):
    size = round(2 * fs)
    epochs = [values[i:i + size] for i in range(0, len(values) - size + 1, round(fs))]
    centered = [epoch - epoch.mean(axis=0) for epoch in epochs]
    valid = [epoch for epoch in centered if np.isfinite(epoch).all() and np.max(np.abs(epoch)) <= 150e-6]
    if len(valid) < 10:
        return None, None, None, len(valid), len(epochs)
    density = np.mean([ref.periodogram(epoch, fs) for epoch in valid], axis=0)
    freq = np.fft.rfftfreq(size, 1 / fs)
    left, right = [ref.integrate(freq, row, 8, 13) for row in density]
    return float(np.log(right) - np.log(left)), left, right, len(valid), len(epochs)


def iapf_details(freq, density):
    power = density.mean(axis=0)
    mask = (freq >= 3) & (freq <= 30) & ~((freq >= 7) & (freq <= 13))
    x, y = np.log10(freq[mask]), np.log10(power[mask])
    slope = np.dot(x - x.mean(), y - y.mean()) / np.dot(x - x.mean(), x - x.mean())
    intercept = y.mean() - slope * x.mean()
    fitted = intercept + slope * x
    alpha_mask = (freq >= 7) & (freq <= 13)
    alpha = freq[alpha_mask]
    baseline = 10 ** (intercept + slope * np.log10(alpha))
    residual = np.maximum(power[alpha_mask] - baseline, 0)
    index = np.argmax(residual)
    return dict(peak_hz=float(alpha[index]),
                cog_hz=float(np.dot(alpha, residual) / residual.sum()) if residual.sum() > 0 else None,
                model_error=float(np.mean(np.abs(y - fitted))),
                model_r2=float(1 - np.sum((y - fitted) ** 2) / np.sum((y - y.mean()) ** 2)),
                source="peak" if residual[index] / max(baseline[index], 1e-20) >= .2 else "cog")


def check_quality(path, evidence, clean, total, checks):
    checks.exact(path + ".clean_segments", evidence["clean_segments"], clean)
    checks.exact(path + ".total_segments", evidence["total_segments"], total)
    checks.numeric(path + ".clean_ratio", evidence["clean_ratio"], clean / total)


def check_spectrum(path, evidence, freq, density, checks, *, rbp=False):
    checks.numeric(path + ".frequencies_hz", evidence["frequencies_hz"], freq, rtol=0, atol=0)
    checks.numeric(path + ".psd_uv2_per_hz", evidence["psd_uV2_per_hz"],
                   density[0] * 1e12, atol=1e-10)
    bands = BANDS if rbp else {key: value for key, value in BANDS.items() if key != "gamma"}
    powers = {name: ref.integrate(freq, density[0], *edges) * 1e12
              for name, edges in bands.items()}
    checks.tree(path + ".band_power", evidence["band_power"], powers)
    total = sum(powers.values())
    checks.tree(path + ".relative_band_power", evidence["relative_band_power"],
                {name: value / total for name, value in powers.items()})
    if rbp:
        checks.numeric(path + ".rbp_denominator_uv2", evidence["rbp_denominator_uv2"], total)


def check_extensions(path, point, algorithm, freq, density, checks):
    official = point["official"]
    if algorithm == "iapf":
        checks.tree(path + ".iapf", official["iapf_evidence"], iapf_details(freq, density))
    elif algorithm == "band_ratio":
        evidence = official["band_ratio_evidence"]
        for name, edges in (("numerator", (4, 8)), ("denominator", (13, 30))):
            checks.exact(path + f".{name}_band_hz", evidence[f"{name}_band_hz"], list(edges))
            checks.numeric(path + f".{name}_power_v2", evidence[f"{name}_power_v2"],
                           ref.integrate(freq, density[0], *edges), atol=1e-22)
    elif algorithm in {"theta_beta", "brainbeat"}:
        peak = ref.iapf(freq, density)
        evidence = official[algorithm + "_evidence"]
        checks.numeric(path + ".iapf_hz", evidence["iapf_hz"], peak)
        if algorithm == "theta_beta":
            theta, beta = [max(4, peak - 6), peak - 2], [peak + 2, 30]
            checks.exact(path + ".theta_range_hz", evidence["theta_range_hz"], theta)
            checks.exact(path + ".beta_range_hz", evidence["beta_range_hz"], beta)
            for name, edges in (("theta", theta), ("beta", beta)):
                checks.numeric(path + f".{name}_power_uv2", evidence[f"{name}_power_uv2"],
                               ref.integrate(freq, density[0], *edges) * 1e12)


def stft_reference(values, fs, low, high):
    size = round(4 * fs)
    full = np.fft.rfftfreq(size, 1 / fs)
    mask = (full >= low) & (full <= high)
    epochs = [values[i:i + size] for i in range(0, len(values) - size + 1, round(fs))]
    valid = [ref.clean(epoch) for epoch in epochs]
    rows = [ref.periodogram(epoch, fs)[0, mask] if good else np.full(mask.sum(), np.nan)
            for epoch, good in zip(epochs, valid)]
    linear = np.asarray(rows)
    db = 10 * np.log10(np.maximum(linear * 1e12, np.finfo(float).tiny))
    return full[mask], 2 + np.arange(len(rows), dtype=float), linear, db, valid


def check_stft(path, point, evidence, arrays, values, fs, start, dynamic, gate, checks):
    frequency, times, linear, db, valid = stft_reference(values, fs, 1, 50)
    quality = "clean" if all(valid) else "partial" if any(valid) else "gate_failed"
    if dynamic:
        padded = np.full((2, len(frequency)), np.nan)
        padded_db = padded.copy()
        if gate:
            padded[:len(linear)], padded_db[:len(db)] = linear, db
        linear, db = padded, padded_db
        quality = "clean" if gate and all(valid) else "gate_failed"
    checks.exact(path + ".quality", point["quality"] if dynamic else point["quality"]["status"], quality)
    checks.numeric(path + ".power_linear", arrays["power_linear"], linear, atol=1e-22)
    checks.numeric(path + ".power_db", arrays["power_db"], db, atol=1e-8)
    if not dynamic:
        checks.numeric(path + ".time_centers", arrays["axis_time_center_s"], times + start, rtol=0, atol=0)
    if gate:
        quality_rows = evidence["source_quality"]
        checks.exact(path + ".clean_windows", quality_rows["clean_windows"], sum(valid))
        checks.exact(path + ".bad_windows", quality_rows["bad_windows"], len(valid) - sum(valid))
        checks.exact(path + ".inner_total", quality_rows["total_windows"], len(valid))
        checks.exact(path + ".quality_rows", len(quality_rows["windows"]), len(valid))
        for index, (row, good) in enumerate(zip(quality_rows["windows"], valid)):
            checks.exact(path + f".row-{index}.status", row["status"], "clean" if good else "bad")
            checks.exact(path + f".row-{index}.start", row["start_s"], start + index)
            checks.exact(path + f".row-{index}.end", row["end_s"], start + index + 4)
    # Static STFT retains clean rows with partial quality and no failure.
    return gate and (all(valid) if dynamic else any(valid))


def check_frame(path, point, start, end, fs, structured, checks):
    if structured:
        checks.exact(path + ".start_s", point["start_s"], start)
        checks.exact(path + ".end_s", point["end_s"], end)
        checks.exact(path + ".start_sample", point["start_sample"], round(start * fs))
        checks.exact(path + ".end_sample", point["end_sample"], round(end * fs))
    else:
        checks.exact(path + ".start_s", point["window_start_s"], start)
        checks.exact(path + ".end_s", point["window_end_s"], end)
        checks.exact(path + ".time_s", point["time_s"], end)


def validate_scenario(key, snapshot, arrays, source, labels, fs, checks, cache):
    kind, algorithm, mode = key.split("/")
    dynamic, structured = mode == "dynamic", algorithm in {"psd", "stft"}
    bounds = schedule(algorithm, mode)
    high, notch = (50, 50) if algorithm in {"psd", "stft", "rbp", "faa"} else (30, None)
    roles = ["F3", "F4"] if algorithm == "faa" else ["Fz", "Pz"] if algorithm == "brainbeat" else ["Fz"]
    selected = [f"{role}-{kind}" for role in roles]
    checks.exact(key + ".channel_order", snapshot["channels"]["channels"], selected)
    checks.exact(key + ".role_mapping", snapshot["channels"]["analysis_montage"]["outputs"],
                 [dict(role=role, output=channel, unit="V") for role, channel in zip(roles, selected)])
    cache_key = (kind, high)
    if cache_key not in cache:
        cache[cache_key] = ref.filtered(source, fs, 1, high, notch)
    prepared = cache[cache_key][:, [labels.index(role) for role in roles]]
    result = snapshot["result"]["structured" if structured else "metric"]
    points = result["windows" if structured else "series"] if dynamic else [result]
    checks.exact(key + ".point_count", len(points), len(bounds))
    checks.exact(key + ".requested", snapshot["requested_range"], dict(start_s=0, end_s=30))
    checks.exact(key + ".actual", snapshot["actual_range"], dict(start_s=0, end_s=30))
    if algorithm == "faa":
        checks.exact(key + ".filters", snapshot["filters"]["software_bandpass"], [1, 50])
    else:
        checks.exact(key + ".filters", snapshot["filters"]["bandpass_hz"], [1, high])
    checks.exact(key + ".notch", snapshot["filters"]["notch_hz"], notch)
    if dynamic and not structured:
        checks.numeric(key + ".metric_time_s", arrays["metric_time_s"], [end for _, end in bounds], rtol=0, atol=0)
    if structured:
        frequency = np.fft.rfftfreq(round(4 * fs), 1 / fs)
        frequency = frequency[(frequency >= 1) & (frequency <= high)]
        checks.numeric(key + ".frequency_axis", arrays["axis_frequency_hz"], frequency, rtol=0, atol=0)
        if dynamic and algorithm == "stft":
            checks.numeric(key + ".inner_time_axis", arrays["axis_time_center_s"], [2, 3], rtol=0, atol=0)
    for index, (point, (start, end)) in enumerate(zip(points, bounds)):
        path = f"{key}/window-{index:03d}"
        if dynamic:
            check_frame(path, point, start, end, fs, structured, checks)
        else:
            checks.exact(path + ".actual", point["actual_range"], dict(start_s=start, end_s=end))
        values = prepared[round(start * fs):round(end * fs)]
        evidence = snapshot["result"]["window_evidence"][index] if dynamic and structured else point
        scalar_value = None
        if algorithm == "faa":
            scalar_value, left, right, clean, total = faa_reference(values, fs)
            gate = scalar_value is not None
            check_quality(path, point["source_quality"], clean, total, checks)
            detail = point["official"]["faa_evidence"]
            for name, expected in (("p_f3", left), ("p_f4", right)):
                if expected is None:
                    checks.exact(path + "." + name, detail[name], None)
                else:
                    checks.numeric(path + "." + name, detail[name], expected, atol=1e-22)
            checks.exact(path + ".preprocessing_scope", detail["preprocessing_scope"], "whole_recording_before_window_slice")
            checks.exact(path + ".preprocessing_range", detail["preprocessing_range_s"],
                         dict(start_s=0, end_s=len(source) / fs))
        else:
            freq, density, clean, total = ref.welch(values, fs, 1, high)
            gate = freq is not None
            quality = evidence["source_quality"]
            if not quality and not gate:
                quality = point["failure"]["detail"]
            if algorithm != "stft" or not gate:
                check_quality(path, quality, clean, total, checks)
            if gate and algorithm != "stft":
                check_spectrum(path, evidence["spectral_evidence"], freq, density, checks, rbp=algorithm == "rbp")
            if algorithm == "psd":
                expected = density[0] if gate else np.full(len(frequency), np.nan)
                shares = ref.shares(freq, density) if gate else np.full(5, np.nan)
                for name, expected_array in (("psd", expected), ("band_share", shares)):
                    checks.numeric(path + "." + name, arrays[name][index] if dynamic else arrays[name],
                                   expected_array, atol=1e-22 if name == "psd" else 1e-12)
            elif algorithm == "stft":
                window_arrays = {name: array[index] if dynamic and name in {"power_linear", "power_db"} else array
                                 for name, array in arrays.items()}
                gate = check_stft(path, point, evidence, window_arrays, values, fs, start, dynamic, gate, checks)
            elif algorithm == "rbp":
                shares = ref.shares(freq, density) if gate else [None] * 5
                expected = dict(zip(BANDS, shares))
                checks.tree(path + ".band_values", point["band_values"], expected)
                if not dynamic:
                    checks.numeric(path + ".band_share_values", arrays["band_share_values"], list(expected.values()))
            elif gate:
                config = SimpleNamespace(low_hz=8, high_hz=13, numerator_low_hz=4, numerator_high_hz=8,
                                         denominator_low_hz=13, denominator_high_hz=30)
                scalar_value = ref.scalar(algorithm, freq, density, config)
                gate = scalar_value is not None
                if gate:
                    check_extensions(path, point, algorithm, freq, density, checks)
        if not structured:
            actual = point["output"]["value"]
            if scalar_value is None:
                checks.exact(path + ".value", actual, None)
            else:
                checks.numeric(path + ".value", actual, scalar_value)
            checks.numeric(path + ".artifact_value", arrays["metric_values" if dynamic else "metric_value"][index],
                           np.nan if scalar_value is None else scalar_value)
        expected_quality = "clean" if gate else "gate_failed"
        if algorithm != "stft":
            checks.exact(path + ".quality", point["quality"] if dynamic and structured else point["quality"]["status"], expected_quality)
        checks.exact(path + ".has_failure", point["failure"] is not None, not gate)
        if dynamic:
            state = "Rejected" if not gate else "Partial" if end - start < 5 and algorithm != "faa" else "Complete"
            checks.exact(path + ".state", point["state" if structured else "analysis_state"], state)
        coordinate = evidence.get("extensions", {}).get("sample_coordinate") if dynamic and structured else point["official"].get("sample_coordinate")
        checks.exact(path + ".coordinate", coordinate, dict(coordinate_system="recording_relative_sample",
                     sfreq_hz=fs, sample_range=dict(start_sample=round(start * fs), end_sample=round(end * fs), half_open=True)))
        if gate and algorithm != "faa":
            typed = (evidence["calculation_trace"] if algorithm == "stft" else evidence["spectral_evidence"]).get("typed_source_request", {})
            expected = dict(roles=roles if algorithm == "brainbeat" else selected, channel_order=selected,
                            start_sample=round(start * fs), end_sample=round(end * fs), sfreq_hz=fs,
                            filter_low_hz=1., filter_high_hz=float(high), output_low_hz=1., output_high_hz=float(high),
                            notch_hz=notch, preprocessing_scope="configured_continuous_before_slice",
                            signal_input_unit="V", algorithm_density_unit="V^2/Hz", service_density_unit="uV^2/Hz",
                            representation="spectrogram" if algorithm == "stft" else "spectrum")
            source_identity = snapshot["channels"]["signal_source"]
            expected.update(montage_id=source_identity["montage_id"],
                            montage_fingerprint=source_identity.get("montage_fingerprint") or source_identity["snapshot_sha256"])
            checks.tree(path + ".typed_source_request", typed, expected)
