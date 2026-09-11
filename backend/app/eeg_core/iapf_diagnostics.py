"""Formatting helpers for IAPF diagnostic logs."""

import numpy as np


def _fmt_diag(value, digits=3):
    try:
        number = float(value)
    except (TypeError, ValueError):
        return "nan"
    if not np.isfinite(number):
        return "nan"
    return f"{number:.{digits}f}"


def _signed_diff(a, b):
    try:
        left, right = float(a), float(b)
    except (TypeError, ValueError):
        return float("nan")
    if not (np.isfinite(left) and np.isfinite(right)):
        return float("nan")
    return left - right


def iapf_attempt_log_line(
    result,
    locked,
    candidates,
    target_count,
    iapf_live,
    iapf_global,
    segment_samples,
    sfreq,
):
    gate = result.gate_failed or "pass"
    buffer_s = float(segment_samples) / float(sfreq) if sfreq else 0.0
    return (
        "[IAPF] "
        f"locked={bool(locked)} calibrated={bool(result.calibrated)} gate={gate} "
        f"iapf={_fmt_diag(result.iapf, 2)}Hz live={_fmt_diag(iapf_live, 2)}Hz "
        f"global={_fmt_diag(iapf_global, 2)}Hz candidates={int(candidates)}/{int(target_count)} "
        f"clean={_fmt_diag(result.clean_duration_s, 2)}/{_fmt_diag(result.total_duration_s, 2)}s "
        f"bad={_fmt_diag(result.bad_duration_s, 2)}s signal_quality={_fmt_diag(result.signal_quality, 2)} "
        f"r2={_fmt_diag(result.model_r2, 3)} mae={_fmt_diag(result.model_error, 3)} "
        f"cog={_fmt_diag(result.cog, 2)}Hz peak={_fmt_diag(result.gaussian_cf, 2)}Hz "
        f"cog_minus_peak={_fmt_diag(_signed_diff(result.cog, result.gaussian_cf), 2)}Hz "
        f"source={result.iapf_source or 'none'} "
        f"peak_exists={bool(result.peak_exists)} "
        f"quality={result.peak_quality or 'unknown'} peak_power={_fmt_diag(result.peak_power, 3)} "
        f"alpha_ratio={_fmt_diag(result.alpha_residual_ratio, 3)} buffer={_fmt_diag(buffer_s, 2)}s"
    )


def iapf_waiting_log_line(segment_samples, window_samples, sfreq):
    have_s = float(segment_samples) / float(sfreq) if sfreq else 0.0
    need_s = float(window_samples) / float(sfreq) if sfreq else 0.0
    return f"[IAPF] waiting_buffer buffer={have_s:.2f}/{need_s:.2f}s"
