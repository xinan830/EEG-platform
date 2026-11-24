"""Create/replay a private, independently checked nine-algorithm baseline."""

import argparse
import gc
import importlib.metadata
import json
import logging
import os
from pathlib import Path
import platform
from tempfile import TemporaryDirectory

from scripts.regression_recording import replay
from scripts.regression_store import (
    Differences, TOLERANCES, checksum, compare_snapshots, load_baseline, save_baseline, write_json,
)


BACKEND = Path(__file__).resolve().parents[1]
LOCAL = BACKEND.parent / ".artifacts" / "real-recording-regression"


def identity():
    """Track reference/tool bytes separately from the current production implementation."""
    tool_names = ("real_recording_regression.py", "regression_recording.py", "regression_store.py")
    reference_names = ("regression_reference.py", "recording_audit_reference.py")
    production = sorted((BACKEND / "app").rglob("*.py"))
    return dict(python=platform.python_version(), platform=platform.platform(),
                libraries={name: importlib.metadata.version(name) for name in ("numpy", "scipy", "mne", "pydantic")},
                lock_sha256=checksum(BACKEND / "uv.lock"), tolerances=TOLERANCES,
                tool={name: checksum(BACKEND / "scripts" / name) for name in tool_names},
                reference={name: checksum(BACKEND / "scripts" / name) for name in reference_names},
                production={path.relative_to(BACKEND).as_posix(): checksum(path) for path in production})


def resolve_recording(path):
    root = Path(path)
    if (root / "manifest.json").is_file():
        return root
    candidates = sorted((root / "recordings").glob("*/manifest.json"))
    if len(candidates) != 1:
        raise ValueError("Provide one explicit recording directory containing manifest.json")
    return candidates[0].parent


def execute(action, recording, baseline, report, *, progress=print):
    """Never publish baseline data until every independent check passes."""
    if action == "create" and baseline.exists():
        raise FileExistsError("Baseline already exists; overwrite is forbidden")
    expected = load_baseline(baseline) if action == "compare" else None
    report.parent.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(prefix="eeg-regression-") as temporary:
        try:
            snapshot, arrays, independent = replay(recording, Path(temporary), progress=progress)
        finally:
            # sqlite context managers commit but some connection cycles await GC on Windows.
            gc.collect()
    snapshot.update(schema="real-recording-regression-v1", identity=identity())
    result = dict(action=action, independent=independent, source_unchanged=True,
                  scenarios=len(snapshot["scenarios"]), arrays=len(arrays))
    if expected is not None:
        previous, previous_arrays = expected
        identities = Differences()
        identities.tree("identity", snapshot["identity"], previous["identity"])
        result["identity"] = identities.report()
        result["comparison"] = compare_snapshots(
            {key: value for key, value in snapshot.items() if key != "identity"}, arrays,
            {key: value for key, value in previous.items() if key != "identity"}, previous_arrays)
    result["passed"] = independent["passed"] and all(
        result[name]["passed"] for name in ("identity", "comparison") if name in result)
    write_json(report, result)
    if action == "create" and result["passed"]:
        save_baseline(baseline, snapshot, arrays)
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("create", "compare"))
    parser.add_argument("--recording", default=os.environ.get("EEG_REGRESSION_RECORDING"))
    parser.add_argument("--baseline", type=Path, default=LOCAL / "baseline")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args(argv)
    if not args.recording:
        parser.error("Set EEG_REGRESSION_RECORDING or provide --recording")
    report = args.report or LOCAL / (args.action + "-report.json")
    logging.disable(logging.CRITICAL)
    try:
        result = execute(args.action, resolve_recording(args.recording), args.baseline, report)
    except Exception as error:
        report.parent.mkdir(parents=True, exist_ok=True)
        # Error text may embed personal paths from dependencies; store the class only.
        write_json(report, dict(passed=False, error=type(error).__name__, stage="replay_or_storage"))
        print(f"FAIL ({type(error).__name__}); no baseline was replaced")
        return 2
    print(json.dumps({key: result[key] for key in ("passed", "scenarios", "arrays")}))
    print(f"Independent failed checks: {result['independent']['failed']}")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
