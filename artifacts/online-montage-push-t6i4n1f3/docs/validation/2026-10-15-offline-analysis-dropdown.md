# 离线分析方式下拉选择验证

日期：2026-10-15。范围：九个已验收算法无需启动命令即可在 WPF 选择全量/按需；不接入实时分析。

## 使用方法

关闭当前旧客户端与后端，双击根目录 `start_brain_desktop.bat` 启动当前源码。
在“算法列表 → 动态 → 分析方式”选择“全量分析”或“按需分析”。初始为全量分析。
选择变化继续使用既有的执行调度、旧结果清理和订阅释放。

## 根因与变更依据

Evidence：动态参数卡已有双向绑定下拉框，但九个结果处理器和后端启动筛选均要求环境变量等于 `1`。
源码启动脚本只强制开启 PSD；用户逐算法验收和综合验收后仍需反复输入命令。

Finding：缺口是默认可用性，不是缺少另一套计算引擎。九个已验收具体算法模块声明默认开放；
共享解析器接受维护覆盖，未批准的新模块仍默认关闭。后端继续检查实际单窗能力，
不能仅凭默认声明为没有能力的模块生成按需结果。

Path：复用 `AnalysisParameterCard`，控件名 `DynamicExecutionModeComboBox`，标签为“分析方式”。
后端启动与 WPF 处理器分别读取模块批准值；PSD Review 复用 WPF 可用性解析器。
启动脚本移除强制 PSD 变量，保留原有源码构建和两个窗口流程。

科学公式、参数、窗口、单位、滤波、质量、缓存身份和原始记录均未由本变更调整。
跨模式窗口复用仍仅 PSD/STFT 显式支持，其余模块不因默认开放而获得共享缓存能力。

## 自动化结果

| 检查 | 结果 |
| --- | --- |
| 后端可用性与封闭注册表专项 | 32 passed |
| 后端完整回归 | 703 passed，2 条依赖弃用警告 |
| WPF 下拉选择与按需详情专项 | 70 passed |
| WPF 完整回归 | 702 passed，0 skipped |
| WPF Release build | 0 errors，3 条既有 NU1701 包兼容性警告 |
| 五个相关 OpenSpec strict | 通过 |
| 文件规模与 `git diff --check` | 通过；保留已有已登记规模提示 |

专项覆盖九模块无变量默认提供两种方式、全量初始值、控件实际布局与双向绑定、
显式关闭后的完整路径、模式切换清旧投影与释放订阅、相同科学配置重新进入按需，
以及 Review 默认值与注入覆盖。WPF 测试用传输 fixture，不能代替真实 EEG 数值验证。

最初写入普通 Release 目录时，正在运行的旧客户端占用了 DLL，构建复制失败。
未终止该进程；后续测试和构建使用独立输出目录：
`C:/Users/23998/AppData/Local/Temp/brain-offline-selection-20261015/`。
控件专项最初使用未挂载控件的 `IsVisible` 断言失败，修正为可见性声明与实际布局检查后通过。
没有把上述失败算作通过，也没有修改生产逻辑来迎合测试。

## 复验命令

在项目根目录执行；输出目录可替换为其他独立临时目录。

```powershell
Push-Location backend
.\.venv\Scripts\python.exe -m pytest -q
Pop-Location
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --configuration Release --no-restore -p:UseAppHost=false -p:OutDir=C:\Users\23998\AppData\Local\Temp\brain-offline-selection-20261015\ --verbosity minimal
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj -c Release --no-restore -p:UseAppHost=false -p:OutDir=C:\Users\23998\AppData\Local\Temp\brain-offline-selection-20261015\ --verbosity minimal
openspec validate enable-accepted-offline-analysis-selection --strict
openspec validate unify-offline-analysis-execution-modes --strict
openspec validate on-demand-dynamic-analysis --strict
openspec validate share-offline-scientific-window-identity --strict
openspec validate share-stft-offline-scientific-window-identity --strict
.\backend\.venv\Scripts\python.exe backend/scripts/check_file_sizes.py
git diff --check
```

## 人工证据与限制

前轮九个算法及综合场景的用户确认记录见
[人工验收记录](2026-10-15-offline-modes-wpf-acceptance.md)。
当前免命令入口已通过真实 WPF 控件自动化，本轮未重新操作用户运行中的应用、设备或采集；
不宣称新增界面人工验收或拖动延迟、帧率保证。

维护回退可成对设置 `BRAIN_ENABLE_ON_DEMAND_<ID>` / `BRAIN_WPF_ON_DEMAND_<ID>` 为 `0` 后重启。
未设置或空值采用模块默认值，`1` 开启，其他非空值关闭。前后端覆盖需保持一致，
否则前端选择不能代替后端准入校验。无需数据迁移，不删除历史结果。

工作树基于 `main` 的 `190472c` 并含此前未提交改动，测试针对当前工作树，
该基线提交号不是本轮全部受测源码的不可变版本。
