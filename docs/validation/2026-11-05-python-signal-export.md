# 2026-11-05 整段原始录音 Python EDF/BDF 导出验收

变更：`openspec/changes/move-review-export-to-python`。验收覆盖工程正确性，不构成科研数据有效性或临床效度证明。

## 结果与输入

已将 EDF+/BDF+ 写入统一迁到 Python pyEDFlib 0.1.42，移除桌面 SharpEDFlib 依赖和写入器。WPF 仅注册原始来源、请求、下载和发布文件。当前观察窗口、显示滤波、查看导联和算法选择均不裁剪或重算导出样本。

输入：`D:/迅雷下载/纪昱彤/recordings/20261001-165105_8e0c630a60d14913ae9f5c60d7b23edf`。

来源 SHA-256：`1c3a9ef23771d6b768ec31bd9f072d9504d32d4149fe799dbd8e976f27465983`。包含 manifest、summary、audit 和样本文件；导出前后复核一致。事件文件单独检查导出期间未变化。

| 核对项 | EDF+ | BDF+ |
| --- | ---: | ---: |
| 原始 Reference/Bipolar 通道数 | 28 | 28 |
| 每通道样本数 | 149982 | 149982 |
| 采样率 Hz | 1000 | 1000 |
| 时长 s | 149.982 | 149.982 |
| 单记录样本数 / 时长 s | 24997 / 24.997 | 24997 / 24.997 |
| 文件字节数（不含元数据） | 8407356 | 12606852 |
| 最大回读绝对误差 µV | 17.1514133 | 0.0669947 |
| 最大误差 / 本通道量化步长 | ≤ 1.000001 | ≤ 1.000001 |
| 来源已记录事件数 | 0 | 0 |

MNE 以非 preload 模式逐块回读，并逐通道对照原始 V 样本；样本、通道顺序、精确采样率均通过。来源中 7 路无有效原标签，生成无冲突别名，并在 sidecar 保留原标签、stream/native 索引和通道类型。设备 REF 及采集导联公式快照保存在 sidecar，未做软件重参考。

源文件存在有限极端值，尤其 CH0025/CH0027 拉宽量程。它们保留为原始数据，并记录实际范围、量化步长和极端值数量。BDF 精度优于 EDF，但两者均不修复疑似采集占位值；不得把通过格式验收解释为这些值具有生理意义。

## 验证路径

1. 后端 50 项定向测试通过：导出、API、WPF 记录/读取、TNK 模块与模型加载、文件规模策略单元测试。包括缺口、NaN/Inf、不支持单位、来源变化、长偏置量程、短记录时间刻度、读取分块、取消、断连、并发拒绝、磁盘失败和临时文件清理。
2. 桌面 9 项导出定向测试通过，并完成 WPF/BAML 构建。包含取消、关闭页面取消、重复导出拒绝、失败回滚、HTTP 错误码，以及真实录音经 C# HTTP 客户端 → 本地 Uvicorn/FastAPI → Python → ZIP → 目标两文件的实际导出。
3. 真实 HTTP 下载的两份信号文件，与 MNE 独立逐样本核对过的服务输出逐字节相同。
4. `openspec validate move-review-export-to-python --strict` 与 `git diff --check` 通过。

可复现命令（仓库根目录 PowerShell）：

```powershell
uv sync --project backend --frozen --extra dev
.\backend\.venv\Scripts\python.exe -m pytest backend/tests/test_recording_signal_export.py backend/tests/test_recording_signal_export_api.py backend/tests/test_wpf_recording.py backend/tests/test_recording_access.py backend/tests/test_tnk_modules.py backend/tests/test_tnk_legacy_biofeedback.py backend/tests/test_file_size_policy.py -q
.\backend\.venv\Scripts\python.exe backend/tools/validate_signal_export.py 'D:/迅雷下载/纪昱彤/recordings/20261001-165105_8e0c630a60d14913ae9f5c60d7b23edf' '_temp/review-python-export-acceptance'
```

真实桌面 HTTP 验证需要先启动可用的测试后端（本次 18001 端口，隔离 storage），再运行：

```powershell
$env:BRAIN_PLATFORM_EXPORT_RECORDING='D:/迅雷下载/纪昱彤/recordings/20261001-165105_8e0c630a60d14913ae9f5c60d7b23edf'
$env:BRAIN_PLATFORM_EXPORT_OUTPUT='D:/AI/Work/brain-platform/_temp/review-python-export-http-acceptance'
$env:BRAIN_PLATFORM_EXPORT_BACKEND='http://127.0.0.1:18001/'
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -o _temp/python-export-desktop --filter 'FullyQualifiedName~RecordingReviewSignalExporterTests|FullyQualifiedName~Cancel_or_close_review_cancels_export'
```

