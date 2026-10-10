# 实时采集 WPF 按钮回归报告

## 结论与范围

2026-10-10 已通过隔离宿主中的真实按钮流程：导联选择、进入预览、开始、
暂停、恢复、完成记录。四路 SciChart 序列非空，F88-REF 取 CH3，F3-REF
取 CH5，状态切换未恢复旧映射；保存文件独立校验通过。
用户另行反馈真实设备手测逻辑正常，本报告没有替代或重新执行硬件验收。

此次新增交互式测试项目和证据校验脚本。生产壳仅增加可选配置存储注入，
用于隔离测试；正式调用不传参数时行为和路径不变。不再修改采集业务规则、
SDK、滤波公式、原始列或历史记录。工具与注入整理采用 `skip_specs: true`；
映射修复契约仍属于 `fix-live-acquisition-channel-mapping`。

## 证据与结论

| 证据 | 来源与观察 | 支持的结论 |
| --- | --- | --- |
| E-01 | 原生 UI Automation 点击真实 ComboBox 和按钮；树中出现四个 REF 标签及正确状态文本 | 按钮绑定和自定义标签显示有效 |
| E-02 | 隔离目录的 `history.json`、`current.json`；五个要求阶段均存在，无断言失败，四条 SciChart 序列非空 | 预览、记录、暂停、恢复期间映射和图表来源正确 |
| E-03 | `Verify-Evidence.ps1` 独立解析所有原始批次及 manifest/summary/audit，返回 PASS | 标签快照、CH2/3/5 原始值、样本计数和暂停缺口一致 |
| E-04 | Release 桌面测试：454 通过、0 失败、0 跳过 | 包括映射生命周期回归测试在内的现有测试未回归 |

F-01：该合成场景的通道映射与按钮工作流验证通过，依据 E-01、E-02、E-03。
F-02：不据此宣称硬件、科学滤波或长期性能通过；测试边界见下文。

P-01 调用路径：真实导联选择控件（E-01）→ 生产准备按钮及采集 ViewModel
→ 合成流与生产采集运行时 → 实际 SciChart 绑定（E-02）→ 原始记录和审计
→ 独立文件校验（E-03）。测试用已知输入识别来源，不凭波形名称判断正确性。

## 本次完整隔离复测

证据目录：
`C:/Users/23998/AppData/Local/Temp/brain-platform-ui-tests/7b17491967cc46468f67c6274585e6c1`。

记录目录名：`20261010-020438_8a4c01578a3d43a88049d9f0f6d09c7c`。

| 项目 | 结果 |
| --- | --- |
| 采样率、每批样本、原始路数 | 1000 Hz、50、30 |
| 保存批次、有效样本 | 668、33400 |
| 有效时长 | 33.4 s |
| Started、Paused 计数 | 20100、38349 |
| 暂停缺口 | 38350–67099，共 28750 样本 |
| Resumed、Stopped 计数 | 67100、82249 |
| 保存标签 | CH3=F88、CH5=F3，单位 V |
| 校验结果 | PASS，无宿主异常文件 |

较早一次测试也完成五阶段并返回 PASS：目录
`3f79b53a231a4df2afdd8dd1964e467b`，827 批、41350 样本、41.35 s，
暂停缺口 20350 样本。但旧宿主尚未注入独立预设存储，可能读取用户预设
并执行旧格式迁移，因此不把该次作为完整文件系统隔离证据。
本次已用独立存储刷新并完整重跑。此前被人工打断的启动不计入通过次数。

## 复现与质量门

按[测试项目说明](../../desktop-client/BrainPlatform.Desktop.UiTests/README.md)
构建启动并点击五阶段。文件验证命令（仓库根目录）：

```powershell
& ./desktop-client/BrainPlatform.Desktop.UiTests/Verify-Evidence.ps1 -EvidenceDirectory 'C:/Users/23998/AppData/Local/Temp/brain-platform-ui-tests/7b17491967cc46468f67c6274585e6c1'
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --configuration Release --no-restore -p:NoWarn=NU1701
dotnet build desktop-client/BrainPlatform.Desktop.UiTests/BrainPlatform.Desktop.UiTests.csproj --configuration Release --no-restore -p:NoWarn=NU1701
openspec validate fix-live-acquisition-channel-mapping --strict
git diff --check
```

证据目录为本机临时文件，第三方需重新运行宿主生成自己的证据；不得将临时
目录缺失解释成历史通过仍可复核。新增手写测试文件均未超过 400 行。
最终 Release 构建成功（0 错误）、OpenSpec 严格校验通过、`git diff --check`
通过；已跟踪文件只有既有 LF/CRLF 转换提示，没有空白错误。
提交前另从暂存区导出独立副本，排除其他未提交功能：Release UI 宿主构建
成功、413 项测试全部通过、映射复现与 OpenSpec 严格校验通过。454 项是
完整本地工作区的结果，413 项是本次独立提交范围的结果，两者不可混淆。
回滚测试工具时可移除独立测试项目与可选存储注入，原映射修复应单独评估，
不要回滚用户已有的无关工作。

## 限制与安全边界

HTTP 替身仅返回恒等数据，不验证真实 Python 滤波。没有加载硬件 SDK、
操作真实设备或真实记录；最终隔离复测不读写用户配置。
宿主加载正式资源及视图，但不运行生产启动路径或完整主窗口导航。
原生截图用于交互确认，窗口大小与屏幕限制未作为布局验收。
阶段证据是定时采样，不是每个渲染帧的性能追踪，不能证明长期无卡顿。
SciChart 许可警告与水印原样保留。

测试窗口已正常退出；合成证据保留供本机复查，不随源码提交或推送。
