"""Capture current synthetic fixtures only after independent and historical checks."""

import copy
import gc
import hashlib
import json
from pathlib import Path
import platform
import sys
from tempfile import TemporaryDirectory

import numpy as np
import scipy

BACKEND = Path(__file__).resolve().parents[1]
ROOT = BACKEND.parent
sys.path.insert(0, str(BACKEND / "tests"))

from result_boundary_support import (
    ALGORITHM_IDS, canonical_json, snapshot_run, validate_current_scalar_reference,
)
from test_official_strategy_validation_matrix import _contract_snapshot


def checksum(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def capture():
    fixtures = BACKEND / "tests/fixtures"
    report_path = fixtures / "algorithm-result-boundary-20261127.json"
    artifact_path = report_path.with_suffix(".npz")
    matrix_path = fixtures / "official-algorithm-contracts-20261127.json"
    if any(path.exists() for path in (report_path, artifact_path, matrix_path)):
        raise FileExistsError("Refusing to overwrite frozen evidence")
    historical_path = ROOT / "docs/validation/algorithm-result-boundary-v4-baseline.json"
    historical = json.loads(historical_path.read_text(encoding="utf-8"))
    previous_artifact = ROOT / historical["artifact"]["path"]
    assert checksum(previous_artifact) == historical["artifact"]["sha256"]
    unchanged = {"band_ratio", "faa", "peak_frequency", "psd", "rbp", "stft"}
    cases, values = [], {}
    with np.load(previous_artifact, allow_pickle=False) as previous:
        for identifier in ALGORITHM_IDS:
            for mode in ("static", "dynamic"):
                for rejected in (False, True):
                    key = f"{identifier}__{mode}__" + ("rejected" if rejected else "clean")
                    with TemporaryDirectory() as temporary:
                        try:
                            snapshot, arrays = snapshot_run(Path(temporary), identifier, mode, rejected)
                            validate_current_scalar_reference(snapshot, arrays)
                            if identifier in unchanged:
                                for name, array in arrays.items():
                                    if array.dtype.kind not in "US":
                                        np.testing.assert_allclose(array, previous[key + "__" + name],
                                                                   **historical["tolerances"], equal_nan=True)
                            values[key + "__snapshot_json"] = np.array(canonical_json(snapshot))
                            values.update({key + "__" + name: array for name, array in arrays.items()})
                            cases.append(dict(case=key, algorithm_id=identifier, mode=mode,
                                              rejected=rejected, array_keys=sorted(arrays)))
                            print(key, "validated", flush=True)
                        finally:
                            gc.collect()
    with artifact_path.open("xb") as stream:
        np.savez_compressed(stream, **values)
    report = dict(
        schema="algorithm-result-boundary-current-v1", scope=historical["scope"],
        capture="Current Run/artifact contracts with independent scalar and unchanged historical array checks.",
        environment=dict(python=platform.python_version(), numpy=np.__version__, scipy=scipy.__version__),
        tolerances=historical["tolerances"],
        historical_baseline=dict(path=historical_path.relative_to(ROOT).as_posix(),
                                 sha256=checksum(historical_path),
                                 artifact_sha256=checksum(previous_artifact), preserved=True),
        changes={
            "config": "Added frequency_mode and preprocessing fields; exact resolved config digest retained.",
            "iapf": "Validated alpha peak/COG diagnostics and complete 20-second dynamic windows.",
            "theta_beta": "Standard 4-8 / 13-30 Hz bands and complete 20-second dynamic windows.",
            "brainbeat": "Classic absolute Fz 4-8 / Pz 8-12 Hz; separate individualized algorithm ID.",
            "rbp": "Private custom-frequency projection-source evidence and Definition identity.",
            "preprocessing": "Structured offline-preprocessing evidence on bounded/rejected outputs.",
        },
        independent_reference="backend/scripts/recording_audit_reference.py",
        artifact=dict(path=artifact_path.relative_to(ROOT).as_posix(), sha256=checksum(artifact_path)),
        cases=cases,
    )
    old_matrix = ROOT / "docs/validation/official-algorithm-strategy-matrix.json"
    matrix = json.loads(old_matrix.read_text(encoding="utf-8"))
    matrix.update(schema="official-algorithm-strategy-matrix-v2",
                  historical_matrix_sha256=checksum(old_matrix), contract_snapshot=_contract_snapshot())
    matrix["algorithms"]["brainbeat_individualized"] = copy.deepcopy(matrix["algorithms"]["brainbeat"])
    for identifier in ("brainbeat", "brainbeat_individualized", "iapf", "theta_beta"):
        for name in ("full", "on_demand"):
            tests = matrix["algorithms"][identifier]["paths"][name]["tests"]
            tests.extend(["backend/tests/test_real_recording_regression.py",
                          "backend/tests/test_result_boundary_baseline.py"])
    for path, value in ((report_path, report), (matrix_path, matrix)):
        with path.open("x", encoding="utf-8") as stream:
            json.dump(value, stream, indent=2, allow_nan=False)
            stream.write("\n")
    print(f"Captured {len(cases)} independently checked cases")


if __name__ == "__main__":
    capture()
