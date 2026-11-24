# 回溯居中游标验收

已实现居中跟随和深灰虚线游标。自动化验证不替代真实长记录、GPU和鼠标拖拽的体验验收；独立构建不会更新正在运行的 WPF。

## 如何验收

重新编译启动客户端，在一条长于一屏的记录中暂停播放：

1. 拖动底部时间滑块，从起点慢慢向前：游标由左侧移到中间，此后保持居中，波形窗口继续向前移动。
2. 拖到记录结尾：窗口停在最后一屏，游标由中间移到右端，时间达到完整记录末尾。反向拖拽应得到对称行为。
3. 拖回起点：时间准确为 `0s`，不再跳到四分之一屏对应的秒数。
4. 选择短于一屏的记录：整段波形留在视窗内，游标仍可从起点到末尾。
5. 在中段播放、暂停、恢复，再点击时间轨道跳转。检查位置、滑块和波形匹配，轨道点击仍在完整目标帧准备后才提交。
6. 改变窗口宽度，切换记录时间/采集时间，检查游标不侵入横轴标签；两种时间模式下坐标相同。

拖拽仍受已有每秒两屏的速率限制，并保留合并加载机制。未缓存的远端窗口可能需要准备滤波历史，本次没有承诺磁盘或滤波服务零延迟，也没有为好看而改动样本时间或插值数据。

## 证据、结论与修复路径

- 证据：原 `GetFollowViewportStart` 使用 `position - visible * 0.25`；导航器又从视窗起点加回该偏移，左右两端分别只能选择四分之一屏和末尾前四分之三屏的时间。
- 结论：底部控件混用了视窗起点与播放位置，无法实现完整起止时间和居中跟随。
- 修复：`RecordingReviewViewModel` 改成二分之一屏跟随；`ReviewTimelineNavigator` 和 XAML 直接绑定 `PositionSeconds`，固定宽度滑块覆盖完整记录时长，拖拽限速的起点及鼠标同步使用同一位置映射。
- 外观：`ReviewPlaybackCursor` 独立负责 1 DIP 深灰虚线及顶部倒三角。主图表只传入真实绘图区和位置比例；相同几何不重复触发布局刷新，无独立动画或新的播放计时器。

没有修改后端、原始 EEG、manifest、导联、滤波参数、科学算法或缓存身份。
主图表为 507 行（既有登记上限 515），主视图模型 816 行（既有上限 831）；新增游标控件 67 行，导航器 285 行。未扩大到无关重构。

## 自动化结果

- 新交互测试先在旧实现复现 5 项失败；实现后全部通过。
- 全部桌面测试：498 通过，0 失败，0 跳过。
- Release 构建：0 错误、0 警告；仅抑制已有依赖兼容警告 `NU1701`。
- OpenSpec `center-review-playhead --strict`、文件规模检查和差异空白检查通过。
- 覆盖前后拖拽、两端、中段、短记录、播放及轨道跳转；保留既有原子跳转、缓存及合并读取测试。
- 640/1280 DIP 下，真实回溯图表的游标位置与绘图区一致；切换时间标签保持一致。
- 320/1000 DIP 下，独立游标离屏渲染并检查像素：线条非空、有虚线间隔、有顶部标记，所有像素均位于绘图区。已查看生成截图。

## 复现命令

在仓库根目录执行：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --artifacts-path _verify/time-display-build --verbosity quiet -p:NoWarn=NU1701
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj --configuration Release --artifacts-path _verify/time-display-build --verbosity quiet -p:NoWarn=NU1701
openspec validate center-review-playhead --strict
backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py
git -c core.safecrlf=false diff --check
```

需要重新生成游标截图时，测试前设置 `BRAIN_REVIEW_PREVIEW_DIR` 为本地验证输出目录。截图不含真实 EEG。
