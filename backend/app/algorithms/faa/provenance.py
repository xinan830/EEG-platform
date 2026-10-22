"""FAA-owned projection of paired-epoch evidence for current Run views."""

from typing import Any


def _as_dict(value: object) -> dict[str, Any]:
    return dict(value) if isinstance(value, dict) else {}


def _faa_provenance_fields(faa: dict[str, Any]) -> dict[str, object]:
    """Project only the current paired-epoch FFT contract."""
    contract = _as_dict(faa.get("faa_contract"))
    required = ("epoch_s", "overlap_fraction", "step_s", "frequency_resolution_hz", "alpha_band_hz")
    if any(key not in contract for key in required):
        return {}
    try:
        epoch_s = float(contract["epoch_s"])
        overlap = float(contract["overlap_fraction"])
        step_s = float(contract["step_s"])
        resolution = float(contract["frequency_resolution_hz"])
        band = [float(item) for item in contract["alpha_band_hz"]]
        if len(band) != 2 or epoch_s <= 0 or resolution <= 0:
            raise ValueError("invalid current FAA contract")
    except (TypeError, ValueError):
        return {}
    preprocessing = {"kind": "per_epoch_demean", "label_zh": "每个成对 Epoch 独立去均值；未做软件带通"}
    if contract.get("software_bandpass"):
        scope = faa.get("preprocessing_scope")
        preprocessing = {
            "kind": "continuous_recording_then_epoch_demean" if scope == "whole_recording_before_window_slice" else "window_filter_then_epoch_demean",
            "label_zh": "整条记录连续滤波后截取分析窗口，再逐 Epoch 去均值" if scope == "whole_recording_before_window_slice" else "分析窗口独立滤波，再逐 Epoch 去均值",
        }
    return {
        "method": {"kind": "faa_paired_epoch_fft", "label_zh": "成对 Epoch FFT 密度谱", "detail_zh": f"{epoch_s:g} s Hann，{overlap * 100:g}% Epoch overlap（{step_s:g} s 步进）"},
        "preprocessing": preprocessing,
        "frequency": {"label_zh": "Alpha 积分频段", "low_hz": band[0], "high_hz": band[1], "point_count": int(round((band[1] - band[0]) / resolution)) + 1, "resolution_hz": resolution},
        "quality_label_zh": "成对 Epoch 质量门",
    }



def project_provenance(official: dict[str, Any]) -> dict[str, Any]:
    """Describe FAA evidence without adding interpretation to shared serializers."""
    faa = _as_dict(official.get("faa_evidence"))
    if not faa:
        return {}
    contract = _as_dict(faa.get("faa_contract"))
    channels = faa.get("channels") if isinstance(faa.get("channels"), list) else []
    paired = {
        **faa,
        "source_channels": faa.get("source_channels") or {
            "left": channels[0] if len(channels) > 0 else None,
            "right": channels[1] if len(channels) > 1 else None,
        },
        "faa_contract": {
            **contract,
            "minimum_clean_epochs": contract.get("minimum_clean_epochs", 10),
            "artifact_peak_uv": contract.get("artifact_peak_uv", faa.get("artifact_uv")),
        },
    }
    return {
        "evidence": faa,
        "fields": _faa_provenance_fields(faa),
        "extensions": [{"kind": "faa_paired_quality", "data": paired}],
    }
