# 实现计划与边界

## 数据流

ColorPicker.SelectedColor 双向绑定事件 DraftColor 或导联草稿 Color。
导联草稿的颜色/顺序 -> DerivedMontageChannel -> 配置 JSON -> 采集时不可变
MontageProfile 快照 -> live trace / review frame -> SciChart Stroke。

## 分阶段计划

1. 公共控件：抽离事件色块、HEX、RGB 预览及应用/取消；支持行内紧凑模式。
2. 领域与编辑：增加可选 Color，校验 RGB；用单一排序控制器移动草稿行并
   归一化顺序。颜色不随行位置变动。仅删除负极编号 UI，不删除取数语义。
3. 展示接线：采集 trace 和回溯 frame 携带颜色，同名不同颜色也刷新 Stroke。
4. 回归验证：自动化契约、完整构建及测试，记录硬件/视觉验收边界。

## 历史兼容与身份

- 缺少 Color 的旧配置仍可读取，使用统一默认波形调色板；编辑保存后颜色
  显式保存。默认调色板采用现有采集的八色，回溯同步这套默认值。
- 回溯默认使用采集快照，不通过当前预设补写历史颜色。
- 颜色加入新配置完整指纹；没有颜色的旧配置保持原指纹算法字节结果。
  科学通道指纹及严格通道配置 ID 筛选不变。改变颜色可能使展示帧缓存重建，
  不改变科学信号计算。
- DisplayOrder 为输出排列，不再要求等于正极源通道 DisplayOrder。仍验证
  正负极存在、覆盖规则、唯一非负顺序和所有参考依赖。
- 后端已经按保存的输出顺序解析，不依赖输出序号等于物理输入序号。

## 模块边界

- Shared/Controls：颜色交互，无事件或导联依赖。
- Shared/Display：RGB 解析；Shared/Waveform：默认波形颜色策略。
- Montages/Domain、Validation：持久化与校验。
- Montages/ViewModels：草稿和排序控制器；Views：紧凑颜色、上下移按钮。
- Acquisition/Waveform、Review/Playback/Waveform：传递和渲染展示属性。

## 验收步骤

复制一个系统导联，修改两条导联颜色并交换顺序，保存后重开确认。
采集预览和完成记录回溯应使用同一颜色/顺序，原始输入编号和公式不变。
再修改预设，旧记录默认仍还原采集快照。回溯切换同通道配置导联时按目标
导联颜色与顺序展示。事件编辑原有色块、自定义颜色、取消和保存正常。
