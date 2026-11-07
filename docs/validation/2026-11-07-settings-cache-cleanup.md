# 设置页缓存清理验证记录

日期：2026-11-07。OpenSpec：`add-settings-cache-cleanup`。环境：Windows / PowerShell、项目 Python 虚拟环境、.NET 10 WPF；基于当前未提交工作区验证。

## 行为与范围

设置页增加“缓存管理 → 清除缓存”。异步执行，期间禁用重复提交；成功显示动态窗口数、缓存文件数和释放的文件字节数，失败显示原因。

- 连接中的后端：事务清除 `dynamic_analysis_subscriptions`、`dynamic_analysis_attempts`、`dynamic_analysis_windows`、`dynamic_analysis_sessions`，再删除固定动态窗口目录中的 JSON/NPZ，清空两个产物读取缓存及 TNK 交付缓冲，重计目录占用。
- 当前桌面用户：后端成功后清理 `%LOCALAPPDATA%/BrainPlatform/review-cache` 和 `review-display-cache`。录音访问租约及关闭中的会话会阻止清理；清理期间新租约等待。文件工作在线程池执行。
- 保留原始录音、历史 Runs/产物、已有审计记录、项目/用户配置、导联快照及数据库表/索引定义。仅新增本次操作的审计事件。时间、单位、通道顺序、算法公式及既有 EEG API/WebSocket 协议不变。
- 存储/分析 HTTP 请求与清理互斥，后台恢复共享动态窗口所有者锁；尚未结束的执行器任务及排队/运行中的分析会拒绝清理。健康检查、实时滤波及实时样本访问独立，清理不删除这些实时会话。
- 先移除数据库记录再删除文件；文件锁定时明确报告部分失败，可重试删除孤立文件。拒绝目录链接、重解析点和后端目录中的非窗口产物，不接受外部路径或 SQL。

## 自动化结果

后端 **38 通过**，2 条警告，37.48 s：

```powershell
& backend/.venv/Scripts/python.exe -m pytest `
  backend/tests/test_cache_maintenance.py `
  backend/tests/test_on_demand_failure_injection.py `
  backend/tests/test_offline_window_reuse.py `
  backend/tests/test_on_demand_process_recovery.py `
  backend/tests/test_tnk_playback_delivery_api.py `
  -q --disable-warnings --maxfail=2
```

覆盖合成 WPF 录音的真实 PSD 动态窗口、缺失产物、源文件哈希及受保护表/历史产物保持、数据库完整性和 schema 保持、重复清理及同值重算、后台忙碌、完整 HTTP 响应生命周期/异常释放、并发请求拒绝、部分文件删除失败后重试、未知文件/链接拒绝及实时批次在互斥期间可用、实时状态保持。新增实时状态断言曾将最后样本计数误写为半开区间终点，已按既有高水位契约修正并通过最终回归。

桌面 **29 通过**，0 失败/跳过，655 ms（不含构建）：

```powershell
$env:BRAIN_SETTINGS_PREVIEW_PATH='D:/AI/Work/brain-platform/_temp/settings-cache-tests/settings-cache-preview.png'
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj `
  --filter 'FullyQualifiedName~CacheMaintenanceTests|FullyQualifiedName~SettingsCacheViewTests|FullyQualifiedName~LocalRawRecordingReaderTests|FullyQualifiedName~ReviewFilteredSourceChunkCacheTests|FullyQualifiedName~ReviewDisplayLevelChunkCacheTests' `
  --no-restore --nologo -v quiet `
  -p:OutDir=D:/AI/Work/brain-platform/_temp/settings-cache-tests/
```

覆盖本地删除边界与实际文件字节数、锁定文件、目录链接及删除前完整预检、后端失败保留本地文件、HTTP 请求/响应与缺失字段拒绝、ViewModel 防重复/错误恢复、录音访问与清理互斥及既有缓存回归。构建包含 WPF/BAML；STA 测试加载实际 SettingsView，验证命令、忙碌禁用和完成文本，并渲染 1100×760 页面供视觉检查。渲染使用测试状态，未对真实用户缓存执行删除。存在既有 NU1701 包兼容、xUnit2013 和 CA2022 警告。

其他检查：严格 OpenSpec 验证、变更涉及文件的规模评估及 `git diff --check` 通过。全仓规模检查仍被本次未修改的 `backend/app/algorithms/psd/runner.py` 阻断：464 行超过登记上限 420 行；本功能涉及的文件均未超过 400 行目标。

## 验收边界

未清除实际用户缓存，未完成运行中桌面应用连接真实后端的人工点击验收、设备采集或大目录性能测试。自动化只操作临时夹具。清理后缓存会重新生成，首次打开/分析可能更慢；本功能不宣称修复计算滞后。释放量仅统计实际删除的缓存文件，不包含 SQLite 空闲页；不执行 VACUUM。跨进程共享同一存储目录仍沿用既有不支持的边界。

人工验收：更新并重启后端、桌面，关闭数据回放及使用录音的算法页面，待后台分析结束后进入设置清理；确认成功计数，再打开原录音验证窗口重新生成。回放保持打开时应明确拒绝，不能报告成功。