本地产物：`_temp/review-python-export-acceptance/validation.json`、`recording.edf`、`recording.bdf`；完整桌面链路产物位于 `_temp/review-python-export-http-acceptance/`，包含 `desktop-http.edf`、`desktop-http.bdf`、sidecar 和 `validation.json`。未提交录音、临时数据、构建输出或旧环境备份。

## 运行环境与导出契约

- 开发后端统一使用 Python 3.12.13；`.python-version` 固定 3.12，`pyproject.toml` 支持 3.10–3.12。pyEDFlib 0.1.42 缺少当前 Windows Python 3.13 轮子，本机未安装所需 MSVC；选择有现成轮子的统一 3.12 环境，不引入单独的导出进程或永久第二套环境。
- 原 3.13 venv 保留为本地回滚备份 `_temp/backend-python313-before-export`；正式路径仍为 `backend/.venv`。启动方式仍为 `backend/start_backend.bat`。其他机器须先退出使用旧 venv 的后端，将旧 venv 移至备份，再执行 `uv sync --project backend --frozen --extra dev --python 3.12`。不迁移数据库或改写录音。
- POST `/api/recordings/{id}/signal-export` 只接受 `{"format":"edf"}` 或 `{"format":"bdf"}`；只支持 `raw` WPF 登记；返回含两个固定成员的 ZIP。格式文件可独立读取，`.labels.json` 保存 EDF/BDF 无法完整表达的来源、导联和精确事件坐标。
- 单进程仅允许一个导出任务，繁忙返回 409 / `RECORDING_EXPORT_BUSY`。两遍样本读取每次不超过 65536 个样本；记录写入同时受缓冲字节上限约束。共享读取器裁剪实际复制范围，避免对大源 batch 做整批复制。哈希与索引阶段也支持取消。元数据索引内存仍随 batch 数增长，不能宣称完全恒定内存。
- 整数 µV 物理范围端点向外取整，规避 EDFlib 对二进制小数的文本截断。交付前回读头部，确认实际端点与请求一致且包含原始范围；量化步长以编码头部为准。
- pyEDFlib 记录时长绑定使用浮点乘 100000 后截断，0.009 秒等值可能少一个刻度。局部向上推进一个浮点表示值后交给库转换，编码目标仍为精确的样本数/采样率；交付前额外核对实际样本数、头部有理数采样率和通道数，发现不一致就拒绝交付。32.77 秒等正确十进制时长在读取端可能产生一位浮点尾差，故不能以二进制浮点严格相等判断格式正确性。
- pyEDFlib 0.1.42 datetime setter 的亚秒倍数问题已局部规避，写入使用 100 ns ticks。MNE 的 measurement date 当前丢弃亚秒，故另核对编码 ticks，并在 sidecar 保留完整 UTC；事件注释最大舍入误差约 0.05 ms，sidecar 保留原始整数样本坐标。
- 不补点、不裁尾、不填零、不自动清洗有限极端值。连续格式无法精确表示的采样率/总样本数组合明确拒绝；含源缺口、非有限值、超范围头字段或无法完整编码的事件也明确失败。
- 桌面暂存下载和两份文件，发布失败恢复原目标；回滚本身失败时保留备份并报告恢复目录。两文件不能获得跨进程崩溃时的单次原子替换保证。

## 验证边界与现有失败

未做 WPF 保存对话框的实际点击/视觉验收、超长录音压力测试、完整发布包构建或第三方商业阅读器验证。当前正在运行的旧桌面进程不会自动载入新 DLL；使用 `_temp/python-export-desktop/BrainPlatform.Desktop.exe` 进行人工验收。

扩大桌面筛选范围的 127 项测试中，125 项通过，2 项失败：`Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel`、`Dragging_exclusively_owns_position_then_restores_previous_playback_state(True)`。分别涉及已有分析面板绑定和拖动播放状态路径，本次导出定向测试不依赖它们。未宣称桌面回归全绿。

仓库全量规模检查仍失败于三个现有文件，且均在 HEAD 中已超限：`algorithm_recording_context.py` 410 行无登记、`on_demand_dynamic.py` 559 行超过 530 上限、`psd/runner.py` 450 行超过 420 上限。本次新增文件均低于 400 行；修改的超限 Review ViewModel/视图仍处于已登记范围内，不扩大现有预算。未为绕过检查而提高这三个无关文件的上限。
