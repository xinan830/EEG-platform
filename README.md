# 脑电科研平台

本地单用户 EEG 科研工作站，包含 WPF 桌面端和 Python/FastAPI 后端。桌面端负责设备采集、原始记录、阅图和交互；后端负责数据校验、导联投影、分析预处理及科学计算。支持 BDF/EDF 导入和已完成的 WPF 采集记录分析。

当前官方目录注册 11 个公共算法 ID：10 个独立算法（PSD、STFT、RBP、Band Ratio、Peak Frequency、IAPF、Theta/Beta、FAA、经典 Brainbeat、个体化 Brainbeat）和 1 个 TNK 指标组。两个 Brainbeat ID 共用 `brainbeat/` 包，包目录数不等于算法 ID 数；TNK 内部子指标不计入公共目录数量。运行管理统一，公式和专属质量门由各算法模块独立维护。动态分析提供全量、按需及组合方式；具体复用方向及验证范围见[管理基线](docs/algorithms/official-algorithm-management.md)。

算法工作区已为这 11 个 ID 注册详情和结果处理器；Review 已注册其中 9 个，尚未注册 Band Ratio 和 Peak Frequency。后端可运行、工作区注册、Review 注册和实际验收是不同状态，逐项范围见[客户端接入状态表](docs/algorithms/review-adapter-onboarding.md)。

## 启动

在 Windows 双击 `backend\start_backend.bat` 启动源码后端。它只运行后端，不构建或启动 WPF。

后端启动后，可通过 IDE 或在仓库根目录运行以下命令单独启动桌面端；需要 .NET 10 SDK：

```powershell
dotnet run --project desktop-client\BrainPlatform.Desktop\BrainPlatform.Desktop.csproj
```

API 文档：<http://127.0.0.1:8000/docs>。

## 阅读入口

- [文档导航](docs/README.md)：当前说明和验证证据入口。
- [交接说明](HANDOFE.md)：当前基线、边界和排查顺序。
- [工程开发契约](docs/工程开发契约.md)：修改和审查要求。
- [算法接入指南](docs/algorithms/official-algorithm-development.md)：新增算法流程。
- [采集与阅图架构](docs/architecture/desktop-acquisition-review.md)：设备、存储、滤波和播放边界。

原始记录只读；导联和分析处理产生派生输入。官方算法仅维护当前科学版本及结果格式。工程验证不等同于临床验证。构建产物、运行缓存、数据库和真实 EEG 不纳入 Git。
