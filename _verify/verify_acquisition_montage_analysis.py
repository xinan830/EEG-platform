"""Read-only real-recording verification; derived artifacts stay in a temp folder."""
import json
import gc
import sys
import tempfile
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))
from app.models.run import RunCreateRequest
from app.services.recordings import RecordingService
from app.services.runs import RunService


def verify(directory: str):
    with tempfile.TemporaryDirectory(prefix="montage-analysis-") as temporary:
        root = Path(temporary)
        service = RecordingService(root / "stored", root / "catalog.sqlite3")
        raw = service.register_wpf_recording(directory)
        derived = service.register_wpf_recording(directory, signal_mode="acquisition_montage")
        source = service.analysis_signal_provenance(derived)
        raw_reader, reader = service.open_sample_reader(raw), service.open_sample_reader(derived)
        try:
            raw_values = raw_reader.read(0, min(1000, raw_reader.sample_count))
            values = reader.read(0, min(1000, reader.sample_count))
            for index, formula in enumerate(source["formulas"]):
                positive = raw_values[:, raw_reader.names.index(formula["positive"])]
                negative = formula["negative_labels"]
                expected = positive if not negative else positive - raw_values[:, [raw_reader.names.index(name) for name in negative]].mean(axis=1)
                np.testing.assert_allclose(values[:, index], expected, atol=0, rtol=0, equal_nan=True)
        finally:
            raw_reader.close()
            reader.close()
        runs = RunService(service, service.database_path, root / "artifacts")
        evidence = {"recording": Path(directory).name, "duration_s": derived.duration_s,
                    "raw_channels": len(raw.channels), "analysis_outputs": list(derived.channels),
                    "projection_numeric_match": True, "runs": []}
        for algorithm_id in ("psd", "stft", "rbp", "theta_beta", "iapf", "peak_frequency"):
            for mode in ("static", "dynamic"):
                run = runs.create(RunCreateRequest(recording_id=derived.id, analysis_type="official_algorithm", config={
                    "algorithm_id": algorithm_id, "channel": derived.channels[0], "mode": mode,
                    "time": {"start_s": 0, "end_s": min(12, derived.duration_s)},
                    "dynamic_window_s": 5, "refresh_step_s": 1,
                }))
                assert run.status in {"completed", "gate_failed"}, run.error
                assert run.reference["mode"] == "acquisition_montage_snapshot"
                evidence["runs"].append({"algorithm": algorithm_id, "mode": mode, "status": run.status.value})
        print(json.dumps(evidence, ensure_ascii=False, indent=2))
        del runs, service
        gc.collect()


if __name__ == "__main__":
    verify(sys.argv[1])
