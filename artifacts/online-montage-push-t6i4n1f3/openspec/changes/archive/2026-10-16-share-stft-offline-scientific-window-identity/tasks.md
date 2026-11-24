# Tasks

- [x] 统一 STFT 按需与全量动态窗口的相对内层时间轴和完整窗口形状。
- [x] 增加 STFT 模块的安全复用能力声明，并接入公共离线窗口复用适配器。
- [x] 测试独立执行一致性、部分窗口不复用、损坏 artifact/坐标/形状回退和取消。
- [x] 使用真实 Recording 验证先按需后全量的复用数量、数值和原始文件不变。
- [x] 运行后端、WPF 回归、OpenSpec strict、代码规模和 diff 检查。

## 验证记录（2026-10-15）

- 真实记录：`D:\迅雷下载\纪昱彤\recordings\20261001-165105_8e0c630a60d14913ae9f5c60d7b23edf`。
- `python scripts/validate_on_demand_stft.py --recording ... --channel F3-REF`：通过；60–65 秒完整窗口复用计数 `1`，117 个频点，矩阵 `2×117`，线性功率与 dB 最大绝对误差均为 `0`，频率轴/单位/相对时间轴一致，原始文件哈希不变。
- 先独立执行全量 STFT，再创建按需窗口并再次执行全量，用独立矩阵完成数值比对；未把缓存命中当作独立证据。
- STFT 后端专项：8 passed；WPF STFT 专项：24 passed。
- 全量回归：后端 `673 passed`，WPF `675 passed`；OpenSpec strict 通过。
- WPF 按需适配器将相对内层时间轴投影回记录绝对时间，旧的绝对时间 provenance 会被拒绝；实现身份更新为 `stft-runtime-v4-relative-window-times`，旧坐标 artifact 不会复用。

## 人工验收同步（2026-10-15）

- [x] 用户在 STFT 全量/按需验收指引后回复“ok测试了”并继续下一项，正常路径按用户反馈登记完成；见 `docs/validation/2026-10-15-offline-modes-wpf-acceptance.md`。没有新增截图、逐项操作日志或延迟测量，不扩大到服务故障等综合场景。
