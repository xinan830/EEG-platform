"""
PyInstaller 打包入口 —— 不要直接运行此文件用于开发。
开发环境请继续使用：uvicorn app.main:app --reload
"""
import os
import sys
from pathlib import Path


def _resolve_storage_root() -> Path:
    """
    打包后 sys.frozen=True，可执行文件位于 backend/ 子目录，
    storage/ 与 backend/ 同级，即 executable/../storage/
    """
    configured = os.environ.get("BRAIN_STORAGE_ROOT")
    if configured:
        return Path(configured)
    if getattr(sys, "frozen", False):
        local_app_data = os.environ.get("LOCALAPPDATA")
        if local_app_data:
            return Path(local_app_data) / "BrainPlatform" / "storage"
        exe_dir = Path(sys.executable).parent
        return exe_dir.parent / "storage"
    else:
        # 非打包模式：fallback 到原有逻辑（PROJECT_ROOT/storage）
        return Path(__file__).resolve().parents[2] / "storage"


# 在 import app.main 之前先注入环境变量，让 config.py 读取
storage_root = _resolve_storage_root()
os.environ.setdefault("BRAIN_STORAGE_ROOT", str(storage_root))

if __name__ == "__main__":
    import uvicorn

    port = int(os.environ.get("BRAIN_BACKEND_PORT", "8000"))
    uvicorn.run(
        "app.main:app",
        host="127.0.0.1",
        port=port,
        log_level="warning",
        # 打包后不能用 reload
        reload=False,
    )
