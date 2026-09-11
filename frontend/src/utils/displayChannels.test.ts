import { describe, expect, it } from 'vitest'
import { chooseWaveformChannels } from './displayChannels'

describe('chooseWaveformChannels', () => {
  it('导入后优先展示常用脑电通道，并将 O2 作为 Oz 的回退', () => {
    expect(chooseWaveformChannels(['Fp1', 'Pz', 'O2', 'F3', 'F4', 'Fz'])).toEqual([
      'Fz', 'Pz', 'O2', 'F3', 'F4',
    ])
  })

  it('缺少常用通道时按文件原始顺序展示前五个', () => {
    expect(chooseWaveformChannels(['A', 'B', 'C', 'D', 'E', 'F'])).toEqual([
      'A', 'B', 'C', 'D', 'E',
    ])
  })
})
