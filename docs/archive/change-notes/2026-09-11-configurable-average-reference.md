# 可配置平均参考

## 基本信息

- 变更编号：MONTAGE-AVG-2026-09-11
- 日期：2026-09-11
- 范围：平均参考参与通道的可配置性

## 变更

- 平均参考默认使用文件中全部有效通道，界面标记为 `AVG-All`。
- 用户可以从文件的完整通道列表中任意勾选排除项，不限于预设的 Fp1、Fp2、F7、F8。
- EOG、ECG、坏道或其他不适合参与共同平均的通道，由用户按当前文件的通道命名和质量判断自行排除。
- 静态窗口、连续 WebSocket 回放和算法检验共用同一份排除配置。
- 后端对排除通道按实际文件通道解析；不存在的通道返回明确错误，不静默忽略。

## API 契约

窗口和算法检验接口使用相同的查询参数：

```text
average_exclude=Fp1,Fp2,EOG
```

播放创建接口使用相同语义的 JSON 字段：

```json
{
  "montage": "average",
  "channels": ["F3", "Fz", "F4"],
  "average_exclude": ["Fp1", "Fp2", "EOG"]
}
```

未提供 `average_exclude` 时，参与平均的是文件中全部通道（`AVG-All`）。

## 边界

- 当前不会根据通道名称自动判断 EOG/ECG，也不会自动检测坏道；自动质量标记需要独立的质量评估规则和人工复核流程。
- 排除通道只改变 Average Reference 的参与集合，不会从原始 BDF/EDF 中删除数据，也不会阻止用户单独显示该通道。
- `AVG-All` 是当前文件全部通道的共同平均，不代表所有临床系统都采用同一组电极；后续可在此基础上增加可保存的命名预设。

## 验证

- 单元测试覆盖任意通道（含 EOG 命名）的排除和平均值计算。
- 前后端接口均传递 `average_exclude`，确保静态读取与连续回放行为一致。
- 证据：`backend/tests/test_montage.py`、`backend/app/services/montage.py`、`frontend/src/components/MontageSelector.vue`。
- 结论：排除集合由请求和界面状态决定，不依赖 Fp1/Fp2/F7/F8 的硬编码名单。
