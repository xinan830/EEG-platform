from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[3]
STORAGE_DIR = PROJECT_ROOT / "storage"
RECORDINGS_DIR = STORAGE_DIR / "recordings"
# 旧的 brain-platform.sqlite3 被重建前服务锁住，新的应用绝不复用它。
DATABASE_PATH = STORAGE_DIR / "brain-platform-v2.sqlite3"
ALLOWED_EXTENSIONS = {".bdf", ".edf"}


def ensure_storage_directories() -> None:
    RECORDINGS_DIR.mkdir(parents=True, exist_ok=True)
    STORAGE_DIR.mkdir(parents=True, exist_ok=True)
