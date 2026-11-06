"""Shared SQLite connection policy for the local workstation."""

from __future__ import annotations

import sqlite3
from pathlib import Path
from time import perf_counter

from app.core.performance_trace import current_performance_trace


def connect_database(
    database_path: Path,
    *,
    foreign_keys: bool = False,
    autocommit: bool = False,
) -> sqlite3.Connection:
    """Open one SQLite connection with the platform's common timeout policy.

    Callers opt into foreign-key enforcement because legacy recording services
    intentionally preserve their historical no-cascade behaviour. Migrations
    opt into autocommit so they can explicitly own each ``BEGIN IMMEDIATE``.
    """
    trace = current_performance_trace()
    started = perf_counter() if trace is not None else 0.0
    path = Path(database_path)
    path.parent.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(
        path,
        timeout=5.0,
        isolation_level=None if autocommit else "DEFERRED",
    )
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA busy_timeout = 5000")
    if foreign_keys:
        connection.execute("PRAGMA foreign_keys = ON")
    if trace is not None:
        trace.add("database_connect", (perf_counter() - started) * 1000)
    return connection
