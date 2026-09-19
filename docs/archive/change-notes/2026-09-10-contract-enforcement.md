# 工程契约落地

## 基本信息

- 变更编号：CONTRACT-2026-09-10
- 日期：2026-09-10
- 负责人：Codex
- 当前版本/提交：工作区变更
- 关联需求或问题：将工程开发契约变成可执行约束

## 事实与范围

- 新增 FastAPI 请求 ID、统一错误响应和请求耗时日志。
- 新增前端结构化 API 错误解析。
- 新增代码规模检查、超限遗留基线和 GitHub Actions 质量门禁。
- 将 IAPF 质量纯逻辑拆到独立模块，将前端设置、调试台、顶部工具栏拆成组件，并将调试采样状态抽为 composable。

## 根因与证据

- 原契约只有文档，没有自动执行机制。
- 原 `processor.py`、`App.vue`、`iapf_estimator.py` 超过 400 行。
- 原 API 错误依赖 FastAPI 默认 `detail`，无法稳定追踪请求。

## 实现与追溯

- 后端追踪与错误：`backend/app/core/api_contract.py`、`backend/app/main.py`。
- 规模检查：`backend/scripts/check_file_sizes.py`、`docs/code-size-baseline.json`。
- 前端拆分：`frontend/src/components/DisplaySettingsPanel.vue`、`DebugConsole.vue`、`ViewerToolbar.vue`、`frontend/src/composables/useDebugSample.ts`。
- IAPF 拆分：`backend/app/eeg_core/iapf_quality.py`。
- 自动化门禁：`.github/workflows/quality.yml`。

## 验证与风险

- 后端：`pytest -q`，20 passed。
- 前端：`npm test -- --run`、`npx vue-tsc --noEmit`，均通过。
- 规模检查：通过；超限文件仍登记在遗留基线中，CI 禁止继续增长。
- 未完成项：`processor.py` 仍需按指标职责继续拆分；本次未改变其算法行为。
- 风险：统一错误响应可能影响依赖旧 `detail` 结构的外部客户端，当前保留 `detail` 兼容字段。

## 结论

- 基础工程契约已具备自动检查和 CI 门禁：是。
- 所有超限生产文件已完成拆分：否，`processor.py` 为已登记遗留项。

`processor.py` 的拆分是下一次独立变更；在完成前，项目不能宣称已完全满足 400 行硬规则。
