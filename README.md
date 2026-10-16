# 脑电分析平台

面向 BDF/EDF 文件的离线脑电导入、分析与可视化平台。第一阶段不连接实时硬件设备。

## 启动

Windows 下可直接双击根目录的 `start_brain_platform.bat`，该入口只启动后端终端窗口，
不会启动 WPF。它优先使用 `dist\BrainPlatform-Release\backend\brain_backend.exe`，
没有便携版时回退到 `backend\.venv` 的 Python 环境，并检查 `http://127.0.0.1:8000/api/health`。
如果 8000 端口已经被本项目后端占用，脚本会复用并提示；如果属于其他项目，则不会强制关闭。

需要同时启动后端和 WPF 桌面客户端时，使用 `start_brain_desktop.bat`。该脚本会先构建当前 WPF
源码，再用 `dotnet` 加载最新 DLL；它不会启动便携发布包或旧的桌面 EXE。脚本会分别打开后端
终端窗口和 WPF 前端窗口；关闭 WPF 后只会关闭它本次启动的后端。

启动后在“算法列表 → 动态 → 分析方式”下拉框选择“全量分析”或“按需分析”，无需输入环境变量命令。
已验收的九个算法均提供这两个选项，初始选择全量分析；选择按需后，结果随游标目标窗口加载。

后端 API 文档地址为 `http://127.0.0.1:8000/docs`。如果需要单独启动后端，可在 `backend`
目录执行 `uv run uvicorn app.main:app --reload --host 127.0.0.1 --port 8000`。
