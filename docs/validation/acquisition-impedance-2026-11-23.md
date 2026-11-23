# 阻抗检测实现验证（2026-11-23）

变更：`add-acquisition-impedance-check`。环境：Windows x64、.NET 10、WPF；本记录仅是软件工程验证。

## 本次范围

- 采集准备页及采集工作区提供阻抗入口；单实例非模态工具窗，支持拖动、头皮电极图/列表选择、kΩ、降序排序和本次会话操作阈值。
- 预览先结束；录制/暂停需要确认，成功 finalize 后才打开阻抗。结束检测只空闲，选择预览才创建新 EEG 会话，不自动录制。
- Reference 物理输入请求；SDK 实际输出顺序和 role/index 决定测量身份，REF/GND 不复用普通输入标签，不造坐标或测量。
- 每次读后等待500ms，3秒无成功通信在 UI 标灰；相同读数仍刷新通信时间。
- 阻抗独立于 EEG 原始记录、滤波、算法、科学时间及回溯；未改后端协议、公式或在线导联功能。

## SDK 依据

本地厂商 SDK：eemagine/sdk/amplifier.h:59-85,169-170（Reference 请求）、wrapper.h（C ABI）、wrapper.cc（返回通道表）。SDK User Manual rev8.1 §5.3/§5.3.1 第14页规定：阻抗为最新状态，一个样本，单位 Ω。

## 自动验证

桌面命令：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --filter 'FullyQualifiedName~Acquisition|FullyQualifiedName~Devices' -p:OutDir=C:/Users/xda/AppData/Local/Temp/EEG-impedance-validation/ -v:q
```

结果：当前工作区287项通过。另将精确暂存内容导出到临时目录独立还原依赖和测试：279项通过；差异8项来自未纳入本次提交的其他任务测试。独立快照证明本功能提交不依赖其他工作区未提交代码。包含 WPF/BAML 编译及以下聚焦覆盖：

- 契约及 native delegate 替身：请求角色、输出乱序、REF/GND 同index不同角色、Ω不变、单状态形状、成功读取节流、缺失导出与普通SDK错误区分。
- 原生持有权：并发独占、读取/关闭互斥、打开后取消、部分打开清理失败、重复释放、stream→amplifier→lease顺序、关闭重试前禁止新流。
- 假设备集成：检测阻止发现/选择/重配/EEG、排队请求重新校验、错会话及取消后回调丢弃、等待读取退出再关闭、关闭失败保持占用、新预览新身份、不自动录制。
- 录制：检测前完成并保存；模拟文件锁导致 finalize 失败时不开阻抗，保留记录目录和manifest，不伪报成功。
- 数据隔离：阻抗期间无EEG缓存、无记录目录、没有HTTP请求；既有采集/原始写盘/队列/物理配置测试一起运行。
- 显示：Ω→kΩ、阈值边界/非法阈值/过期态、物理键、无坐标及REF无坐标、窗口资源加载、地图点击选择；离屏渲染检查，无电脑控制。

后端命令：

```powershell
backend/.venv/Scripts/python.exe -m pytest backend/tests/test_live_recording_access.py backend/tests/test_live_analysis_coordinator.py -q
```

结果：18项通过，覆盖实时会话关闭/重启、旧任务结果失效及采样窗口隔离。
测试工程原有 xUnit2013、CA2022 两条 analyzer 警告仍存在；本次未扩展其范围。

## 规模与边界审查

本功能新增读取所有者、物理电极呈现、工具窗和运行时模式 partial，未向已有大型协调器追加轮询或图形逻辑。触及的超过400行文件登记于 docs/code-size-policy.json，包括主窗口、配置运行时、设备会话、ANT适配器、准备页和既有大型采集VM的可执行拆分计划。
其他任务的事件编辑/实时画布文件超出现有登记值，不在本次改动/提交范围，保留原工作区内容。

`openspec validate add-acquisition-impedance-check --strict`、`git diff --check`及暂存区空白检查通过；现有后端文件规模脚本通过。由于该脚本只扫描 backend/app 与旧 frontend/src，另以同一 assess_file 对精确暂存的全部C#/XAML逐一检查，全部通过，未将原工作区其他任务的规模问题伪报为本次通过。

## 未执行与限制

- 未连接或操作真实 ANT 硬件；实际通道表、开路哨兵、REF/GND支持、刷新速度及USB断开恢复仍需真机验收。
- 未用电脑控制工具，未宣称用户交互验收通过。确认取消、重复点击聚焦、多轮切换、主窗口退出及密集电极可读性仍需人工验收。
- SDK调用若一直不返回，不强行卸载DLL或释放租约；界面保持停止/故障，阻止新流。
- 既有原始写盘队列保存失败后保留粘性故障，不自动恢复；需先处理文件/磁盘问题并恢复记录，不能借切换阻抗绕过。
- 此变更保持未归档；tasks 6.1/6.2待真实证据后勾选。厂商SDK、私有测量和构建产物不提交。
