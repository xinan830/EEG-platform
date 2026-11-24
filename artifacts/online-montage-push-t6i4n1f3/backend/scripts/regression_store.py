"""Immutable local snapshots and diagnostic comparisons for EEG regression."""

import hashlib
import json
from pathlib import Path

import numpy as np


TOLERANCES = {"rtol": 1e-8, "scalar_atol": 1e-12,
              "density_atol": 1e-22, "db_atol": 1e-8, "signal_atol": 1e-18}


def checksum(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, sort_keys=True,
                               indent=2, allow_nan=False), encoding="utf-8")


class Differences:
    """Accumulate exact structure checks and numerical errors with coordinates."""

    def __init__(self):
        self.rows = []

    def numeric(self, path, actual, expected, *, atol=None, rtol=None):
        atol = TOLERANCES["scalar_atol"] if atol is None else atol
        rtol = TOLERANCES["rtol"] if rtol is None else rtol
        left, right = np.asarray(actual, dtype=float), np.asarray(expected, dtype=float)
        row = {"path": path, "points": int(right.size), "failed": 0,
               "max_abs": 0., "max_rel": 0., "rtol": rtol, "atol": atol}
        if left.shape != right.shape:
            row.update(failed=max(1, int(right.size)), actual_shape=list(left.shape),
                       expected_shape=list(right.shape))
        else:
            bad = ~np.isclose(left, right, rtol=rtol, atol=atol, equal_nan=True)
            row["failed"] = int(bad.sum())
            row["failed_coordinates"] = np.argwhere(bad)[:20].tolist()
            finite = np.isfinite(left) & np.isfinite(right)
            if finite.any():
                errors = np.abs(left[finite] - right[finite])
                row["max_abs"] = float(errors.max())
                row["max_rel"] = float((errors / np.maximum(np.abs(right[finite]), 1e-300)).max())
        self.rows.append(row)

    def exact(self, path, actual, expected):
        self.rows.append({"path": path, "points": 1, "failed": int(actual != expected)})

    def tree(self, path, actual, expected):
        if isinstance(expected, dict) and isinstance(actual, dict):
            self.exact(path + ".keys", sorted(actual), sorted(expected))
            for key in sorted(actual.keys() & expected.keys()):
                self.tree(f"{path}.{key}", actual[key], expected[key])
        elif isinstance(expected, list) and isinstance(actual, list):
            if expected and all(type(item) in (int, float) for item in expected) and all(type(item) in (int, float) for item in actual):
                exact_axis = any(name in path for name in ("frequencies_hz", "time_center_s"))
                self.numeric(path, actual, expected, atol=0 if exact_axis else None,
                             rtol=0 if exact_axis else None)
                return
            self.exact(path + ".length", len(actual), len(expected))
            for i, (left, right) in enumerate(zip(actual, expected)):
                self.tree(f"{path}[{i}]", left, right)
        elif isinstance(expected, (float, np.floating)) and isinstance(actual, (int, float, np.floating)):
            exact = any(path.endswith(name) for name in ("start_s", "end_s", "time_s", "sfreq_hz"))
            atol = TOLERANCES["density_atol"] if path.endswith(("power_v2", ".p_f3", ".p_f4")) else None
            self.numeric(path, actual, expected, atol=0 if exact else atol, rtol=0 if exact else None)
        else:
            self.exact(path, actual, expected)

    def report(self):
        return {"passed": not any(row["failed"] for row in self.rows),
                "points": sum(row["points"] for row in self.rows),
                "failed": sum(row["failed"] for row in self.rows), "checks": self.rows}


def array_atol(key):
    if key.endswith(("/psd", "/power_linear")):
        return TOLERANCES["density_atol"]
    if key.endswith("/power_db"):
        return TOLERANCES["db_atol"]
    if "/axis_" in key or key.endswith("/metric_time_s"):
        return 0.
    return TOLERANCES["scalar_atol"]


def save_baseline(directory: Path, snapshot: dict, arrays: dict) -> None:
    """Exclusive creation; never replace an existing baseline, even partially."""
    directory.mkdir(parents=True, exist_ok=False)
    np.savez_compressed(directory / "arrays.npz", **arrays)
    write_json(directory / "snapshot.json", snapshot)
    write_json(directory / "checksums.json", {
        name: checksum(directory / name) for name in ("arrays.npz", "snapshot.json")})


def load_baseline(directory: Path):
    checksums = json.loads((directory / "checksums.json").read_text(encoding="utf-8"))
    if set(checksums) != {"arrays.npz", "snapshot.json"}:
        raise ValueError("Invalid baseline checksum inventory")
    for name, expected in checksums.items():
        if checksum(directory / name) != expected:
            raise ValueError(f"Baseline checksum mismatch: {name}")
    snapshot = json.loads((directory / "snapshot.json").read_text(encoding="utf-8"))
    with np.load(directory / "arrays.npz", allow_pickle=False) as archive:
        arrays = {key: archive[key].copy() for key in archive.files}
    return snapshot, arrays


def compare_snapshots(actual, arrays, expected, expected_arrays):
    differences = Differences()
    differences.tree("snapshot", actual, expected)
    differences.exact("arrays.keys", sorted(arrays), sorted(expected_arrays))
    for key in sorted(arrays.keys() & expected_arrays.keys()):
        differences.exact(key + ".dtype", str(arrays[key].dtype), str(expected_arrays[key].dtype))
        atol = array_atol(key)
        differences.numeric(key, arrays[key], expected_arrays[key], atol=atol, rtol=0 if atol == 0 else None)
    return differences.report()
