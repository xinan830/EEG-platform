# 操作审计基础

## 已实现

- SQLite 新增 `audit_events` 持久化表，记录操作时间、动作、结果、recording/session/request 标识和参数快照。
- 已接入文件导入、静态窗口读取、算法检验、波形播放创建和播放控制。
- `GET /api/audit/events` 支持按 `recording_id` 查询，`limit` 范围为 1–1000。
- 审计参数不包含原始或处理后 EEG 样本，也不保存上传文件内容。

## API 示例

```text
GET /api/audit/events?recording_id=<recording_id>&limit=100
```

返回字段包括 `id`、`occurred_at`、`action`、`outcome`、`recording_id`、`session_id`、`request_id`、`actor_id` 和结构化 `parameters`。

## 不能宣称的能力

- 当前没有登录体系，`actor_id` 为空，无法证明具体操作者身份。
- 当前数据库记录不是防篡改账本，没有数字签名、哈希链、保留策略或外部归档。
- 查询接口目前没有权限控制，仅适合本地开发验证；正式部署前必须先接入身份认证和最小权限。
- 尚未实现人工事件标记、报告版本、签名导出和失败操作的完整审计。

## 验证

- 测试覆盖跨服务实例持久化、按记录筛选、数量上限和 API 响应字段。
- 后端全量测试：42 个通过。
