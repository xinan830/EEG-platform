# 算法实现地图

所有路径相对仓库根目录。目录参数 `<id>` 表示具体算法包，不是通配加载或插件入口。

| 职责 | 路径 |
| --- | --- |
| 九算法身份、参数和独立计算 | `backend/app/algorithms/<id>/` |
| 唯一后端组合表 | `backend/app/algorithms/builtin_modules.py` |
| typed 输入/结果、能力及校验 | `backend/app/algorithm_runtime/` |
| 记录到算法输入的适配 | `backend/app/services/algorithm_recording_context.py` |
| 连续预处理与谱服务 | `backend/app/services/spectral_analysis.py` |
| 共享谱计算及 band power | `backend/app/scientific/primitives/spectral.py` |
| Run 入口和生命周期 | `backend/app/api/runs.py`、`backend/app/services/runs.py` |
| 按需窗口 HTTP | `backend/app/api/analysis_windows.py` |
| 通用结果序列化 | `backend/app/services/run_result_serialization.py` |
| 当前结果格式边界 | `backend/app/models/current_algorithm_result.py` |
| WPF 算法工作区 | `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/` |
| WPF UI/detail 注册 | `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/AlgorithmUiRegistry.cs` |
| WPF Run handler 注册 | `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/AlgorithmRunResultHandlerRegistry.cs` |
| 基于记录的导联候选 | `desktop-client/BrainPlatform.Desktop/Modules/Review/Montages/RecordingMontageCatalog.cs` |

新增算法遵循[作者指南](official-algorithm-development.md)，不在通用层加入按 ID 分支的公式。出现新输入来源或结果形状时，单独扩展 typed 契约。

排查依次核对：请求/Run/window → 源 checksum → 导联快照与角色顺序 → 样本范围/滤波身份 → 模块中间量与质量门 → artifact 与 WPF 投影。历史报告中的旧路径不作为当前入口。
