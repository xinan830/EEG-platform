## Decision

复用 App.xaml 已合并的 ScrollBarStyles.xaml，增加基于现有命名样式的隐式 ScrollBar 样式，不逐页增加可视树遍历或更换 ScrollViewer 模板。

纵向设置宽度 6 DIP、横向设置高度 6 DIP，并清除横向固定宽度；滑块宽度/高度 4 DIP、圆角 2 DIP。横向 Track 使用从左到右的值增长方向，保留标准 ScrollBar 分页命令和 PART_Track 拖拽处理。透明分页按钮不绘制原生粗边框。

## Compatibility And Validation

保留 SlimTableScrollBarStyle 资源键及原附加行为消费者，避免页面资源引用失效。通过实际 WPF 滚动容器验证双向尺寸、布局、分页、拖拽和溢出范围；运行回放表格回归。文件均低于 400 行，无后端或存储变更，后端测试不适用。真实鼠标命中手感需用户目视验收。
