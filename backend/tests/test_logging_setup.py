from __future__ import annotations

import logging
from pathlib import Path

from app.core.config import STORAGE_DIR
from app.core.logging_setup import configure_logging


def test_backend_logging_writes_bounded_dated_file(tmp_path: Path):
    try:
        path = configure_logging(tmp_path, force=True)
        assert path is not None
        assert path.parent == tmp_path / "logs"
        logger = logging.getLogger("brain_platform.test")
        logger.info("diagnostic_test request_id=test-42")
        assert path.exists()
        assert "request_id=test-42" in path.read_text(encoding="utf-8")
    finally:
        configure_logging(STORAGE_DIR, force=True)
