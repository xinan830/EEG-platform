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

  it('静态 PSD 在波形显示范围翻页时不改变已提交的分析范围', async () => {
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

    expect(wrapper.emitted('activeRangeChange')?.length ?? 0).toBe(before)
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

  it('动态时频图在播放位置回跳时清除上一轮的历史', async () => {
    getConfiguredSpectrogram.mockResolvedValue({
      recording_id: 'recording-1', window_start_s: 16, window_duration_s: 10,
      actual_start_s: 16, actual_end_s: 26, channels: ['F3'],
      times_s: [18, 19, 20, 21, 22, 23, 24], frequencies_hz: [1, 2],
      power: { F3: [[1, 1], [1, 1], [1, 1], [1, 1], [1, 1], [1, 1], [1, 1]] },
      power_linear: { F3: [[1, 1], [1, 1], [1, 1], [1, 1], [1, 1], [1, 1], [1, 1]] },
      power_db: { F3: [[1, 1], [1, 1], [1, 1], [1, 1], [1, 1], [1, 1], [1, 1]] },
      band_power_timeseries: { F3: { delta: [1, 1, 1, 1, 1, 1, 1], theta: [1, 1, 1, 1, 1, 1, 1], alpha: [1, 1, 1, 1, 1, 1, 1], beta: [1, 1, 1, 1, 1, 1, 1] } },
      units: 'dB re 1 uV^2/Hz', algorithm_version: 'offline-spectral-v4-configurable', segment_s: 4, step_s: 1,
    })
    const wrapper = mount(SpectrogramPanel, {
      props: { recordingId: 'recording-1', startS: 0, durationS: 100, channels: ['F3'], positionS: 26, playing: false },
      global: { stubs: { SpectrogramChart: true, BandPowerTrendChart: true, SpectrogramAlgorithmDialog: true } },
    })
    await vi.waitFor(() => expect(getConfiguredSpectrogram).toHaveBeenCalledTimes(1))
    await wrapper.find('select').setValue('dynamic')
    await vi.waitFor(() => expect(getConfiguredSpectrogram).toHaveBeenCalledTimes(2))

    await wrapper.setProps({ positionS: 0 })

    expect(wrapper.text()).toContain('暂无时频数据')
  })
})
