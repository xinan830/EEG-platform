# 数据契约与处理链路

## 科学分析

```text
BDF/EDF 或已完成的 WPF 原始记录
  -> 记录读取适配与实际通道校验
  -> 明确选择的分析导联/参考投影
  -> 算法声明的预处理及处理范围
  -> typed 时序/频谱/时频输入
  -> 算法模块独立计算与质量门
  -> typed result 校验、Run/artifact 和 provenance
  -> WPF 结果展示
```

原始采集字节不修改。切换导联保存独立分析快照，重新绑定输入、缓存与结果身份。仅具备实际源通道的配置才可使用；显示波形的导联选择不能替代算法请求。

时序输入为 `(samples, channels)`、V/float64，列顺序与有序角色和通道名一一对应。频谱与时频输入另有轴/单位/形状契约。采样率、录制相对绝对样本范围、缺样策略及 actual range 必须可追溯，不按 UI 定时器推算科学坐标。

## 统一的范围

`backend/app/services/algorithm_recording_context.py` 将记录服务适配到统一 typed source；请求由各模块按科学需要构造。FAA 的配对时序、Brainbeat 的 Fz/Pz 频谱、Welch 和 STFT 不能混成同一种计算。

共享管理统一校验、身份、调度、缓存、取消与持久化；滤波是否开启、连续处理范围、epoch 和质量规则仍由算法契约决定。频谱输出限制为 1–30 Hz 不等于对原始信号做了 1–30 Hz 预滤波。

## 显示链路

采集实时显示和阅图有独立的显示滤波、缓存、导联与渲染生命周期。它们可以产生 µV 波形和显示抽点，不得把抽点后的图形数据当作官方算法输入。详见[采集与阅图](../architecture/desktop-acquisition-review.md)和[输入/输出指南](official-algorithm-development.md)。
