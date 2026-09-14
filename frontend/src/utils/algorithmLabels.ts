import type { AlgorithmDefinition } from '../types/algorithmDefinition'

export interface AlgorithmLabel {
  name: string
  abbreviation: string
  purpose: string
  steps: string[]
  result: string
}

const OFFICIAL_LABELS: Record<string, AlgorithmLabel> = {
  'Official THETA_BETA': {
    name: 'Theta/Beta 比值', abbreviation: 'Theta/Beta', purpose: '基于个体 Alpha 峰频率划定 Theta 与 Beta 后得到的功率比值。',
    steps: ['读取逻辑 Fz、Pz、Oz 通道的 EEG 数据。', '按离线分析契约完成预处理、质量门和 PSD 估计。', '估计个体 Alpha 峰频率（IAPF），用它确定个体化频段边界。', '积分计算 Theta 和 Beta 频段功率，并输出两者的比值。'],
    result: '结果是无单位比值（ratio）。它是科研指标，不能单独用于诊断或临床结论。',
  },
  'Official IAPF': {
    name: '个体 Alpha 峰频率', abbreviation: 'IAPF', purpose: '估计当前记录中个体化 Alpha 活动最集中的频率位置。',
    steps: ['计算 1–30 Hz 的功率谱密度（PSD）。', '拟合并扣除 1/f 背景成分，避免低频整体较强掩盖峰值。', '在 7–13 Hz 候选范围内寻找残差峰值或频率重心。', '通过质量门后输出 IAPF；不满足条件时结果保持不可用。'],
    result: '结果单位为 Hz，表示 Alpha 峰频率或其重心位置；它不是诊断结论。',
  },
  'Official BRAINBEAT': {
    name: '脑节律指数', abbreviation: 'BrainBeat', purpose: '结合 Fz 的 Theta 与 Pz 的 Alpha 相对功率形成的研究型节律指标。',
    steps: ['读取 Fz 和 Pz 的 EEG 数据并执行统一离线预处理。', '计算每个通道在 1–30 Hz 内的 PSD 与相对频段功率。', '提取 Fz 的 Theta 占比与 Pz 的 Alpha 占比。', '按官方定义组合两个相对功率，输出 BrainBeat 指数。'],
    result: '结果为无单位研究指标。需要结合实验设计、质量状态和其他结果解释，不能单独作临床判断。',
  },
  'Official FAA': {
    name: '额叶 Alpha 不对称性', abbreviation: 'FAA', purpose: '比较 F4 与 F3 通道 Alpha 频段功率的对数差。',
    steps: ['读取 F3、F4 的 EEG 数据并按离线分析契约完成预处理。', '计算两侧通道的 PSD，并积分得到 Alpha 频段绝对功率。', '对功率执行自然对数变换。', '计算 F4 与 F3 的对数功率差，保留通道映射和质量状态。'],
    result: '结果是对数功率差，属于无量纲指标。正负方向只代表该锁定公式中的左右差异，不应直接解释为临床状态。',
  },
  'Official RBP': {
    name: '相对频段功率', abbreviation: 'RBP', purpose: '展示 Delta、Theta、Alpha、Beta 在 1–30 Hz 总功率中的相对占比。',
    steps: ['按通道计算 1–30 Hz PSD，并应用质量门。', '对每个预定义频段的 PSD 做梯形积分，得到绝对频段功率。', '积分得到 1–30 Hz 总功率。', '每个频段功率除以总功率，输出相对频段功率。'],
    result: '结果是比例或百分比。四个互斥频段在允许频率离散化误差下合计接近 100%，不是诊断结果。',
  },
}

export function algorithmLabel(definition: AlgorithmDefinition | null | undefined): AlgorithmLabel {
  if (!definition) return { name: '', abbreviation: '', purpose: '', steps: [], result: '' }
  return OFFICIAL_LABELS[definition.name] ?? {
    name: definition.name,
    abbreviation: '',
    purpose: definition.description,
    steps: ['此私有算法的具体步骤请在“开发者详情”中查看其已保存定义。'],
    result: '结果单位、质量规则和可用性以已保存的算法版本为准。',
  }
}
