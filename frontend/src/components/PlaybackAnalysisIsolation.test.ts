// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import SpectrumPanel from './SpectrumPanel.vue'
import SpectrogramPanel from './SpectrogramPanel.vue'

const getConfiguredSpectrogram = vi.fn()

vi.mock('../api/spectrogram', () => ({
  getConfiguredSpectrogram: (...args: unknown[]) => getConfiguredSpectrogram(...args),
}))

vi.mock('../composables/useSpectrum', () => ({
  useSpectrum: () => ({ result: ref(null), loading: ref(false), error: ref(''), reload: vi.fn() }),
}))

describe('playback analysis isolation', () => {
  beforeEach(() => {
    getConfiguredSpectrogram.mockReset()
    getConfiguredSpectrogram.mockImplementation(() => new Promise(() => {}))
  })

  it('静态 PSD 同页不更新，翻页时只提交一次新区间', async () => {
    const wrapper = mount(SpectrumPanel, {
      props: {
        recordingId: 'recording-1', startS: 0, channels: ['F3'], positionS: 5,
        playing: true, totalDurationS: 100, screenDurationS: 10,
      },
      global: { stubs: { SpectrumPsdChart: true, BandPowerChart: true, SpectrumAlgorithmDialog: true } },
    })
    const before = wrapper.emitted('activeRangeChange')?.length ?? 0

    await wrapper.setProps({ startS: 0.05, positionS: 5.05 })

    expect(wrapper.emitted('activeRangeChange')?.length ?? 0).toBe(before)

    await wrapper.setProps({ startS: 10, positionS: 10.05 })

    expect(wrapper.emitted('activeRangeChange')?.length ?? 0).toBe(before + 1)
  })

  it('静态时频图不跟随每个播放数据包重新请求', async () => {
    const wrapper = mount(SpectrogramPanel, {
      props: {
        recordingId: 'recording-1', startS: 0, durationS: 100, channels: ['F3'],
        positionS: 5, playing: true, activeRange: { start: 0, end: 30, source: 'current_30s' },
      },
      global: { stubs: { SpectrogramChart: true, BandPowerTrendChart: true, SpectrogramAlgorithmDialog: true } },
    })
    await vi.waitFor(() => expect(getConfiguredSpectrogram).toHaveBeenCalledTimes(1))

    await wrapper.setProps({ startS: 0.05, positionS: 5.05 })

    expect(getConfiguredSpectrogram).toHaveBeenCalledTimes(1)
  })
})
