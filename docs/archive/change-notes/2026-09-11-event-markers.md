# 人工事件标记后端

## 契约

- `POST /api/recordings/{recording_id}/events` 创建事件。
- `GET /api/recordings/{recording_id}/events` 按文件绝对时间升序返回事件。
- `DELETE /api/recordings/{recording_id}/events/{marker_id}` 删除指定文件内的事件。
- 请求字段：`time_s >= 0`、`label` 长度 1–120、可选 `duration_s`。
- 事件时间和持续时间不得超出文件时长。

## 实现

- 新增 SQLite `event_markers` 表和 `EventMarkerService`。
- 创建与删除操作写入 `audit_events`，参数中不包含 EEG 样本。
- 事件按 `recording_id` 隔离，删除接口不能操作其他文件的标记。

## 边界

- 前端时间轴交互已接入，事件使用文件绝对时间创建、跳转和删除。
- 没有操作者身份、权限和防篡改审计，因此不能视为临床合规事件记录。
- 文件删除、导入替换时的事件清理策略将在报告/生命周期设计中统一处理。

## 验证

- `backend/tests/test_events.py` 覆盖排序、文件隔离和幂等删除。
- 后端全量测试：44 个通过。
