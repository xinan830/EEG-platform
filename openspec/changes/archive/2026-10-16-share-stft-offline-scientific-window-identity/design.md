# 设计

STFT 的完整动态外层窗口使用 `window_s` 与固定 4 秒内层段、1 秒步长。输出 `time_center_s` 必须是相对外层窗口起点的坐标，完整 5 秒窗口形状为 2 x frequency。按需窗口执行时保留相同的坐标和矩阵形状；Partial/warm-up 窗口可继续展示真实有效行，但不进入全量复用集合。

WPF 按需展示适配器保留记录绝对时间的用户界面语义：它先验证后端返回的是相对内层坐标，再加上窗口 `start_s` 一次，禁止把旧的绝对坐标 artifact 当作新契约结果展示。STFT implementation identity 随坐标契约更新为 `stft-runtime-v4-relative-window-times`，使旧身份不会进入新复用会话。

窗口复用继续使用公共科学身份、项目/Recording 范围和精确半开样本范围。STFT 模块声明复用能力时必须验证：状态为 ready、分析状态为 Complete、内层时间轴相对坐标正确、频率轴与当前采样率/参数一致、线性和 dB 矩阵形状及单位符合模块 schema。不能因为输出 JSON 可解析就认为可复用。

全量 STFT 命中后只把已验证的完整窗口矩阵加入当前结果，未命中窗口调用同一个模块执行。完整 artifact 独立写入，复用来源和 miss reason 进入 execution provenance；不写回按需 artifact，不修改原始记录。

验证分两层：先用独立执行避免缓存命中的同一窗口比较坐标、矩阵、单位、质量和证据；再用真实本地 Recording 先请求一个按需完整窗口、再执行全量，断言复用数量大于零、完整结果保持一致、原始文件哈希不变。暖机窗口、频率轴篡改、矩阵形状篡改和旧 manifest 必须回退重算。
