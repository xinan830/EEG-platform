"""Reproducible read-only CLI verifier for offline scientific preprocessing on real WPF or EDF recordings.

Usage (from repo root):
    backend\\.venv\\Scripts\\python.exe backend/scripts/verify_offline_preprocessing.py \\
        --wpf-dir "<path-to-wpf-recording-dir>" [--output-json report.json]

Never modifies the source recording directory; runs against an isolated temporary SQLite database.
"""

from __future__ import annotations

import argparse
import json
import sys
import tempfile
import time
from pathlib import Path

BACKEND_ROOT = Path(__file__).resolve().parents[1]
if str(BACKEND_ROOT) not in sys.path:
    sys.path.insert(0, str(BACKEND_ROOT))

from fastapi.testclient import TestClient

from app.algorithms.catalog import ensure_official_definitions
from app.main import app
from app.services.audit import AuditService
from app.services.projects import ProjectService
from app.services.recordings import RecordingService
from app.services.runs import RunService


def verify_recording(
    *,
    wpf_dir: Path | None = None,
    edf_path: Path | None = None,
    start_s: float = 10.0,
    end_s: float = 70.0,
    ica_n_components: int = 15,
    peak_to_peak_uv: float = 300.0,
    muscle_zscore_threshold: float = 4.0,
) -> dict[str, object]:
    if (wpf_dir is None) == (edf_path is None):
        raise ValueError("Specify exactly one of --wpf-dir or --edf-path")

    with tempfile.TemporaryDirectory(prefix="verify-offline-preproc-", ignore_cleanup_errors=True) as tmp:
        tmp_path = Path(tmp)
        db = tmp_path / "platform.sqlite3"
        recordings = RecordingService(tmp_path / "recordings", db)
        projects = ProjectService(db)
        audit = AuditService(db)
        runs = RunService(recordings, db, tmp_path / "artifacts")
        ensure_official_definitions(runs.definition_service)

        app.state.recording_service = recordings
        app.state.project_service = projects
        app.state.audit_service = audit
        app.state.definition_service = runs.definition_service
        app.state.run_service = runs
        client = TestClient(app)

        if wpf_dir is not None:
            rec = recordings.register_wpf_recording(str(wpf_dir), signal_mode="acquisition_montage")
        else:
            assert edf_path is not None
            uploaded = client.post("/api/recordings/import", files={"file": (edf_path.name, edf_path.read_bytes())})
            uploaded.raise_for_status()
            rec = recordings.require_recording(uploaded.json()["id"])

        dur = float(rec.duration_s or 0.0)
        win_start = min(start_s, max(0.0, dur - 8.0))
        win_end = min(end_s, dur)
        sample_chs = list(rec.channels[:3])
        if any(ch.startswith("O1") for ch in rec.channels):
            o1 = next(ch for ch in rec.channels if ch.startswith("O1"))
            if o1 not in sample_chs:
                sample_chs[-1] = o1

        # 1. Step 1: Bad-channel inspection
        t0 = time.perf_counter()
        r_bad = client.get(f"/api/recordings/{rec.id}/preprocessing/bad-channels", params={"duration_s": min(dur, 180.0)})
        t_bad = round(time.perf_counter() - t0, 3)
        r_bad.raise_for_status()
        bad_body = r_bad.json()

        # 2. Step 2: Artifact-segment inspection
        t0 = time.perf_counter()
        r_art = client.get(
            f"/api/recordings/{rec.id}/preprocessing/artifact-segments",
            params={
                "duration_s": min(dur, 180.0),
                "peak_to_peak_uv": peak_to_peak_uv,
                "muscle_zscore_threshold": muscle_zscore_threshold,
            },
        )
        t_art = round(time.perf_counter() - t0, 3)
        r_art.raise_for_status()
        art_body = r_art.json()

        # 3. Step 3: Full-recording continuous ICA review (aligned with official RBP 1-50 Hz + 50 Hz notch)
        t0 = time.perf_counter()
        r_ica = client.get(
            f"/api/recordings/{rec.id}/preprocessing/ica-components",
            params={
                "low_hz": 1.0,
                "high_hz": 50.0,
                "notch_hz": 50.0,
                "artifact_policy": "reject",
                "peak_to_peak_uv": peak_to_peak_uv,
                "muscle_zscore_threshold": muscle_zscore_threshold,
                "allow_frontal_eog_proxy": "true",
                "ica_n_components": min(ica_n_components, max(2, len(rec.channels) - 1)),
                "ica_eog_threshold": 0.7,
            },
        )
        t_ica = round(time.perf_counter() - t0, 3)
        r_ica.raise_for_status()
        ica_body = r_ica.json()
        fit_fp = ica_body["ica_fit_fingerprint"]
        rec_ics = ica_body["recommended_exclude_components"]

        preproc_payload = {
            "artifact_rejection": {
                "policy": "reject",
                "peak_to_peak_uv": peak_to_peak_uv,
                "muscle_zscore_threshold": muscle_zscore_threshold,
            },
            "artifact_removal": {
                "method": "ica_infomax",
                "allow_frontal_eog_proxy": True,
                "ica_n_components": ica_body["n_components"],
                "ica_eog_threshold": 0.7,
                "ica_exclude_components": rec_ics,
                "ica_fit_fingerprint": fit_fp,
            },
        }

        # 4. Direct configured spectrum endpoint (PSD + RBP)
        r_psd_base = client.post(
            f"/api/recordings/{rec.id}/spectrum/configured",
            json={"mode": "static", "channels": sample_chs, "time": {"start_s": win_start, "end_s": win_end}},
        )
        r_psd_base.raise_for_status()
        t0 = time.perf_counter()
        r_psd_clean = client.post(
            f"/api/recordings/{rec.id}/spectrum/configured",
            json={
                "mode": "static",
                "channels": sample_chs,
                "time": {"start_s": win_start, "end_s": win_end},
                "preprocessing": preproc_payload,
            },
        )
        t_psd = round(time.perf_counter() - t0, 3)
        r_psd_clean.raise_for_status()

        # 5. Formal /api/runs official RBP run (baseline vs preprocessed with verified ICA fingerprint)
        r_run_base = client.post(
            "/api/runs",
            json={
                "recording_id": rec.id,
                "analysis_type": "official_algorithm",
                "config": {
                    "algorithm_id": "rbp",
                    "mode": "static",
                    "channel": sample_chs[0],
                    "channels": sample_chs,
                    "time": {"start_s": win_start, "end_s": win_end},
                    "notch_hz": 50.0,
                },
            },
        )
        r_run_base.raise_for_status()
        t0 = time.perf_counter()
        r_run_clean = client.post(
            "/api/runs",
            json={
                "recording_id": rec.id,
                "analysis_type": "official_algorithm",
                "config": {
                    "algorithm_id": "rbp",
                    "mode": "static",
                    "channel": sample_chs[0],
                    "channels": sample_chs,
                    "time": {"start_s": win_start, "end_s": win_end},
                    "notch_hz": 50.0,
                    "preprocessing": preproc_payload,
                },
            },
        )
        t_run = round(time.perf_counter() - t0, 3)
        r_run_clean.raise_for_status()

        # 6. Fingerprint mismatch guard check
        r_mismatch = client.post(
            f"/api/recordings/{rec.id}/spectrum/configured",
            json={
                "mode": "static",
                "channels": [sample_chs[0]],
                "time": {"start_s": win_start, "end_s": win_end},
                "preprocessing": {
                    "artifact_removal": {
                        "method": "ica_infomax",
                        "allow_frontal_eog_proxy": True,
                        "ica_n_components": ica_body["n_components"],
                        "ica_exclude_components": rec_ics,
                        "ica_fit_fingerprint": fit_fp,
                    },
                },
            },
        )

        # 7. STFT configured endpoint (bounded)
        r_stft = client.post(
            f"/api/recordings/{rec.id}/spectrogram/configured",
            json={
                "mode": "spectrogram",
                "channels": [sample_chs[-1]],
                "time": {"start_s": win_start, "end_s": min(win_start + 30.0, win_end)},
                "preprocessing": {
                    "artifact_rejection": {
                        "policy": "reject",
                        "peak_to_peak_uv": peak_to_peak_uv,
                        "muscle_zscore_threshold": muscle_zscore_threshold,
                    },
                },
            },
        )

        clean_pre = r_psd_clean.json()["filter_contract"]["preprocessing"]
        base_run_struct = r_run_base.json()["result_summary"]["structured"]
        clean_run_struct = r_run_clean.json()["result_summary"]["structured"]
        clean_run_pre = clean_run_struct["spectral_evidence"]["filter_contract"]["preprocessing"]
        return {
            "recording": {
                "id": rec.id,
                "sfreq_hz": rec.sfreq,
                "duration_s": rec.duration_s,
                "channel_count": len(rec.channels),
                "tested_channels": sample_chs,
                "tested_window_s": [win_start, win_end],
            },
            "step1_bad_channels": {
                "elapsed_s": t_bad,
                "recommended_bad_channels": bad_body["recommended_bad_channels"],
            },
            "step2_artifact_segments": {
                "elapsed_s": t_art,
                "clean_ratio": art_body["clean_ratio"],
                "segment_count": len(art_body["segments"]),
                "segments": art_body["segments"],
            },
            "step3_ica_review": {
                "elapsed_s": t_ica,
                "eog_channel_source": ica_body["eog_channel_source"],
                "n_components": ica_body["n_components"],
                "recommended_exclude_components": rec_ics,
                "ica_fit_fingerprint": fit_fp,
                "can_apply_to_continuous": ica_body["can_apply_to_continuous"],
            },
            "direct_configured_spectrum": {
                "elapsed_s": t_psd,
                "ica_model_reused": clean_pre["artifact_removal"]["ica_model_reused"],
                "artifact_segment_count": len(clean_pre["artifact_rejection"]["segments"]),
                "baseline_band_power_uv2": r_psd_base.json()["band_power"],
                "cleaned_band_power_uv2": r_psd_clean.json()["band_power"],
            },
            "formal_official_rbp_run": {
                "elapsed_s": t_run,
                "baseline_run_id": r_run_base.json()["run_id"],
                "baseline_status": r_run_base.json()["status"],
                "cleaned_run_id": r_run_clean.json()["run_id"],
                "cleaned_status": r_run_clean.json()["status"],
                "runs_isolated": r_run_base.json()["run_id"] != r_run_clean.json()["run_id"],
                "ica_model_reused": clean_run_pre["artifact_removal"]["ica_model_reused"],
                "artifact_segment_count": len(clean_run_pre["artifact_rejection"]["segments"]),
                "baseline_band_powers_uv2": base_run_struct["calculation_trace"]["band_powers_uv2"],
                "cleaned_band_powers_uv2": clean_run_struct["calculation_trace"]["band_powers_uv2"],
            },
            "fingerprint_mismatch_guard": {
                "http_status": r_mismatch.status_code,
                "rejected_with_expected_code": "ICA_FIT_FINGERPRINT_MISMATCH" in r_mismatch.text,
            },
            "stft_bounded_check": {
                "http_status": r_stft.status_code,
            },
        }


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify offline EEG preprocessing end-to-end (read-only).")
    parser.add_argument("--wpf-dir", type=Path, default=None, help="Path to a completed WPF recording folder.")
    parser.add_argument("--edf-path", type=Path, default=None, help="Path to an EDF/BDF recording file.")
    parser.add_argument("--start-s", type=float, default=10.0)
    parser.add_argument("--end-s", type=float, default=70.0)
    parser.add_argument("--output-json", type=Path, default=None, help="Optional path to save JSON report.")
    args = parser.parse_args()

    report = verify_recording(
        wpf_dir=args.wpf_dir, edf_path=args.edf_path, start_s=args.start_s, end_s=args.end_s,
    )
    rendered = json.dumps(report, ensure_ascii=False, indent=2)
    if args.output_json is not None:
        args.output_json.parent.mkdir(parents=True, exist_ok=True)
        args.output_json.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)


if __name__ == "__main__":
    main()
