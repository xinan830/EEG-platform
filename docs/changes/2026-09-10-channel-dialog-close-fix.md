# 通道设置弹窗无法关闭修复

## 基本信息

- 变更编号：FIX-2026-09-10-DIALOG
- 日期：2026-09-10
- 负责人：Codex
- 当前版本/提交：工作区变更
- 关联需求或问题：通道设置弹窗的"应用并从头显示""取消"和右上角"×"全部失灵

## 事实与范围

- 用户可观察行为：导入文件后通道设置弹窗立即出现；点击取消、×、Escape 或"应用并从头显示"后弹窗均不关闭。弹窗内部的勾选、全选、清空仍正常。
- 涉及后端模块：无。
- 涉及前端模块：`frontend/src/App.vue`。
- 是否改变 HTTP/WebSocket 契约：否。
- 是否改变时间、单位、通道、滤波或参考语义：否。

## 根因与证据

- 复现步骤：导入任一 BDF/EDF → 打开通道设置（缺陷下导入后即自动出现）→ 点击取消/×/应用。
- 根因（事实）：`useChannelSelection` 返回普通对象，其 `isChannelDialogOpen` 是嵌套的 `Ref<boolean>`。
  Vue 模板只自动解包**顶层**绑定里的 ref，普通对象嵌套属性中的 ref 不解包；
  `v-if="recording && channelSelection.isChannelDialogOpen"` 求值时拿到的是 Ref 对象本身，
  恒为真值——因此只要有 recording 弹窗就渲染，且 `closeChannelDialog()` 把 `.value` 置为
  `false` 后 v-if 仍为真，弹窗永远关不掉。"应用"按钮实际上已触发重读文件，只是弹窗不消失。
- 证据：新增组件测试 `frontend/src/App.channelDialog.test.ts` 在修复前 3 项断言全部失败
  （弹窗默认应关闭却存在；取消/×/Escape 后仍存在；应用后仍存在），修复后全部通过。

## 实现与追溯

- 修改文件和函数：`frontend/src/App.vue`。仿照同文件中 `useDebugSample` 的既有用法，
  在 composable 调用后将 `channelSelection.isChannelDialogOpen` 别名为顶层绑定
  `isChannelDialogOpen`，模板 v-if 改用该顶层绑定并附注释说明解包约束。
- 数据流或状态流变化：无；仅修正模板对同一状态的读取路径。
- 算法/公式来源：不涉及。
- 错误处理和回退行为：不涉及。
- 是否触发 300/400 行拆分规则：App.vue 393 行（<400），仍处于既有拆分评估结论内；
  本次 +2 行未改变结论。新增测试文件 108 行。

## 验证与风险

- 新增或修改的测试：`frontend/src/App.channelDialog.test.ts`（jsdom 环境挂载 App，
  mock API 模块，桩替换依赖 canvas/ResizeObserver 的 WaveformPanel）。覆盖：
  导入后默认关闭、工具栏打开、取消/×/Escape 关闭、应用后关闭并按所选通道从 0 秒重读。
  为此新增 dev 依赖 `@vue/test-utils`、`jsdom`（仅测试使用）。
- 执行命令及结果：`npx vitest run src/App.channelDialog.test.ts` 3 passed；
  `npm test` 9 文件 19 测试全部通过；`npx vue-tsc --noEmit` 通过；
  `uv run python scripts/check_file_sizes.py` 通过。
- 未覆盖的边界：真实浏览器下的视觉层级（z-index/遮罩）不在 jsdom 覆盖范围，本次未改动样式。
- 性能、兼容性、隐私或医学解读风险：无。
- 回滚方式：还原 `App.vue` 两处改动并删除测试文件与两个 dev 依赖。

## 结论

- 是否满足完成定义：是。
- 未完成项和截止日期：无。
