import sqlite3

from app.persistence.migrations import CURRENT_SCHEMA_VERSION, MIGRATIONS, migrate_database


def test_on_demand_migration_creates_isolated_tables_and_is_idempotent(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    assert migrate_database(database) == CURRENT_SCHEMA_VERSION
    assert migrate_database(database) == CURRENT_SCHEMA_VERSION

    with sqlite3.connect(database) as connection:
        tables = {
            row[0]
            for row in connection.execute(
                "SELECT name FROM sqlite_master WHERE type = 'table'"
            )
        }
        assert {
            "dynamic_analysis_sessions",
            "dynamic_analysis_windows",
            "dynamic_analysis_attempts",
            "dynamic_analysis_subscriptions",
        } <= tables
        columns = {
            row[1]
            for row in connection.execute("PRAGMA table_info(dynamic_analysis_windows)")
        }
        assert {"window_id", "session_id", "start_sample", "end_sample", "state", "result_artifact_id"} <= columns


def test_on_demand_window_range_and_identity_constraints_are_database_enforced(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("INSERT INTO recordings(id, original_name, stored_name, extension, created_at) VALUES ('r', 'r', 'r', '.edf', 'now')")
        connection.execute("""INSERT INTO dynamic_analysis_sessions(
            session_id, recording_id, source_sha256, algorithm_id, definition_id,
            definition_version, scientific_version, implementation_identity,
            configuration_sha256, configuration_json, window_policy_version,
            status, created_at, updated_at
        ) VALUES ('s', 'r', 'source', 'psd', 'definition', '1', 'v1', 'runner', 'config', '{}', 'policy-v1', 'queued', 'now', 'now')""")
        connection.execute("""INSERT INTO dynamic_analysis_windows(
            window_id, session_id, start_sample, end_sample, start_s, end_s,
            state, quality, created_at, updated_at
        ) VALUES ('w', 's', 0, 10, 0, 1, 'queued', 'pending', 'now', 'now')""")
        try:
            connection.execute("""INSERT INTO dynamic_analysis_windows(
                window_id, session_id, start_sample, end_sample, start_s, end_s,
                state, quality, created_at, updated_at
            ) VALUES ('w2', 's', 0, 10, 0, 1, 'queued', 'pending', 'now', 'now')""")
            raise AssertionError("duplicate scientific window range was accepted")
        except sqlite3.IntegrityError:
            pass


def test_v13_database_upgrades_to_on_demand_schema_without_changing_old_tables(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrations_v13 = tuple(migration for migration in MIGRATIONS if migration.version <= 13)
    assert migrate_database(database, migrations=migrations_v13) == 13
    with sqlite3.connect(database) as connection:
        old_columns = {row[1] for row in connection.execute("PRAGMA table_info(analysis_runs)")}
    assert migrate_database(database) == CURRENT_SCHEMA_VERSION
    with sqlite3.connect(database) as connection:
        assert {row[1] for row in connection.execute("PRAGMA table_info(analysis_runs)")} == old_columns
        assert connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'dynamic_analysis_windows'"
        ).fetchone() is not None


def test_v14_database_receives_the_attempt_lease_index_in_v15(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrations_v14 = tuple(migration for migration in MIGRATIONS if migration.version <= 14)
    assert migrate_database(database, migrations=migrations_v14) == 14
    migrations_v15 = tuple(migration for migration in MIGRATIONS if migration.version <= 15)
    assert migrate_database(database, migrations=migrations_v15) == 15
    with sqlite3.connect(database) as connection:
        assert connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name = 'idx_dynamic_attempts_one_running'"
        ).fetchone() is not None
        try:
            connection.execute("""INSERT INTO dynamic_analysis_windows(
                window_id, session_id, start_sample, end_sample, start_s, end_s,
                state, quality, created_at, updated_at
            ) VALUES ('invalid', 's', 10, 10, 1, 1, 'queued', 'pending', 'now', 'now')""")
            raise AssertionError("empty window range was accepted")
        except sqlite3.IntegrityError:
            pass
    assert migrate_database(database) == CURRENT_SCHEMA_VERSION


def test_v16_database_adds_lease_fencing_and_one_active_attempt_index(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrations_v15 = tuple(migration for migration in MIGRATIONS if migration.version <= 15)
    assert migrate_database(database, migrations=migrations_v15) == 15
    assert migrate_database(database) == CURRENT_SCHEMA_VERSION
    with sqlite3.connect(database) as connection:
        columns = {row[1] for row in connection.execute("PRAGMA table_info(dynamic_analysis_attempts)")}
        assert "lease_token" in columns
        assert connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name = 'idx_dynamic_attempts_one_active'"
        ).fetchone() is not None


def test_v17_preserves_existing_window_attempt_and_subscription_foreign_keys(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database, migrations=tuple(item for item in MIGRATIONS if item.version <= 16))
    with sqlite3.connect(database) as connection:
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("INSERT INTO recordings(id, original_name, stored_name, extension, created_at) VALUES ('r', 'r', 'r', '.edf', 'now')")
        connection.execute("""INSERT INTO dynamic_analysis_sessions(
            session_id, recording_id, source_sha256, algorithm_id, definition_id,
            definition_version, scientific_version, implementation_identity,
            configuration_sha256, configuration_json, window_policy_version,
            status, created_at, updated_at
        ) VALUES ('s', 'r', 'hash', 'psd', 'definition', '1', 'v1', 'impl', 'config', '{}', 'policy', 'queued', 'now', 'now')""")
        connection.execute("""INSERT INTO dynamic_analysis_windows(
            window_id, session_id, start_sample, end_sample, start_s, end_s,
            state, quality, result_artifact_id, created_at, updated_at
        ) VALUES ('w', 's', 0, 100, 0, 1, 'ready', 'clean', 'artifact', 'now', 'now')""")
        connection.execute("""INSERT INTO dynamic_analysis_attempts(
            attempt_id, window_id, request_id, state, created_at, updated_at
        ) VALUES ('a', 'w', 'request', 'succeeded', 'now', 'now')""")
        connection.execute("""INSERT INTO dynamic_analysis_subscriptions(
            subscription_id, window_id, request_id, state, created_at, updated_at
        ) VALUES ('sub', 'w', 'request', 'active', 'now', 'now')""")
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        assert connection.execute("PRAGMA foreign_key_check").fetchall() == []
        assert connection.execute("SELECT generation, state, result_artifact_id FROM dynamic_analysis_windows WHERE window_id='w'").fetchone() == (0, "ready", "artifact")
        assert connection.execute("SELECT window_id FROM dynamic_analysis_attempts WHERE attempt_id='a'").fetchone() == ("w",)
        assert connection.execute("SELECT window_id FROM dynamic_analysis_subscriptions WHERE subscription_id='sub'").fetchone() == ("w",)


def test_v19_preserves_populated_sessions_generations_checksums_and_live_leases(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database, migrations=tuple(item for item in MIGRATIONS if item.version <= 18))
    with sqlite3.connect(database) as connection:
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("INSERT INTO recordings(id, original_name, stored_name, extension, created_at) VALUES ('r', 'r', 'r', '.edf', 'now')")
        connection.execute("""INSERT INTO dynamic_analysis_sessions(
            session_id, recording_id, source_sha256, algorithm_id, definition_id,
            definition_version, scientific_version, implementation_identity,
            configuration_sha256, configuration_json, window_policy_version,
            status, created_at, updated_at
        ) VALUES ('s', 'r', 'hash', 'psd', 'definition', '1', 'v1', 'impl', 'config', '{}', 'policy', 'queued', 'now', 'now')""")
        for window, generation, state in [("ready", 1, "ready"), ("pending", 0, "preparing_source")]:
            connection.execute("""INSERT INTO dynamic_analysis_windows(
                window_id, session_id, generation, start_sample, end_sample, start_s, end_s,
                state, quality, result_artifact_id, result_manifest_sha256, created_at, updated_at
            ) VALUES (?, 's', ?, 0, 100, 0, 1, ?, 'clean', ?, ?, 'now', 'now')""",
                (window, generation, state, "artifact" if state == "ready" else None, "checksum" if state == "ready" else None))
        connection.execute("""INSERT INTO dynamic_analysis_attempts(
            attempt_id, window_id, request_id, state, worker_id, lease_token, lease_expires_at, created_at, updated_at
        ) VALUES ('a', 'pending', 'request', 'running', 'worker', 'token', '2099-01-01T00:00:00+00:00', 'now', 'now')""")
        connection.execute("""INSERT INTO dynamic_analysis_subscriptions(
            subscription_id, window_id, request_id, state, created_at, updated_at
        ) VALUES ('sub', 'pending', 'request', 'active', 'now', 'now')""")
        snapshots = {table: connection.execute(f"SELECT * FROM {table}").fetchall() for table in
            ("dynamic_analysis_windows", "dynamic_analysis_attempts", "dynamic_analysis_subscriptions")}
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        assert connection.execute("PRAGMA foreign_key_check").fetchall() == []
        for table, rows in snapshots.items():
            current = connection.execute(f"SELECT * FROM {table}").fetchall()
            if table != "dynamic_analysis_attempts":
                assert current == rows
                continue
            # Migration 20 appends explicit producer metadata. The legacy
            # lease columns must remain byte-for-byte readable, while old
            # attempts receive the documented safe defaults.
            assert [row[:len(rows[0])] for row in current] == rows
            assert current[0][-3:] == ("on_demand", None, 0)
        assert connection.execute("SELECT definition_sha256, implementation_build FROM dynamic_analysis_sessions").fetchone() == ("", "")
    assert migrate_database(database) == CURRENT_SCHEMA_VERSION
