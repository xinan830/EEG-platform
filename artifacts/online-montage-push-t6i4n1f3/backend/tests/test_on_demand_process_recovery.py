"""Real worker-process termination, not two simulated in-process services."""

import json
import sqlite3
import subprocess
import sys
import time
from pathlib import Path

import pytest

from on_demand_fixtures import configure_api, source_hashes


@pytest.mark.parametrize("invalidate_identity", [False, True])
def test_killed_process_recovers_without_new_ensure_or_fails_changed_source(tmp_path, invalidate_identity):
    client, recording, windows, _, root = configure_api(tmp_path)
    windows.close()
    windows._executor.shutdown(wait=True)
    client.close()
    before = source_hashes(root)
    probe = Path(__file__).with_name("on_demand_process_probe.py")
    crashed = subprocess.Popen([sys.executable, str(probe), str(tmp_path), recording.id, "block"],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        deadline = time.monotonic() + 20
        window_id = None
        while time.monotonic() < deadline:
            with sqlite3.connect(windows.repository.database_path) as connection:
                row = connection.execute("SELECT window_id FROM dynamic_analysis_windows WHERE state='preparing_source'").fetchone()
            if row:
                window_id = row[0]
                break
            assert crashed.poll() is None, crashed.communicate()
            time.sleep(0.02)
        assert window_id is not None
        crashed.kill()
        crashed.communicate(timeout=10)
        assert crashed.returncode != 0
        with sqlite3.connect(windows.repository.database_path) as connection:
            old_attempt = connection.execute("SELECT attempt_id FROM dynamic_analysis_attempts WHERE state='running'").fetchone()[0]
            # Advance expiry deterministically after actual OS process death;
            # production's five-minute lease duration is not changed.
            connection.execute("UPDATE dynamic_analysis_attempts SET lease_expires_at='2000-01-01T00:00:00+00:00'")
            if invalidate_identity:
                connection.execute("UPDATE recordings SET source_sha256='changed-source' WHERE id=?", (recording.id,))
        recovered = subprocess.run([sys.executable, str(probe), str(tmp_path), recording.id, "recover", window_id],
            capture_output=True, text=True, timeout=30)
        assert recovered.returncode == 0, recovered.stderr
        response = json.loads(recovered.stdout.strip().splitlines()[-1])
        assert response["window_id"] == window_id
        assert response["identity"]["generation"] == 0
        if invalidate_identity:
            assert response["state"] == "failed"
            assert response["failure"]["code"] == "WINDOW_RECOVERY_IDENTITY_INVALID"
            assert response["result"] is None
        else:
            assert response["state"] == "ready"
            assert response["result"]["arrays"]["psd"]
        with sqlite3.connect(windows.repository.database_path) as connection:
            assert connection.execute("SELECT state FROM dynamic_analysis_attempts WHERE attempt_id=?", (old_attempt,)).fetchone()[0] == "expired"
            assert connection.execute("SELECT COUNT(*) FROM dynamic_analysis_subscriptions WHERE state='active'").fetchone()[0] == 1
        assert source_hashes(root) == before
    finally:
        if crashed.poll() is None:
            crashed.kill()
            crashed.communicate(timeout=10)
