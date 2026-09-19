# 频谱 API 与连续预处理缓存

## 变更内容

新增 `GET /api/recordings/{recording_id}/spectrum`，返回 `offline-spectral-v3` 的 PSD、绝对频段功率、RBP、单位、质量门和算法契约。新增进程内 LRU 连续预处理缓存，避免不同时间窗口或通道顺序重复执行整段零相位滤波。

## 约束

第一版 API 只接受 `start_s`、`window_s` 和 `channels`；参考、滤波和 Welch 参数仍由 v3 固定。请求通道顺序会原样保留。

## 验证

新增服务测试覆盖通道顺序、单位、RBP 和缓存复用。后端完整测试：78 项通过。

## 风险

缓存是进程内、有限容量的派生数据，重启后会重建；尚未实现跨进程共享和长记录磁盘缓存。当前未修改 Viewer Pipeline，未新增前端频谱页面。
