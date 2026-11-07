# 回放优化、样式与缓存设置归档验证

日期：2026-11-07。基于 `8fc7ff8` 之后的当前工作区；用户授权归档、提交并推送 GitHub。

提交检查期间确认功能源码已在 `776de55` 提交并与远端 `main` 一致；本次后续提交保留该提交，仅补齐正式规格同步、归档与本验证记录。

## 归档范围

下列 5 项变更的 35 个任务均已完成；先同步正式规格，再移动至 `openspec/changes/archive/2026-11-07-<change>/`：

- `add-psd-all-band-topomap`：PSD 单频段/全部频段地形图、实际量级色阶、AVG 空间位置。实现此前已经提交，本次补齐规格同步及归档。
- `format-custom-frequency-values`：自定义频率表格统一两位小数，保持底层精度。
- `unify-slim-scrollbars`：共享细滚动条，保留翻页与拖动。
- `fix-cold-spectral-playback-lag`：PSD/RBP 数值面板及 FAA/IAPF 的有界提前准备、连续源共享及终态交付优化。
- `add-settings-cache-cleanup`：设置页清除回放缓存及动态窗口记录，保留原始录音和历史结果。

共同步 6 个正式能力文件、12 个需求块；每个 delta 的完整需求及全部场景均与正式规格逐项比对一致。缓存清理的新能力补齐 Purpose。正式规格校验 **89 通过、0 失败**；仍有既有 Purpose 占位和过长描述警告，本轮未批量改写历史规格。5 项变更严格校验均通过。

## 本次提交前回归

- 后端：**230 通过**，2 条警告，133.26 s。覆盖 FAA 源复用和数值一致性、IAPF/官方算法、PSD/RBP、预处理身份/缓存/资源预算、动态窗口、频率投影、缓存清理、恢复、full-window reuse 和 TNK 交付。使用临时测试数据，未清除用户缓存。
- 桌面：**530 项，528 通过、2 失败**。构建成功；覆盖 Review、动态观察器/交付、on-demand、Settings 和细滚动条。两项失败均与此前验证和基线 overlay 记录一致，没有新增失败：`Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel`（期望 1、实际 0）和 `Dragging_exclusively_owns_position_then_restores_previous_playback_state(wasPlaying: True)`（期望暂停、实际播放）。保留失败，未删改断言。现有 NU1701、CA2022、xUnit2013 警告仍在。
- 本轮文件规模检查：涉及源码已按职责登记；全仓检查仍因本次未修改的 `backend/app/algorithms/psd/runner.py` 为 464 行、超过原登记 420 行而失败。本次未为通过检查而放宽该文件上限。
- 提交排除本地构建、测试输出、缓存、数据库、录音和日志；提交前检查暂存路径、文件大小及 `git diff --cached --check`。

后端复现命令（仓库根目录）：

```powershell
& backend/.venv/Scripts/python.exe -m pytest backend/tests/test_faa_shared_preparation.py backend/tests/test_faa_continuous.py backend/tests/test_faa_on_demand_capability.py backend/tests/test_iapf_algorithm_module.py backend/tests/test_official_algorithm_runs.py backend/tests/test_spectrum_service.py backend/tests/test_prepared_source_identity.py backend/tests/test_prepared_source_cache.py backend/tests/test_block_preprocessing.py backend/tests/test_stft_bounded_preprocessing.py backend/tests/test_rbp_channel_quality.py backend/tests/test_on_demand_resource_budgets.py backend/tests/test_on_demand_dynamic_service.py backend/tests/test_custom_frequency_window_api.py backend/tests/test_on_demand_rbp_api.py backend/tests/test_on_demand_psd_api.py backend/tests/test_cache_maintenance.py backend/tests/test_offline_window_reuse.py backend/tests/test_on_demand_process_recovery.py backend/tests/test_tnk_playback_delivery_api.py backend/tests/test_on_demand_failure_injection.py -q --disable-warnings --maxfail=2
```

桌面复现命令：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --filter '(FullyQualifiedName~Tests.Review&FullyQualifiedName!~Provided_recording)|FullyQualifiedName~DynamicWindowObserverTests|FullyQualifiedName~DynamicWindowDeliveryTests|FullyQualifiedName~OnDemand|FullyQualifiedName~Tests.Settings|FullyQualifiedName~SlimScrollBarStyleTests' --nologo -v quiet -p:OutDir=D:/AI/Work/brain-platform/_temp/publish-validation/ --logger 'trx;LogFileName=publish-validation.trx' --results-directory D:/AI/Work/brain-platform/_temp/publish-validation-results
```

## 人工验收边界

本次没有操作运行中的 GUI、真实采集硬件或真实缓存，未测长时间冷缓存吞吐量。环境依赖的 `Provided_recording` 用例未执行；归档不表示这些缺口已通过。首次冷打开仍需要预处理，清理后缓存会重新生成。

更详细的证据见 [冷缓存播放](2026-11-07-cold-spectral-playback-lag.md)、[FAA/IAPF 后续修复](2026-11-07-faa-iapf-playback-lag.md) 和 [设置页缓存清理](2026-11-07-settings-cache-cleanup.md)；样式用例结果保留在相应归档的 `tasks.md`。
