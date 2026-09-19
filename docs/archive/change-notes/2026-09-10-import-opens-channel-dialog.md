# 导入后先弹出通道设置

## 基本信息

- 变更编号：FEAT-2026-09-10-IMPORT-DIALOG
- 日期：2026-09-10
- 负责人：Codex
- 当前版本/提交：工作区变更
- 关联需求或问题：用户要求导入文件后先显示通道设置，而不是直接进入阅图

## 事实与范围

- 用户可观察行为：
  - 导入 BDF/EDF 后不再立即读取波形，而是自动弹出"通道设置"弹窗，默认预选与原流程相同的默认通道（`chooseWaveformChannels`：偏好 Fz/Pz/Oz(或 O2/O1)/F3/F4，不足 5 个时从剩余通道补足）。
  - 弹窗内"应用并从头显示"：按所选通道从文件 0.0 s 进入阅图（原有行为）。
  - 首次弹窗内"取消"、右上角 ×、Escape 或点击遮罩：按默认通道从文件 0.0 s 进入阅图（即回到导入前的旧默认流程，不会停留在空波形）。
  - 阅图中从工具栏打开的弹窗行为不变：取消只关闭、不重读，避免打断用户当前浏览位置。
- 涉及后端模块：无。
- 涉及前端模块：`frontend/src/composables/useChannelSelection.ts`、`frontend/src/App.vue`（`onImported`）。
- 是否改变 HTTP/WebSocket 契约：否。
- 是否改变时间、单位、通道、滤波或参考语义：否；首次窗口仍从文件绝对 0.0 s 读取。

## 根因与证据

- 复现步骤：导入任一 BDF/EDF → 旧流程直接显示默认通道波形，用户无机会先选通道。
- 根因（事实）：`onImported` 固定调用 `loadReviewWindow(0)`，通道设置只能事后从工具栏进入。
- 需求判断：属于产品流程变更而非缺陷；取消语义取"按默认通道继续"（与旧导入行为一致、不产生空界面死胡同），如需"取消回到文件选择页"另行变更。

## 实现与追溯

- 修改文件和函数：
  - `useChannelSelection.ts`：新增 `openInitialChannelDialog`（导入后首次打开）与内部 `isInitialChoice` 标志；`closeChannelDialog` 在首次模式下触发 `afterResolve`（与确认同一回调：停止回放、重置调试台、从 0 秒重读）；普通模式行为不变。`afterApply` 更名 `afterResolve` 以反映两种触发来源。
  - `App.vue` `onImported`：末尾 `await loadReviewWindow(0)` 改为 `channelSelection.openInitialChannelDialog()`；首次选择的状态归属仍在 composable，App 只持有调用时机。
- 数据流或状态流变化：导入 → 弹窗 →（确认 | 取消）→ `loadReviewWindow(0)`；确认前不再有波形请求。
- 算法/公式来源：不涉及。
- 错误处理和回退行为：弹窗内清空全部通道时确认按钮禁用并提示"至少选择一个通道"（原行为保留）。
- 是否触发 300/400 行拆分规则：App.vue 394 行（+1，仍 <400 且低于登记基线 386 之上的硬上限）；useChannelSelection.ts 43 行。

## 验证与风险

- 新增或修改的测试：`frontend/src/App.channelDialog.test.ts` 重写为导入流程视角，覆盖：
  1. 导入后自动弹出且确认前无波形请求；
  2. 首次取消（含 ×/Escape 及工具栏再开后的取消不重读）按默认通道从 0 秒读取；
  3. 首次确认按所选通道从 0 秒读取且导入阶段只读一次。
- 执行命令及结果：`npm test` 9 文件 19 测试全部通过；`npx vue-tsc --noEmit` 通过；`uv run python scripts/check_file_sizes.py` 通过。
- 未覆盖的边界：遮罩点击关闭走与取消相同的 `closeChannelDialog` 路径，未单独断言（同一函数分支）。
- 性能、兼容性、隐私或医学解读风险：无；首次取消会多一次默认通道的窗口读取，与旧导入流程请求量相同。
- 回滚方式：还原两个文件的改动并恢复旧测试。

## 结论

- 是否满足完成定义：是。
- 未完成项和截止日期：无。
