# 统一离线分析执行模式任务

- [x] 增加动态执行方式契约：全量默认，按需受算法能力和功能开关限制。
- [x] WPF 动态参数区提供全量/按需选择，切换时清理旧结果并重新执行。
- [x] 增加不支持按需、按需开关关闭和两种模式选择的 WPF 测试。
- [x] 运行算法专项测试、WPF 全量测试、OpenSpec strict 和 `git diff --check`。
- [x] 用户使用真实记录逐算法验收全量与按需两条路径；2026-10-15 反馈见 `docs/validation/2026-10-15-offline-modes-wpf-acceptance.md`。后续免命令默认开放由 `enable-accepted-offline-analysis-selection` 独立记录。
