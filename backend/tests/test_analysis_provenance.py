from app.models.run import AnalysisRun, RunStatus, StructuredRunError
from app.services.analysis_provenance import build_analysis_provenance, serialize_analysis_run


def _run(*, dynamic: bool = False, failed: bool = False) -> AnalysisRun:
    evidence = {
        "sfreq_hz": 500.0,
        "analysis_reference": "original_recording_no_software_rereference",
        "algorithm_version": "offline-spectral-v3",
        "filter_contract": {"bandpass_hz": [1.0, 30.0], "preprocessing_phase": "zero_phase"},
        "welch_contract": {
            "welch_segment_s": 4.0,
            "welch_segment_overlap": 0.5,
            "welch_step_s": 2.0,
            "welch_window": "hann",
        },
        "frequencies_hz": [1.0, 1.25, 30.0],
        "band_power": {"delta": 1.0, "theta": 2.0, "alpha": 3.0, "beta": 4.0},
        "relative_band_power": {"delta": 0.1, "theta": 0.2, "alpha": 0.3, "beta": 0.4},
        "quality": {"clean_segments": 4, "total_segments": 4, "clean_ratio": 1.0, "gate_failed": False},
    }
    metric = {
        "channel": "F3",
        "actual_range": {"start_s": 0.0, "end_s": 10.0},
        "inputs": {"theta": {"feature": "theta_power", "value": 2.0, "unit": "uV^2", "channel": "F3"}},
        "output": {"id": "output", "label": "Theta/Beta 比值", "value": 0.5, "unit": "dimensionless"},
        "spectral_evidence": evidence,
    }
    if dynamic:
        metric = {
            "mode": "dynamic",
            "channel": "F3",
            "actual_range": {"start_s": 0.0, "end_s": 30.0},
            "output": {"id": "output", "label": "Theta/Beta 比值", "unit": "dimensionless"},
            "series": [{
                "time_s": 15.0,
                "window_start_s": 5.0,
                "window_end_s": 15.0,
                "value": 0.5,
                "quality": {"status": "clean"},
                "inputs": {"theta": {"feature": "theta_power", "value": 2.0, "unit": "uV^2", "channel": "F3"}},
                "source_quality": evidence["quality"],
                "spectral_evidence": evidence,
            }],
        }
    return AnalysisRun(
        run_id="run-1", recording_id="recording-1", analysis_type="definition_metric",
        status=RunStatus.FAILED if failed else RunStatus.COMPLETED,
        definition_id="definition-1", definition_version="1.0.0",
        scientific_version="definition-engine-v1", implementation_version="build-1",
        config={"channel": "F3", "mode": "dynamic" if dynamic else "static"},
        config_sha256="config-sha", cache_key="cache-key",
        requested_range={"start_s": 0.0, "end_s": 30.0 if dynamic else 10.0},
        actual_range=None if failed else {"start_s": 0.0, "end_s": 30.0 if dynamic else 10.0},
        channel_mapping={"channels": ["F3"]}, reference={"mode": "original"}, filters={}, window={}, quality_rules={},
        environment={"python": "3.13"}, result_summary=None if failed else {"metric": metric},
        error=StructuredRunError(code="QUALITY_GATE_FAILED", message="quality failed", stage="quality") if failed else None,
        created_at="2026-09-15T00:00:00Z", updated_at="2026-09-15T00:00:01Z",
    )


def test_static_metric_run_has_backend_authored_provenance():
    provenance = build_analysis_provenance(_run())

    assert provenance["contract_version"] == "analysis-provenance-v1"
    assert provenance["actual_range"] == {"start_s": 0.0, "end_s": 10.0}
    assert provenance["welch"] == {"segment_s": 4.0, "window": "hann", "overlap_fraction": 0.5, "step_s": 2.0}
    assert provenance["extensions"][0]["kind"] == "spectral_band_power"
    assert provenance["extensions"][1]["kind"] == "metric_inputs_output"
    assert serialize_analysis_run(_run())["analysis_provenance"] == provenance


def test_dynamic_metric_run_uses_latest_completed_point_evidence():
    provenance = build_analysis_provenance(_run(dynamic=True))

    assert provenance["mode"] == "dynamic"
    assert provenance["actual_range"] == {"start_s": 5.0, "end_s": 15.0}
    assert provenance["quality"] == {"clean_segments": 4, "total_segments": 4, "clean_ratio": 1.0, "gate_failed": False}
    assert provenance["extensions"][1]["data"]["output"]["value"] == 0.5


def test_failed_run_provenance_keeps_missing_evidence_null():
    provenance = build_analysis_provenance(_run(failed=True))

    assert provenance["status"] == "failed"
    assert provenance["quality"] is None
    assert provenance["welch"] is None
    assert provenance["extensions"] == []


def test_faa_provenance_keeps_paired_quality_evidence():
    run = _run()
    run.result_summary = {
        "metric": {
            "channel": "F3/F4",
            "actual_range": {"start_s": 0.0, "end_s": 30.0},
            "output": {"id": "faa", "label": "额叶 Alpha 不对称性", "value": 0.2, "unit": "dimensionless"},
            "source_quality": {"clean_segments": 12, "total_segments": 14, "clean_ratio": 12 / 14},
            "official": {"faa_evidence": {"channels": ["F3", "F4"], "clean_epochs": 12, "total_epochs": 14, "clean_ratio": 12 / 14, "band": [8.0, 13.0], "reason": "", "sfreq_hz": 500.0}},
        },
    }

    provenance = build_analysis_provenance(run)

    assert provenance["channel"] == "F3/F4"
    assert provenance["sfreq_hz"] == 500.0
    assert provenance["extensions"][-1]["kind"] == "faa_paired_quality"
