# 仪器式采集与回顾布局验收

日期：2026-10-10。此变更只调整 WPF 展示承载，不改 EEG 数据、时间、单位、
滤波、通道顺序、导联兼容性、采集状态机或历史记录。

## 已实现

- `EegSessionWindow` 提供统一标题栏，页面菜单移入标题栏并继续绑定页面
  DataContext；窗口关闭仍走原有确认和会话清理路径。
- `InstrumentWorkspaceStyles.xaml` 提供浅色仪器式按钮、参数字段、工具按钮
  和紧凑工作区壳。采集与回顾通过显式 opt-in 使用，其他页面不受影响。
- 采集保留高通、低通、陷波、灵敏度、纸速/时间基、导联、设置、事件及
  开始/暂停/恢复/完成命令；回顾保留导联、事件、分析菜单、播放和时间轴。
- 页面菜单在转移到标题栏前从原视觉父级移除，避免 WPF 重复父级运行时错误。

## 验证命令

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --filter "FullyQualifiedName~EegWorkspaceShellTests|FullyQualifiedName~RecordingReviewSmokeTests|FullyQualifiedName~RecordingReviewTimeDisplayTests|FullyQualifiedName~ReviewTimelineNavigatorTests|FullyQualifiedName~AnalysisPanelCompositionTests" --artifacts-path _verify/instrument-layout-tests -p:NoWarn=NU1701
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj -c Release --artifacts-path _verify/instrument-layout -p:NoWarn=NU1701
openspec validate instrument-eeg-workspace-layout --strict --no-interactive
backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py
git diff --check
```

相关回归测试 9 项通过；Release 构建 0 警告、0 错误。原型和 WPF 生产界面
均未接入新的科学计算路径。

## 未覆盖

尚未在真实 Windows GPU 窗口中进行视觉验收，也未运行真实放大器采集。标题栏
拖拽、最大化/还原、菜单命令和窗口关闭需要在交互式宿主中人工确认；本报告不
把 Release 构建当作完整 WPF 套件通过证明。
