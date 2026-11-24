## Purpose

为算法详情提供受记录历史约束的导联配置选择，使采集时确定的通道角色与
用户选择的导联配置保持联动，并保证每次算法结果都使用可验证的实际信号和
完整 provenance。

## ADDED Requirements

### Requirement: Expose historical analysis montage choices

算法详情 SHALL 展示采集历史目录和显式提交、后端验证后持久化的兼容分析配置快照，并默认选择采集时保存的导联配置。可选配置 MUST 绑定该记录的通道快照及实际源信号，不得根据当前全局设置隐式替换历史映射。

#### Scenario: Register current compatible alternatives for a record without a saved catalogue

- **WHEN** 客户端显式提交与记录通道兼容的完整导联配置定义
- **THEN** 后端独立验证设备、通道配置身份、物理映射、硬件参考及地、公式依赖和 V 单位
- **AND** 验证通过的定义单独保存为记录范围内的不可变分析快照，内容变化生成不同身份
- **AND** 下拉框返回新配置及其分析输出，重新加载仍可解析相同快照
- **AND** provenance 区分采集快照与分析配置快照，原始 manifest 和样本文件不变

#### Scenario: Open a record with a saved acquisition montage

- **WHEN** 用户在算法列表选择一条具有有效通道配置和导联映射快照的已完成记录
- **THEN** 页面显示导联配置下拉框
- **AND** 默认值是采集时保存的导联配置
- **AND** 页面不修改原始采集数据

#### Scenario: Historical montage choices are incomplete

- **WHEN** 记录缺少导联目录、源通道映射不唯一或快照与原始通道表不一致
- **THEN** 页面显示结构化不可用原因
- **AND** 不允许提交依赖该配置的算法 Run
- **AND** 不得使用当前全局配置或通道位置猜测替代映射

### Requirement: Link montage configuration to analysis channels

算法详情 SHALL 提供独立的“分析导联”选择。其选项 MUST 由当前导联配置、记录实际存在的源通道和算法声明的输入角色共同决定。切换配置时 SHALL 保留通道角色，并将每个角色映射到当前配置下的对应导联输出。

#### Scenario: Switch REF to average reference

- **WHEN** 记录的有效通道角色为通道 1、通道 2，用户将导联配置从 REF 切换为 AV
- **THEN** 分析导联选项从 “通道1-REF”、“通道2-REF” 更新为 “通道1-AV”、“通道2-AV”
- **AND** 已选通道 1 自动切换为 “通道1-AV”
- **AND** 页面显示的分析导联与实际请求输入一致

#### Scenario: Selected role is unavailable in a montage

- **WHEN** 当前导联配置不能为已选通道角色生成对应导联
- **THEN** 该选项不可选
- **AND** 页面显示缺失源通道或不兼容原因
- **AND** 不得静默改用另一个通道或保留旧导联结果

### Requirement: Recalculate every official algorithm after montage change

导联配置或分析导联改变 SHALL 创建新的科学配置修订，并触发九个官方算法统一的重新计算流程。静态、动态全量、按需和组合策略 MUST 使用同一配置身份和竞态规则。

#### Scenario: Change montage while no run is active

- **WHEN** 用户将算法详情从 REF 切换到 AV
- **THEN** 当前算法的请求配置立即变为 AV 对应分析导联
- **AND** 运行结果、动态预览和状态清除或标记为旧配置
- **AND** 新计算使用 AV 信号，不复用 REF 结果

#### Scenario: Change montage while a run or preview is active

- **WHEN** 用户切换导联配置时旧 Run、动态窗口请求或后台全量任务仍在运行
- **THEN** 旧请求被取消或其返回结果被配置修订拒绝
- **AND** 旧进度和结果不得覆盖新配置
- **AND** 新配置可以提交新的全量、按需或组合执行

#### Scenario: Switch montage for a paired or multi-channel algorithm

- **WHEN** FAA、Brainbeat 或其他声明多个输入角色的算法切换导联配置
- **THEN** 每个输入角色都映射到同一当前配置下的对应导联
- **AND** 配对关系、通道顺序和算法专属质量规则保持不变
- **AND** 缺少任一角色时 Run 结构化失败，不以单通道替代

#### Scenario: Request a cursor window while full analysis is progressing

- **WHEN** 组合模式的全量任务保留了待发布窗口且用户请求另一个游标窗口
- **THEN** 全量生产任务不占用按需队列、项目或记录的按需额度
- **AND** 按需生产任务仍受相应容量上限和共享计算并发限制
- **AND** 同一窗口已有活动生产者时复用该生产者，不创建重复计算或夺取运行中的租约

### Requirement: Bound avoidable on-demand observation delay

The shared observer SHALL use a short polling interval during the first second
of a pending window and back off for long source preparation. The coordinator
SHALL count elapsed observation and cleanup time toward its minimum start
interval while retaining one active observation and latest-pending coalescing.
This transport policy SHALL NOT alter scientific windows or compute concurrency.

#### Scenario: A short window becomes ready during source preparation

- **WHEN** a pending window is still within its first second of observation
- **THEN** the observer schedules its next status read after 100 ms regardless of the pending state
- **AND** cancellation still releases only the owned subscription

#### Scenario: A slow observation already consumed the minimum interval

- **WHEN** the previous observation and cleanup have taken at least the coordinator cadence
- **THEN** the latest pending target starts without an additional post-completion cadence delay
- **AND** observations remain sequential

### Requirement: Persist montage-aware request provenance

每个新官方 Run SHALL 记录导联配置标识、配置版本或指纹、分析导联输出、源通道映射、公式、采样率、单位和实际分析范围。算法科学版本和实现身份仍由后端当前算法模块提供。

#### Scenario: Complete run after montage selection

- **WHEN** AV 配置下的算法 Run 完成
- **THEN** Run provenance 包含 AV 的配置身份和映射指纹
- **AND** 包含实际分析导联名称、源通道及其公式
- **AND** 结果单位、采样率和实际范围仍由后端返回并可追溯

#### Scenario: Historical result is from another montage

- **WHEN** 当前请求选择 AV 而已有完成 Run 使用 REF
- **THEN** REF Run 不被视为当前 AV 结果
- **AND** 只有科学配置指纹匹配的结果可以复用

