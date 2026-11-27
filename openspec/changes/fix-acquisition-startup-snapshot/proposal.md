# Proposal

## Why
启动预检完成后仍需异步配置设备和保存设置；此时滤波编辑及之前排队的延迟更新可以覆盖已验证快照，造成 500 Hz 采样配上 300 Hz 低通且提示仍为 70 Hz。

## What Changes
- 启动全程禁止滤波编辑，在应用启动快照前等待旧滤波更新退出。
- 在打开数据流前再次核验其他启动输入，拒绝使用已过期的准备结果。
- 添加保存等待、预存延迟更新和失败恢复测试，校正规模登记。

## Capabilities
### New Capabilities
无。
### Modified Capabilities
- `desktop/acquisition-preparation`: 启动期间参数、执行与提示保持一致。

## Impact
仅涉及桌面采集工作区、显示偏好、启动协作者及测试。没有新增依赖、API/WebSocket、文件格式或数据库迁移；时间基准、V/µV、通道顺序和科学算法不变。不扩展本次目录整理；撤回本变更即可回滚。
