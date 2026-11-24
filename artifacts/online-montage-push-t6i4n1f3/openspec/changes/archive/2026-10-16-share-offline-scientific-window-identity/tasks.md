# Tasks

- [x] 统一科学身份快照、执行方式与增量持久化；保留历史可读性。
- [x] PSD 按窗口复用已发布按需结果，缺失窗口调用同一个模块。
- [x] 测试两种模式身份一致、参数/导联/版本/窗口隔离、部分命中、损坏回退、质量与取消。
- [x] 独立执行两条路径，比较 PSD/频率轴/单位/质量/证据，不以缓存命中代替数值一致性验证。
- [x] 后端、WPF 回归、OpenSpec strict、文件规模检查与 diff 检查。

## 验证记录（2026-10-15）

- `cd backend; python -m pytest -q`：670 passed。
- `dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --configuration Release --no-restore -p:UseAppHost=false --verbosity minimal`：675 passed。
- `openspec validate share-offline-scientific-window-identity --strict`、`python backend/scripts/check_file_sizes.py`、`python -m compileall -q backend/app`、`git diff --check`：通过。
- 跨模式窗口复用仅对 PSD 开放；全量结果不反向填充按需存储。WPF 自动化回归不等同于人工拖拽、播放和模式切换验收。

## 后续验收同步（2026-10-15）

- [x] 用户报告 PSD 全量/按需正常路径验收通过；其他算法亦已逐项反馈，见 `docs/validation/2026-10-15-offline-modes-wpf-acceptance.md`。该反馈不新增缓存命中或严格数值误差证据。
- 后续 STFT 也已显式声明跨模式复用，见 `share-stft-offline-scientific-window-identity`；上方“仅 PSD”描述的是本 change 交付时范围，其余算法不隐式获得复用能力。
