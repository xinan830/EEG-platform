# 报告快照基础能力

## 变更

- 新增独立 `report_snapshots` 存储，不把报告内容混入 recording 或 analysis 表。
- 支持创建、列表和按 recording 隔离读取报告快照。
- 快照保存标题和阅图元数据 JSON，例如时间基、滤波、导联和事件摘要；不复制 EEG 样本。
- 创建操作写入审计，参数只记录快照键名，不记录原始信号。

## 边界

- 当前没有报告版本状态、签名导出、PDF/打印模板、身份权限或防篡改存储。
- 快照 payload 由调用方提交，后续需要统一前端 schema 和服务端字段校验。

## 验证

- `backend/tests/test_reports.py` 覆盖创建、列表、读取和 recording 隔离。
