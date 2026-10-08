"""Independent audit math; no production spectral or metric implementations."""

import numpy as np
from scipy import signal
import json
import struct


def read_source(directory):
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    channels = manifest["Channels"]
    selected = [c["StreamIndex"] for c in channels if str(c["Kind"]).lower() in
                {"1", "2", "reference", "bipolar"} and c["Unit"] == "V"]
    names = [c["Label"] or "" for c in channels if c["StreamIndex"] in selected]
    header = struct.Struct("<qiiq")
    blocks, previous_end, gaps = [], None, 0
    for path in sorted(directory.glob("samples-*.bin")):
        with path.open("rb") as stream:
            while packet := stream.read(header.size):
                if len(packet) != header.size:
                    raise ValueError("truncated batch header")
                counter, count, columns, ticks = header.unpack(packet)
                if count <= 0 or columns != len(channels):
                    raise ValueError("invalid batch dimensions")
                if previous_end is not None:
                    if counter < previous_end:
                        raise ValueError("overlapping or unordered sample batches")
                    gaps += counter - previous_end
                    if counter > previous_end:
                        blocks.append(np.full((counter - previous_end, len(names)), np.nan))
                payload = stream.read(count * columns * 8)
                if len(payload) != count * columns * 8:
                    raise ValueError("truncated sample payload")
                blocks.append(np.frombuffer(payload, dtype="<f8").reshape(count, columns)[:, selected])
                previous_end = counter + count
    return np.vstack(blocks), names, float(manifest["SamplingRateHz"]), gaps


def filtered(raw, fs, low, high, notch):
    values = np.asarray(raw, dtype=float)
    if notch is not None:
        b, a = signal.iirnotch(notch, 30, fs=fs)
        values = signal.filtfilt(b, a, values, axis=0)
    sos = signal.butter(4, [low, high], fs=fs, btype="bandpass", output="sos")
    return signal.sosfiltfilt(sos, values, axis=0)


def clean(epoch):
    if not np.isfinite(epoch).all() or np.max(np.abs(epoch)) > 150e-6:
        return False
    if np.any(np.ptp(epoch, axis=0) < 0.5e-6):
        return False
    required = max(3, int(np.ceil(len(epoch) * .01)))
    return all(max(np.sum(col == col.min()), np.sum(col == col.max())) < required
               for col in epoch.T)


def periodogram(epoch, fs):
    window = signal.windows.hann(len(epoch), sym=False)
    centered = epoch - epoch.mean(axis=0)
    transformed = np.fft.rfft(centered * window[:, None], axis=0)
    density = np.abs(transformed) ** 2 / (fs * np.dot(window, window))
    density[1:-1] *= 2
    return density.T


def welch(values, fs, low, high):
    size, step = round(4 * fs), round(2 * fs)
    epochs = [values[s:s + size] for s in range(0, len(values) - size + 1, step)]
    valid = [epoch for epoch in epochs if clean(epoch)]
    if not valid or len(valid) / len(epochs) < .75:
        return None, None, len(valid), len(epochs)
    freq = np.fft.rfftfreq(size, 1 / fs)
    mask = (freq >= low) & (freq <= high)
    density = np.maximum(np.mean([periodogram(e, fs) for e in valid], axis=0), 1e-20)
    return freq[mask], density[:, mask], len(valid), len(epochs)


def integrate(freq, density, low, high, interpolate=True):
    if low >= high or low < freq[0] or high > freq[-1]:
        raise ValueError("band outside reference grid")
    if interpolate:
        axis = np.r_[low, freq[(freq > low) & (freq < high)], high]
        values = np.interp(axis, freq, density)
    else:
        mask = (freq >= low) & (freq <= high)
        axis, values = freq[mask], density[mask]
    return float(np.trapezoid(values, axis))


def iapf(freq, density):
    power = density.mean(axis=0)
    fit = (freq >= 3) & (freq <= 30) & ~((freq >= 7) & (freq <= 13))
    x, y = np.log10(freq[fit]), np.log10(power[fit])
    # Closed-form OLS, deliberately not production np.polyfit.
    slope = np.dot(x - x.mean(), y - y.mean()) / np.dot(x - x.mean(), x - x.mean())
    intercept = y.mean() - slope * x.mean()
    mask = (freq >= 7) & (freq <= 13)
    alpha = freq[mask]
    baseline = 10 ** (intercept + slope * np.log10(alpha))
    residual = np.maximum(power[mask] - baseline, 0)
    index = np.argmax(residual)
    if residual[index] / max(baseline[index], 1e-20) >= .2:
        return float(alpha[index])
    if residual.sum() > 0:
        return float(np.dot(alpha, residual) / residual.sum())
    return None


def scalar(algorithm, freq, density, config, *, brainbeat_interpolate=False):
    if freq is None:
        return None
    if algorithm == "peak_frequency":
        mask = (freq >= config.low_hz) & (freq <= config.high_hz)
        return float(freq[mask][np.argmax(density.mean(axis=0)[mask])]) if mask.any() else None
    if algorithm == "band_ratio":
        try:
            numerator = integrate(freq, density[0], config.numerator_low_hz, config.numerator_high_hz)
            denominator = integrate(freq, density[0], config.denominator_low_hz, config.denominator_high_hz)
            return numerator / denominator if denominator > 0 else None
        except ValueError:
            return None
    peak = iapf(freq, density)
    if algorithm == "iapf" or peak is None:
        return peak
    theta = (max(4, peak - 6), peak - 2)
    if algorithm == "theta_beta":
        return integrate(freq, density[0], *theta) / integrate(freq, density[0], peak + 2, 30)
    if algorithm == "brainbeat":
        power = lambda row, a, b: integrate(freq, row, a, b, brainbeat_interpolate)
        relative_theta = power(density[0], *theta) / (power(density[0], 1, 30) + 1e-20)
        relative_alpha = power(density[1], peak - 2, peak + 2) / (power(density[1], 1, 30) + 1e-20)
        return relative_theta / (relative_alpha + 1e-20)
    raise ValueError(algorithm)


def shares(freq, density):
    bands = [(1, 4), (4, 8), (8, 13), (13, 30), (30, 50)]
    powers = np.array([integrate(freq, density[0], *band) for band in bands])
    return powers / powers.sum()


def faa(pair, fs):
    size = round(2 * fs)
    epochs = []
    for start in range(0, len(pair) - size + 1, round(fs)):
        epoch = pair[start:start + size] - pair[start:start + size].mean(axis=0)
        if np.isfinite(epoch).all() and np.max(np.abs(epoch)) <= 150e-6:
            epochs.append(epoch)
    if len(epochs) < 10:
        return None
    density = np.mean([periodogram(e, fs) for e in epochs], axis=0)
    freq = np.fft.rfftfreq(size, 1 / fs)
    return float(np.log(integrate(freq, density[1], 8, 13))
                 - np.log(integrate(freq, density[0], 8, 13)))
