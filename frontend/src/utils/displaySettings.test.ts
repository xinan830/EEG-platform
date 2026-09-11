import { describe, expect, it } from 'vitest'
import { DEFAULT_DISPLAY_SETTINGS, isValidDisplaySettings } from './displaySettings'

describe('EEG 阅图显示设置', () => {
  it('默认使用宽频阅图，而不是生物反馈的 1–30Hz 窄带设置', () => {
    expect(DEFAULT_DISPLAY_SETTINGS).toEqual({
      timebaseSeconds: 10,
      sensitivityUvPerMm: 7,
      lowCutHz: 0.5,
      highCutHz: 70,
      notchHz: null,
      baselineStabilization: false,
    })
  })

  it('只接受低切小于高切的显示滤波参数', () => {
    expect(isValidDisplaySettings({ ...DEFAULT_DISPLAY_SETTINGS, lowCutHz: 1, highCutHz: 30 })).toBe(true)
    expect(isValidDisplaySettings({ ...DEFAULT_DISPLAY_SETTINGS, lowCutHz: 70, highCutHz: 0.5 })).toBe(false)
  })
})
