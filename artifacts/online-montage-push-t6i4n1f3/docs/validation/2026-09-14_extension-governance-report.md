# 扩展治理边界验证报告

日期：2026-09-14

## 范围

本次变更只加入本地模块 manifest 的验证和能力目录。没有增加插件安装、任意 Python、网络执行或多租户。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| 三种允许来源 | PASS：Official、Research Template、User Private |
| 科研类型白名单 | PASS：未知 `PythonPlugin` 被拒绝 |
| metadata-only 权限 | PASS：声明权限被拒绝 |
| 代码传输字段 | PASS：`python_source` 作为未知字段被拒绝 |
| 后端治理测试 | PASS：3 passed |

## 风险说明

Manifest 的权限为声明式元数据，不授予权限，也不代表沙箱。未来任意代码插件必须作为独立安全 change 实施隔离、禁网、资源限制和依赖审计。
