import os
from pathlib import Path


def _resolve_storage_root() -> Path:
    """
    支持两种运行模式：
    1. 打包发布：server_entry.py 在启动前注入 BRAIN_STORAGE_ROOT 环境变量
    2. 开发模式：回退到 PROJECT_ROOT/storage（保持原有行为）
    """
    env = os.environ.get("BRAIN_STORAGE_ROOT")
    if env:
        return Path(env)
    return Path(__file__).resolve().parents[3] / "storage"


STORAGE_DIR = _resolve_storage_root()
RECORDINGS_DIR = STORAGE_DIR / "recordings"
ARTIFACTS_DIR = STORAGE_DIR / "artifacts"
# 旧的 brain-platform.sqlite3 被重建前服务锁住，新的应用绝不复用它。
DATABASE_PATH = STORAGE_DIR / "brain-platform-v2.sqlite3"
ALLOWED_EXTENSIONS = {".bdf", ".edf"}


def on_demand_algorithm_enabled(algorithm_id: str, *, default_enabled: bool = False) -> bool:
    """Use module-approved availability unless a maintenance override is configured."""
    value = os.environ.get(f"BRAIN_ENABLE_ON_DEMAND_{algorithm_id.upper()}")
    return default_enabled if not value else value == "1"


def ensure_storage_directories() -> None:
    RECORDINGS_DIR.mkdir(parents=True, exist_ok=True)
    ARTIFACTS_DIR.mkdir(parents=True, exist_ok=True)
    STORAGE_DIR.mkdir(parents=True, exist_ok=True)
