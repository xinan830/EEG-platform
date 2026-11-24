# 实时采集通道映射修复验收

## 结论与范围

实时显示在采集状态通知后重新加载旧设备标签，导致 F88 导联被遗漏、
迁移后的 F3/F7 导联取错物理输入。修复只区分设备事实变化与运行状态变化，
不交换原始列、不修改 SDK、滤波公式、算法、预设或历史记录。
自动验证使用合成信号；用户已反馈真实设备手测逻辑正常，但没有另行记录
完整硬件验收证据。真实 WPF 按钮回归及保存文件独立校验已完成，详见
[UI 回归报告](2026-10-10-acquisition-ui-regression-report.md)。

## 证据、问题与修复路径

- 证据：用户配置 CH3=F88、CH5=F3，显示四路；截图实时窗口只有
  Fp1-REF、Fp2-REF、F3-REF。
- 复现：修复前将 CH3=F88 或 CH3=F7 应用后，准备状态通知使 CH3 回到 F3。
  F88 曲线消失；F7 错读合成 CH2=20 µV，而非 CH3=40 µV。
- 根因：`AcquisitionWorkspaceViewModel.OnDeviceSessionPropertyChanged` 将
  Snapshot/SelectedDevice 通知一律当作设备变化，调用 LoadDevice 清空
  active configuration 并加载旧标签。按标签取数的显示投影随后读错来源。
- 路径：在 ApplySelectedDevice 复用 DeviceSessionDiscoveryComparer，
  相同设备事实不重新初始化设置。设备身份或能力变化、断开仍正常刷新。
- 证据工具：`_verify/acquisition-mapping-probe/Program.cs`。
- 回归测试：`desktop-client/BrainPlatform.Desktop.Tests/Acquisition/LiveAcquisitionMappingTests.cs`。

## 数值验收

输入为 float64/V 合成样本，CH2=20 µV、CH3=40 µV、CH5=60 µV。
设置 CH3=F88、CH5=F3 时，严格验证四路输出顺序和数值：
Fp1-REF、Fp2-REF、F88-REF（40 µV）、F3-REF（60 µV）。
迁移 F7 到 CH3 时将原 CH2 标签改为 F77，避免重复标签干扰。
数值断言采用 12 位小数精度（V），同时断言原样本值未改变。

覆盖准备、预览、开始记录、暂停、恢复、停止；等价重新发现保留配置，
设备替换、能力变化、断开和重新发现按真实设备事实更新设置。

最终桌面测试 454 项通过（含本次新增 6 项），Release 构建成功；独立复现
工具的 F88/F7 输出均从 CH3 读取 40 µV。OpenSpec 严格校验与
`git diff --check` 通过。未改动后端，未重复执行全算法数值验收。

复现命令：

```powershell
dotnet run --project _verify/acquisition-mapping-probe/AcquisitionMappingProbe.csproj --configuration Release --no-restore -p:NoWarn=NU1701
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --configuration Release --no-restore -p:NoWarn=NU1701
openspec validate fix-live-acquisition-channel-mapping --strict
```

## 人工验收

1. 完整退出旧桌面应用，不只是关闭实时采集窗口；不要中断正在记录的任务。
2. 启动重新编译的 Release 版本，选择绑定测试通道配置的导联。
3. 预览应有四路 Fp1-REF、Fp2-REF、F88-REF、F3-REF。
4. 独立确认 F88 对应 CH3、F3 对应 CH5 的接线/输入响应，不能只看曲线名字。
5. 开始记录、暂停、恢复后，标签及输入来源保持不变。
6. 完成记录后回溯同一记录，核对采集快照、物理编号及波形来源。

真实无波形仍需排除接触、硬件连接、滤波和质量问题，不能通过重命名或
交换历史原始数据掩盖。此前发现的实时显示故障不等于已证明所有历史原始
记录损坏；历史文件保持不变。
