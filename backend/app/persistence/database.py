"""Shared SQLite connection policy for the local workstation."""

from __future__ import annotations

import sqlite3
from pathlib import Path


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
    return connection
