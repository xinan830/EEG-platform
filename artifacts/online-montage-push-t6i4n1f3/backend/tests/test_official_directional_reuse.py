"""Prove inverse reuse against uncached arrays, including invalid evidence."""

import hashlib
import json
import sqlite3

import pytest

from combined_strategy_support import compare_arrays, compare_summary, ensure_preview, setup_strategy, uncached_full, validation_context
from test_on_demand_psd_api import wait_window


@pytest.mark.parametrize("algorithm_id", ["psd", "stft"])
@pytest.mark.parametrize("damage", [None, "missing_manifest", "cache_key"])
def test_on_demand_to_full_matches_uncached_execution(tmp_path, algorithm_id, damage, record_property):
    services, config = setup_strategy(tmp_path, algorithm_id)
    client, recording, windows, runs, _ = services
    try:
        request, expected, arrays = uncached_full(runs, recording, config)
        ensured = ensure_preview(client, recording, config)
        preview = wait_window(client, ensured["window_id"])
        assert preview["state"] == "ready"
        if damage:
            row = windows.repository.get_window(ensured["window_id"])
            path = windows.artifacts_dir / f"{row.result_artifact_id}.json"
            metadata = json.loads(path.read_text(encoding="utf-8"))
            if damage == "cache_key":
                metadata["provenance"]["scientific_cache_key"] = "invalid-identity"
                path.write_text(json.dumps(metadata), encoding="utf-8")
            digest = None if damage == "missing_manifest" else hashlib.sha256(path.read_bytes()).hexdigest()
            with sqlite3.connect(windows.repository.database_path) as connection:
                connection.execute("UPDATE dynamic_analysis_windows SET result_manifest_sha256 = ? WHERE window_id = ?",
                                   (digest, row.window_id))
        runs.enqueue(request)
        full = runs.process_next()
        assert full.status.value == "completed", full.error
        provenance = full.result_summary["execution_provenance"]
        assert provenance["reused_on_demand_window_count"] == (0 if damage else 1)
        if damage:
            reason = "manifest_unverified" if damage == "missing_manifest" else "cache_key_mismatch"
            assert provenance["cache_miss_reasons"][reason] == 1
        artifact = runs.list_artifacts(full.run_id)[0]
        record_property("validation_context", json.dumps(validation_context(recording, config, full, artifact), sort_keys=True))
        record_property("array_comparison", json.dumps(compare_arrays(runs.base.artifacts.read_npz(artifact), arrays), sort_keys=True))
        compare_summary(full.result_summary, expected)
    finally:
        windows.close()
        client.close()
