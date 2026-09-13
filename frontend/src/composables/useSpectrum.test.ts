import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import { nextTick, ref } from 'vue'
import { useSpectrum } from './useSpectrum'

vi.mock('../api/spectrum', () => ({
  getConfiguredSpectrum: vi.fn(async (_id: string, config: { time: { start_s: number; end_s: number } }) => ({
    recording_id: 'r1', window_start_s: config.time.start_s, window_duration_s: config.time.end_s - config.time.start_s, sfreq_hz: 100,
    channels: ['Fz'], analysis_reference: 'original', algorithm_version: 'offline-spectral-v3',
    units: { psd: 'uV^2/Hz', absolute_power: 'uV^2', relative_power: 'ratio' },
    frequencies_hz: [1, 2], psd: { Fz: [1, 1] },
    band_power: { Fz: { delta: 1, theta: 1, alpha: 1, beta: 1 } },
    relative_band_power: { Fz: { delta: .25, theta: .25, alpha: .25, beta: .25 } },
    quality: { clean_segments: 14, total_segments: 14, clean_ratio: 1, gate_failed: null },
  }))
}))

beforeEach(() => vi.clearAllMocks())
afterEach(() => vi.resetModules())

it('refreshes when the waveform screen origin changes', async () => {
  const recordingId = ref('r1'); const startS = ref(0); const channels = ref(['Fz'])
  const windowS = ref(30); const state = useSpectrum(recordingId, startS, windowS, channels)
  await nextTick(); await Promise.resolve()
  expect(state.result.value?.window_start_s).toBe(0)
  startS.value = 10
  await nextTick(); await Promise.resolve()
  expect(state.result.value?.window_start_s).toBe(10)
})

it('uses the requested analysis window', async () => {
  const recordingId = ref('r1'); const startS = ref(4); const windowS = ref(10); const channels = ref(['Fz'])
  const state = useSpectrum(recordingId, startS, windowS, channels)
  await nextTick(); await Promise.resolve()
  expect(state.result.value?.window_duration_s).toBe(10)
})
