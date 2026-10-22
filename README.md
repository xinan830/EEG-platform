# 脑电科研平台

本地单用户 EEG 科研工作站，包含 WPF 桌面端和 Python/FastAPI 后端。桌面端负责设备采集、原始记录、阅图和交互；后端负责数据校验、导联投影、分析预处理及科学计算。支持 BDF/EDF 导入和已完成的 WPF 采集记录分析。

当前九个官方算法：PSD、STFT、RBP、Band Ratio、Peak Frequency、IAPF、Theta/Beta、FAA、离线 Brainbeat。运行管理统一，公式和专属质量门由各算法模块独立维护。动态分析提供全量、按需及组合方式；具体复用方向见[管理基线](docs/algorithms/official-algorithm-management.md)。

## 启动

在 Windows 双击 `start_brain_desktop.bat`：构建当前 WPF 源码，启动匹配的后端与桌面窗口。需要 .NET 10 SDK 和已安装项目依赖的 Python 环境。脚本不使用旧桌面发布包。

仅启动后端可使用 `start_brain_platform.bat`；该脚本优先使用打包后端，开发当前源码时应使用桌面启动脚本，或在 `backend` 目录执行：

```powershell
uv run uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

API 文档：<http://127.0.0.1:8000/docs>。

## 阅读入口

- [文档导航](docs/README.md)：当前说明和验证证据入口。
- [交接说明](HANDOFE.md)：当前基线、边界和排查顺序。
- [工程开发契约](docs/工程开发契约.md)：修改和审查要求。
- [算法接入指南](docs/algorithms/official-algorithm-development.md)：新增算法流程。
- [采集与阅图架构](docs/architecture/desktop-acquisition-review.md)：设备、存储、滤波和播放边界。

原始记录只读；导联和分析处理产生派生输入。官方算法仅维护当前科学版本及结果格式。工程验证不等同于临床验证。构建产物、运行缓存、数据库和真实 EEG 不纳入 Git。
