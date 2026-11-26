# 录音绑定三维电极坐标

现有 EDF/BDF 导入的通道配置快照可携带可选 `ElectrodeGeometry`。桌面从同名 `.labels.json` 的 `hardware.channel_configuration_snapshot_json` 原样上传该字段，后端存入原通道快照，无需新数据库字段。现有 WPF 原始录音导出保留该快照。本次不新增 SET/FDT 原生导入或导入文件再导出能力。

```json
{
  "Channels": [
    {"NativeChannelIndex": 0, "ExpectedKind": 1, "ElectrodeLabel": "Array1",
     "IsSelectedForDisplay": true, "DisplayOrder": 0}
  ],
  "ElectrodeGeometry": {
    "schema_version": "recording-electrode-geometry-v1",
    "coordinate_unit": "m",
    "coordinate_frame": "head",
    "source_id": "documented-array-source",
    "source_kind": "source_provided",
    "source_sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "coordinate_transform": "Source already uses MNE head axes and meters",
    "electrodes": [
      {"label": "Array1", "native_channel_index": 0, "position_m": [0.0, 0.0, 0.095]}
    ]
  }
}
```

- `head` 轴向：+X 右、+Y 前、+Z 上。转换在导入前完成，在 `coordinate_transform` 记录原单位、轴向及缩放。不得把 UI 平面坐标当作科学三维坐标。
- `source_kind` 为 `source_provided`、`standard_template` 或 `measured`。模板/来源布局不得标成实测；哈希用于追溯，不能证明来源描述真实。示例哈希只展示格式。
- 标签、原生索引、三维位置不得重复；坐标有限，径向尺度介于 0.005–0.5 m。索引和标签必须匹配快照单极通道（`ExpectedKind=1`）。范围校验仅防单位错误，不能替代物理校准。
- 显式坐标是录音权威来源；缺少电极时返回不可用，不按同名标准电极补齐。旧文件没有该字段时保留标准模板行为。无效字段返回 `ELECTRODE_GEOMETRY_INVALID`，不发布录音。
- 坏导检查、重参考前插值、ICLabel、ICA 头皮图共用坐标。科学会话、连续预处理和 ICA 身份均包含坐标，坐标变更不能命中旧结果。
- 二维地图基于整份坐标统一投影缩放后再取子集，复用绘图并发锁和有界缓存；不改变科学三维位置或信号值。
- 不改变时间、采样率、源列、滤波、ICA 分解算法；不猜测 EXG 眼电/心电身份，不自动应用 ICLabel 推荐剔除。
- 遵守现有快照 65536 字符、导入身份 262144 字符边界。不要将重复报告、事件表装入身份字段。

真实来源未声明物理单位时应保留不确定性。可显式选择有来源的标准头模尺度做科研验收，但必须声明 `standard_template` 和转换，不可把猜测单位包装成实测坐标。
