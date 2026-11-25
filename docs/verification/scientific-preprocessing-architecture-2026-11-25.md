# 科学预处理架构整改验收 — 2026-11-25

## 范围与约束

对应 OpenSpec：`regularize-scientific-preprocessing`。本次整理离线准备入口、有效配置、共享原语、缓存发布所有权和前后端算法能力契约。保留本轮开始前工作区已有的 Review 修复，未提交或推送。

保持原始文件不变；内部 `(samples, channels)`、V/float64、显式通道顺序与 NaN 断层语义不变；HTTP 科学波形预览仍为绝对文件秒与 µV。波形因果显示滤波独立于后端科学处理，未修改实时采集算法公式、滤波系数、阶数、处理顺序或窗口边界。

## 当前所有权

1. `OfflineScientificPreprocessingService` 是连续准备和有界窗口准备的生产门面。PSD/RBP/FAA 等调用、科学预览和 ICA 审查均经门面；TNK/STFT 删除缺少门面时直接执行低层准备的分支。
2. 门面组装并校验 `OfflinePreprocessingSpec`；`ContinuousPreprocessingService.load` 仅接收有效规格，执行层不再用散落参数重新解释策略。连续全段和有限上下文窗口仍为两个科学上下文。
3. 门面构建只含样本访问/溯源回调与 ICA 缓存的 `PreprocessingRecordingAccess`，并拥有连续缓存与有界缓存的失效调度。RecordingService 保留兼容委派 API 和目录/文件持久化，不再选择科学准备引擎。
4. 共享 `detrend_and_filter_for_inspection` 原样迁至 `scientific.primitives.inspection_filter`，通过 primitives 门面导出；有限窗口坏段应用归 `window_artifact_qc` 所有。连续执行器 141 行、离线门面 342 行、窗口质控 72 行、原语 53 行。
5. 全量和按需入口共同调用 `with_offline_preprocessing` 构建科学身份中的策略证据。有效策略、通道投影、滤波 Q、源文件身份继续参与原缓存身份。

## 缓存修复

- 先新增可复现测试：连续缓存样本可写、失效后新请求仍加入旧准备；修复前 2 failed / 12 passed。
- 连续数据在发布和返回前设置只读，包括已失效任务返回给原消费者的结果。真实 EDF 读取适配器 + memmap 准备路径也验证重复请求同对象且赋值被拒绝。
- 在途 Future 自身是发布令牌：失效摘除 Future；后续请求独立准备；旧任务可完成，但不能覆盖新缓存。缓存移除不强制关闭仍被算法持有的 memmap。
- ICA 拟合前捕获录音发布令牌；单录音失效或全清后，旧拟合不能重新写入。默认无需 ICA 的准备不创建 ICA 令牌。

## 前后端能力契约

算法 manifest 的 `SignalRequirements.supports_artifact_removal` 显式声明支持，默认 false。连续算法声明 true；STFT/TNK 保持 false。现有 `/api/algorithms` 增量输出 `signal_requirements`，桌面 DTO 安全解析，缺失或错误类型视为不支持。

Review 从当前算法目录对象决定是否提交去伪迹，移除 `stft/tnk_biofeedback` ID 排除表；保留同一个 ReviewPreprocessingViewModel 状态所有者和已有范围说明。后端全量与按需正式入口也校验，直接调用有界准备仍拒绝 ICA/SSP/EOG。

## 已执行验证

- 原语迁移对比 Git HEAD 原函数：20/3000 样本 × notch=None/50/60，共 6 组，两份输出均逐位相等，包含 NaN 断层。持久回归覆盖频率响应、短段复制、输入不变和断层保留。
- 后端 25 个相关模块覆盖：坏导/坏段/去伪迹、ICA API/预览、缓存并发失效、连续/有界身份与 Q、FAA 数值与共享准备、PSD/RBP/STFT/TNK、录音读取删除、全量和按需正式入口，以及架构边界：243 passed / 3 failed（49.31 秒），失败项见下节。之后新增入口所有权静态回归，并连同缓存与规模策略再次运行：30 passed / 0 failed；该数字与前一套件有重叠，不相加。
- 桌面针对目录能力、Review 预处理状态、ICA 包络、实际算法请求组装和注册：42 passed / 0 failed。新增相同 RBP ID 在能力声明 true/false 下分别携带/省略去伪迹的行为验证。
- Release 构建成功，0 warning / 0 error。
- `git diff --check`、代码规模策略与 OpenSpec strict 校验通过。401 行运行时契约登记同一契约族评审；618 行 RecordingService 按 >600 高风险规则更新评估，已有目录持久化拆分后续项仍保留。
- 桌面架构 HTML 四页同步当前调用链；Chrome 渲染截图，1600px 视口均无横向溢出，离线回溯全页人工查看通过。仅修改文案和节点名称。Modern Web Guidance 检索静态文档主题，无新的 HTML/CSS/浏览器 API 模式需要应用。

## 尚未解决与验收边界

扩大后的桌面回归首轮总计 154 项，142 passed / 12 failed：其中新增测试的 2 项夹具缺失来源目录已修正，并在 42 项针对性回归中通过；其余 10 项分布于标注窗口布局/命令和播放导航/结果通知测试。后两类单独重跑仍失败（2 failed / 1 passed），不能声称桌面全量通过。此次没有修改这些功能或对应断言。

后端架构检查仍有整改前已记录的 3 项失败：Brainbeat `window_s.maximum=30`、`custom_frequency_inspection.py` 导入科学原语具体子模块、`live_rbp_algorithm.py` 导入算法科学实现。未为使测试变绿而改动其他公式/窗口约束；本次科学层不依赖 services 的边界检查通过。

未运行完整后端/桌面全部测试；未重做真实受试者录音效果验收、硬件采集或人工桌面交互验收。API/算法数值与合成 EDF 流程验证通过不等于真实 EEG 科学效果已全面认证。
