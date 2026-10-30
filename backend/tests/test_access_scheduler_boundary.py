from pathlib import Path


def test_schedulers_do_not_own_recording_file_readers():
    root = Path(__file__).parents[1] / "app"
    scheduler_files = (
        root / "services" / "on_demand_dynamic.py",
        root / "services" / "run_queue.py",
        root / "services" / "scientific_compute_coordinator.py",
        root / "algorithms" / "tnk" / "group.py",
        root / "algorithms" / "tnk" / "legacy_runtime.py",
    )
    forbidden = ("WpfSampleReader", "EdfSampleReader", "open_data_reader", "open_sample_reader", "read_raw_bdf", "read_raw_edf")
    for path in scheduler_files:
        source = path.read_text(encoding="utf-8")
        assert not any(token in source for token in forbidden), path
