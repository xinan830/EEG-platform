"""Reset only rebuildable dynamic windows while preserving scientific history."""

import logging
from pathlib import Path

from app.models.cache_maintenance import CacheClearResponse
from app.services.dynamic_window_artifacts import DynamicWindowArtifactStore
from app.services.on_demand_dynamic import DynamicWindowService


logger = logging.getLogger("brain_platform.cache_maintenance")
TABLES = ("dynamic_analysis_subscriptions", "dynamic_analysis_attempts",
          "dynamic_analysis_windows", "dynamic_analysis_sessions")


class CacheMaintenanceError(RuntimeError):
    def __init__(self, code: str, message: str, status_code: int = 409):
        super().__init__(message)
        self.code, self.status_code = code, status_code


class CacheMaintenanceService:
    """Called only inside the exclusive HTTP maintenance gate in the local backend."""

    def __init__(self, windows: DynamicWindowService, reuse_artifacts: DynamicWindowArtifactStore):
        self.windows = windows
        self.reuse_artifacts = reuse_artifacts

    def clear(self) -> CacheClearResponse:
        # Recovery and admission share this owner lock; finishing cancelled jobs
        # still count as pending even after their SQLite state becomes terminal.
        with self.windows._lock:
            if self.windows._pending:
                raise CacheMaintenanceError("CACHE_BUSY", "算法任务尚未结束，请关闭回放并等待分析完成后重试。")
            files = self._cache_files()
            counts = self._clear_records()
            stores = (self.windows._artifact_store, self.reuse_artifacts)
            for store in stores:
                store.clear_cache()
            self.windows.playback_delivery.clear()
            deleted_files = freed_bytes = 0
            try:
                self.windows.artifacts_dir.mkdir(parents=True, exist_ok=True)
                for path in files:
                    size = path.stat().st_size
                    path.unlink()
                    deleted_files += 1
                    freed_bytes += size
            except OSError as exc:
                logger.exception("cache_clear_partial deleted_windows=%d deleted_files=%d freed_bytes=%d",
                                 counts[2], deleted_files, freed_bytes)
                raise CacheMaintenanceError("CACHE_FILES_REMAIN",
                    "动态窗口索引已清除，但部分缓存文件未能删除。请关闭占用文件的程序后重试。", 500) from exc
            finally:
                for store in stores:
                    store.refresh_directory_usage()
            return CacheClearResponse(deleted_subscriptions=counts[0], deleted_attempts=counts[1],
                deleted_windows=counts[2], deleted_sessions=counts[3],
                deleted_files=deleted_files, freed_bytes=freed_bytes)

    def _cache_files(self) -> list[Path]:
        directory = self.windows.artifacts_dir.absolute()
        if directory.resolve() != directory or any(
            path.is_symlink() or path.is_junction() for path in (directory, *directory.parents)
        ):
            raise CacheMaintenanceError("CACHE_PATH_UNSAFE", "缓存目录包含链接，未执行清理。")
        files = list(directory.iterdir()) if directory.exists() else []
        if any(path.is_symlink() or path.is_junction() or not path.is_file() or
               path.suffix not in {".json", ".npz"} for path in files):
            raise CacheMaintenanceError("CACHE_PATH_UNSAFE", "缓存目录包含不属于动态结果的内容，未执行清理。")
        return files

    def _clear_records(self) -> list[int]:
        with self.windows.repository._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            active = connection.execute("SELECT 1 FROM analysis_runs WHERE status IN ('queued','running') LIMIT 1").fetchone()
            attempts = connection.execute("SELECT 1 FROM dynamic_analysis_attempts WHERE state IN ('queued','running') LIMIT 1").fetchone()
            if active or attempts:
                raise CacheMaintenanceError("CACHE_BUSY", "后台分析仍在排队或计算，请等待完成后重试。")
            counts = [connection.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0] for table in TABLES]
            for table in TABLES:
                connection.execute(f"DELETE FROM {table}")
        return counts
