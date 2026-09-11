# Montage 与参考导联

实现位置：`backend/app/services/montage.py`。

## 统一数学模型

每条显示导联都是线性表达式：

```text
Y(t) = Σ(weightᵢ × channelᵢ(t))
```

权重是乘数，不是灵敏度或百分比。普通双极 `Fp1-Fp2` 等于 `1×Fp1 + (-1)×Fp2`。

## 当前预设

- `original`：原始记录，不重参考。
- `average`：参与通道平均值；支持 `average_exclude`，可排除 Fp1/Fp2/F7/F8、坏道或非 EEG 通道。
- `linked_mastoids`：`通道 - (M1+M2)/2`。
- `reference:<channel>`：任意可用通道参考。
- `cz_reference`：Cz 参考。
- `longitudinal_bipolar`：标准纵向双极链（临床常称 double banana）。
- `transverse_bipolar`：横向双极链。
- `custom_bipolar`：结构化自定义线性表达式；名称沿用旧 ID，语义实际是 custom linear montage。

## 自定义表达式约束

请求结构为 `name + terms[]`，每项包含 `channel` 和非零有限 `weight`。后端逐项解析，不使用 `eval`，禁止重复通道、未知通道和空表达式。

## 参考范围

平均参考的参与集合由文件通道减去 `average_exclude` 得到，不是写死 21 通道。EOG、ECG、EMG 或坏道是否排除，必须由调用方配置并记录。

## 风险

不同通道集合会产生不同 AVG 数值；平均参考后的全部参与通道理论和接近 0，但显示子集本身不一定接近 0。
