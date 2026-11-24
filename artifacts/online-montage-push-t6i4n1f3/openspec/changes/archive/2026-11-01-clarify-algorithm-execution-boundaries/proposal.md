## Why

官方算法已经通过统一 Runtime 调度，但信号准备和结果呈现仍穿过多个服务入口，读者需要跨文件确认导联、滤波、单位、窗口和 provenance 是否一致。趁近期导联切换已验收，把这些公共边界整理成清楚的契约，同时落实用户明确的方向：只维护当前版本，不继续背负旧算法结果/API 的兼容路径。

## What Changes

- 为官方算法定义单一、显式的信号输入与预处理访问契约，明确原始/导联投影信号、V/float64、样本坐标、采样率、通道角色/顺序、缺失样本、滤波范围及其 provenance；FAA、Brainbeat 等不同科学需求由算法模块声明并使用，不统一其科学公式。
- 收敛公共结果边界：算法模块声明结果类型和算法专属证据；通用层只负责统一校验、生命周期、公共质量/失败字段、数组 artifact 与 provenance 序列化，不按具体算法 ID 分支解释科学结果。
- **BREAKING**：清点并删除当前官方算法链路中实际可达的旧 Run/结果字段、旧 warmup 状态回退及旧格式解释路径；桌面端和后端按当前契约同步更新。应用只处理当前契约，不读取、执行、转换或专门识别旧版 Run/artifact 格式；本变更不触碰历史存储字节和原始记录。
- 添加覆盖九个官方算法、静态/动态及其已声明执行策略的契约和数值回归，确保整理前后公式、滤波语义、单位、窗口、状态、失败和 provenance 不变。
- 更新当前算法架构说明与新算法接入步骤，明确每次功能验收后必须整理实现、同步 OpenSpec tasks、验证、提交和推送的闭环。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `analysis/algorithm-runtime`：明确统一信号准备/输入契约、模块所有权和当前执行结果形态，并移除旧状态兼容要求。
- `analysis/run-provenance`：明确新运行仅使用当前 provenance/result contract，不再兼容解释旧结果形态。
- `analysis/scientific-backend-boundaries`：明确结果序列化与预处理适配的公共边界及其不得承载算法科学分支的要求。

## Impact

- 影响 `backend/app/algorithm_runtime/`、`backend/app/algorithms/`、`backend/app/services/`、相关 API/持久化模型和 `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/`。
- 可能改变当前客户端与服务端之间的 Run 结果字段；WPF 客户端需在同一变更中更新。旧客户端和旧结果格式不受支持，不增加旧格式识别、读取或转换逻辑。
- 不改变原始 EEG 文件，不删除已保存 Run/artifact 字节；不改变算法公式、科学版本、滤波参数、窗口/质量门、数值单位或临床解释。
- 采用分阶段实施：先建立输入/输出契约基线和逐算法调用矩阵，再迁移公共边界，再删除已经无调用者的旧路径；每阶段以自动化数值对照和契约测试验收。
