"""Bounded, file-backed diagnostics for the local scientific engine."""

from __future__ import annotations

import logging
import os
import sys
from datetime import datetime
from pathlib import Path
from logging.handlers import RotatingFileHandler
import re


LOGGER_NAME = "brain_platform"
MAX_LOG_BYTES = 10 * 1024 * 1024
BACKUP_COUNT = 5
_WINDOWS_PATH = re.compile(r"(?i)\b[A-Z]:\\[^\r\n]*")


class _RedactingFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        return _WINDOWS_PATH.sub("<path>", super().format(record))


def configure_logging(storage_root: Path, *, force: bool = False) -> Path | None:
    """Configure application diagnostics and return the current log path.

    The file handler is deliberately scoped to the ``brain_platform`` logger so
    importing the app does not unexpectedly replace uvicorn or test handlers.
    """
    logger = logging.getLogger(LOGGER_NAME)
    if force:
        for handler in list(logger.handlers):
            logger.removeHandler(handler)
            handler.close()
    logger.setLevel(os.environ.get("BRAIN_LOG_LEVEL", "INFO").upper())
    logger.propagate = False
    if logger.handlers:
        return next((Path(handler.baseFilename) for handler in logger.handlers
                     if isinstance(handler, RotatingFileHandler)), None)

    formatter = _RedactingFormatter(
        "%(asctime)s %(levelname)s %(name)s %(message)s",
        datefmt="%Y-%m-%dT%H:%M:%S%z",
    )
    stream = logging.StreamHandler(sys.stderr)
    stream.setFormatter(formatter)
    logger.addHandler(stream)

    try:
        log_dir = Path(storage_root) / "logs"
        log_dir.mkdir(parents=True, exist_ok=True)
        log_path = log_dir / f"backend-{datetime.now().strftime('%Y%m%d')}.log"
        file_handler = RotatingFileHandler(
            log_path,
            maxBytes=MAX_LOG_BYTES,
            backupCount=BACKUP_COUNT,
            encoding="utf-8",
        )
        file_handler.setFormatter(formatter)
        logger.addHandler(file_handler)
        return log_path
    except (OSError, ValueError):
        logger.warning("backend_log_file_unavailable storage_root=%s", Path(storage_root))
        return None
