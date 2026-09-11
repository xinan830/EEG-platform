import { describe, expect, it } from 'vitest'
import { isRenderableWaveformValue, mapWaveformValueToY } from './waveformGeometry'

describe('mapWaveformValueToY', () => {
  it('按临床 µV/mm 灵敏度映射；数值越小，显示振幅越大', () => {
    expect(mapWaveformValueToY(0, 120, 90, 7)).toBe(120)
    expect(mapWaveformValueToY(7, 120, 90, 7)).toBeCloseTo(120 - 96 / 25.4)
    expect(mapWaveformValueToY(7, 120, 90, 5)).toBeLessThan(mapWaveformValueToY(7, 120, 90, 7))
  })

  it('不在单通道半轨内裁剪高幅波形', () => {
    expect(mapWaveformValueToY(500, 120, 90, 7)).toBeLessThan(0)
    expect(mapWaveformValueToY(-500, 120, 90, 7)).toBeGreaterThan(240)
  })

  it('扫屏缓冲中的 NaN 是空白扫描缺口，不能被画成零微伏连线', () => {
    expect(isRenderableWaveformValue(Number.NaN)).toBe(false)
    expect(isRenderableWaveformValue(0)).toBe(true)
  })

})
