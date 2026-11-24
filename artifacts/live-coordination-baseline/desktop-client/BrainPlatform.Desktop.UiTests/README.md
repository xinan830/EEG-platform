# 实时采集 UI 回归测试

使用真实 WPF 采集准备页、采集页、按钮绑定及 SciChart，验证自定义通道映射在
预览、开始、暂停、恢复和停止期间保持一致。

这是交互式测试宿主，不是 `dotnet test` 自动执行的无界面 UI 测试。
需要 Windows、.NET 10 SDK、已还原的项目依赖和可交互桌面。
SciChart 许可要求与正式程序相同，不处理许可提示或水印。

## 运行与验证

在仓库根目录执行：

```powershell
dotnet build desktop-client/BrainPlatform.Desktop.UiTests/BrainPlatform.Desktop.UiTests.csproj --configuration Release --no-restore -p:NoWarn=NU1701
& ./desktop-client/BrainPlatform.Desktop.UiTests/bin/Release/net10.0-windows/BrainPlatform.Desktop.UiTests.exe
```

仅操作标题为 `BrainPlatform - Synthetic acquisition UI regression` 的窗口：

1. 在导联下拉框选择 `Synthetic F88 mapping`。
2. 点击“进入实时采集”，等待四条曲线出现。
3. 点击“开始记录”，等待至少两秒。
4. 点击“暂停记录”，等待至少两秒；预览应继续更新。
5. 点击“恢复记录”，等待至少两秒。
6. 点击“完成记录”，等待完成，再关闭测试窗口。

验证证据：

```powershell
$evidenceDirectory = (Get-Content -Raw ./desktop-client/BrainPlatform.Desktop.UiTests/bin/Release/net10.0-windows/last-run.txt).Trim()
& ./desktop-client/BrainPlatform.Desktop.UiTests/Verify-Evidence.ps1 -EvidenceDirectory $evidenceDirectory
```

成功返回 `Result: PASS`；缺少任何阶段、映射错误、图表数据缺失、原始值错误、
暂停缺口不一致或宿主异常均会报错。中途退出不能算通过。
正式单元测试仍须单独运行：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --configuration Release --no-restore -p:NoWarn=NU1701
```

## 隔离与证据

每次启动生成 `%TEMP%/brain-platform-ui-tests/<guid>`，配置存储、事件定义与
合成记录都位于该目录。不会执行生产 `App.OnStartup`、启动 Python、加载
ANT SDK 或连接真实放大器。宿主注入独立的项目、通道和导联存储，使用正式
存储刷新流程，不读取用户预设。正式程序的默认存储路径不变。

合成输入为 1000 Hz、30 路，采样值单位 V；CH2、CH3、CH5 分别为
20、40、60 µV 基线叠加 4 µV 正弦波。自定义映射为 CH3=F88、CH5=F3。
四路输出应为 Fp1-REF、Fp2-REF、F88-REF、F3-REF。

`current.json` 保存最近状态；`history.json` 保存各阶段首次完成检查的状态。
宿主检查实际可见标签、非空 SciChart 数据序列和投影信号来源。
`Verify-Evidence.ps1` 独立读取二进制原始批次，校验每个样本的 CH2/3/5
合成值、计数列、清单快照、有效样本数和生命周期缺口。

测试采用恒等滤波 HTTP 替身，没有真实网络传输，因此不验证 Python 滤波
数学、硬件接线或 SDK 行为。宿主用生产按钮进入预览后自行切换页面，不
覆盖完整主窗口导航。截图用于交互观察，不代表全分辨率布局验收；短时
测试不能证明长时间采集无掉帧。证据保留在临时目录，清理前可留作复查，
不要提交任何真实 EEG 数据。

最近一次结果见[验证报告](../../docs/validation/2026-10-10-acquisition-ui-regression-report.md)。
