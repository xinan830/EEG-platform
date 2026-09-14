import type { AlgorithmDefinition } from '../types/algorithmDefinition'

const OFFICIAL_LABELS: Record<string, { name: string; abbreviation: string; purpose: string }> = {
  'Official THETA_BETA': { name: 'Theta/Beta 比值', abbreviation: 'Theta/Beta', purpose: '基于个体 IAPF 的 Theta 与 Beta 功率比值' },
  'Official IAPF': { name: '个体 Alpha 峰频率', abbreviation: 'IAPF', purpose: 'Individual Alpha Peak Frequency' },
  'Official BRAINBEAT': { name: '脑节律指数', abbreviation: 'BrainBeat', purpose: '基于 Fz Theta 与 Pz Alpha 相对功率的指标' },
  'Official FAA': { name: '额叶 Alpha 不对称性', abbreviation: 'FAA', purpose: 'F4 与 F3 的 Alpha 对数功率差' },
  'Official RBP': { name: '相对频段功率', abbreviation: 'RBP', purpose: 'Delta、Theta、Alpha、Beta 在 1–30 Hz 内的相对占比' },
}

export function algorithmLabel(definition: AlgorithmDefinition | null | undefined): { name: string; abbreviation: string; purpose: string } {
  if (!definition) return { name: '', abbreviation: '', purpose: '' }
  return OFFICIAL_LABELS[definition.name] ?? { name: definition.name, abbreviation: '', purpose: definition.description }
}
