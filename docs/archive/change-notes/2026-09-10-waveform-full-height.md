# 阅图界面随窗口放大

## 基本信息

- 变更编号：FEAT-2026-09-10-WAVEFORM-SIZE
- 日期：2026-09-10
- 负责人：Codex
- 当前版本/提交：工作区变更
- 关联需求或问题：用户反馈阅图界面太小，希望更大

## 事实与范围

- 用户可观察行为：阅图区域不再固定为 485px 高，而是填满窗口剩余的高度与宽度；窗口越大、阅图区越大。波形面板的宽度取消 1600px 上限，随窗口延伸。窗口过小（高度不足）时以约 485px 为下限并允许页面滚动，不会比改动前更小。
- 涉及后端模块：无。
- 涉及前端模块：`frontend/src/styles.css`（仅布局规则）。
- 是否改变 HTTP/WebSocket 契约：否。
- 是否改变时间、单位、通道、滤波或参考语义：否（波形绘制逻辑、滚动缩放均未改，`WaveformPanel` 的 ResizeObserver + `clientWidth/clientHeight` 自绘天然适应新尺寸，无需代码改动）。

## 根因与证据

- 根因（事实）：波形绘图区 `.plot-surface { height: 485px }` 固定高度；主列 `.focused-body`/`.main-column`/`.waveform-panel` 不是纵向弹性布局；外层还有 `max-width: 1600px`。三者共同把阅图区锁死为固定大小，大屏浪费大量空间。
- 证据：真实浏览器（dev 服务器 720px 高的窗口中）实测 `.waveform-panel` 高 596px、底边到 706px（近满屏）、宽 1252px（近全宽）；改动前该面板在任意窗口高度下仅约 600px 高。

## 实现与追溯

- 修改文件和函数：`frontend/src/styles.css`
  - `.desktop-app` 增加 `display:flex; flex-direction:column`。
  - `.focused-body` 改为纵向 flex、`flex: 1`、移除 `max-width:1600px`、`width:100%`。
  - `.main-column` 增加纵向 flex、`flex: 1`。
  - `.waveform-panel` 增加纵向 flex、`flex: 1`。
  - `.plot-surface` 用 `flex: 1 1 auto; min-height:485px` 替换固定的 `height:485px`。
  - `.empty-waveform` 用 `flex: 1 1 auto; min-height:515px` 替换固定的 `height:515px`。
- 数据流或状态流变化：无。
- 错误处理和回退行为：通过各层默认 `min-height:auto` 保留内容下限，小窗口时面板不会压垮、页面可滚动。
- 是否触发 300/400 行拆分规则：styles.css 行数不变，其他前端文件未改。

## 验证与风险

- 新增或修改的测试：无（纯 CSS 布局，jsdom 无布局引擎，`getBoundingClientRect` 恒 0，加高度断言无意义）；以真实浏览器实测代替。
- 执行命令及结果：`npx vitest run` 9 文件 19 测试全部通过；`npx vue-tsc --noEmit` 通过；`uv run python scripts/check_file_sizes.py` 通过。
- 验证方式：dev 服务运行中，浏览器实测面板填满窗口（见"根因与证据"）。
- 未覆盖的边界：示例录制的完整阅图态（波形 + 时间轴 + 总览条）在大窗口下的视觉间距未逐像素核对，但弹性比例由 flex 决定、波形自绘随容器缩放。
- 性能、兼容性、隐私或医学解读风险：无。航线级（trajectory）分辨率随宽度增加而提高，符合阅图需求。
- 回滚方式：还原 styles.css 的 6 处改动。

## 结论

- 是否满足完成定义：是。
- 未完成项和截止日期：无。