# 频谱前端第一版

## 变更内容

新增 `SpectrumPanel`、`useSpectrum`、Spectrum API 类型和客户端。面板只消费后端 `offline-spectral-v3` 结果，显示 PSD 曲线、Delta/Theta/Alpha/Beta 绝对功率与 RBP、单位、参考和质量信息。

## 约束

前端不执行 FFT、滤波或频段积分；请求竞态由 composable 的 request id 防护。Viewer 波形处理链未修改。

## 验证

- 后端：78 项测试通过。
- 前端：29 项测试通过。
- `vue-tsc --noEmit` 通过。
- 未执行 `npm run build`，遵循当前开发约束。

## 未完成

Spectrogram、频谱缩放交互、频段自定义和长时间频谱缓存仍未实现。
