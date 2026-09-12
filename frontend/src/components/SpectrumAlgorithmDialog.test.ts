// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import SpectrumAlgorithmDialog from './SpectrumAlgorithmDialog.vue'

const result = { recording_id: 'r1', window_start_s: 0, window_duration_s: 30, sfreq_hz: 100, channels: ['Fp1'], analysis_reference: 'original', algorithm_version: 'offline-spectral-v3', units: { psd: 'uV^2/Hz', absolute_power: 'uV^2', relative_power: 'ratio' }, frequencies_hz: [1, 30], psd: { Fp1: [1, 2] }, band_power: { Fp1: { delta: 1, theta: 2, alpha: 3, beta: 4 } }, relative_band_power: { Fp1: { delta: .1, theta: .2, alpha: .3, beta: .4 } }, quality: { clean_segments: 14, total_segments: 14, clean_ratio: 1, gate_failed: null } }

it('shows backend algorithm contract and band values', () => {
  const wrapper = mount(SpectrumAlgorithmDialog, { props: { result, channel: 'Fp1' } })
  expect(wrapper.text()).toContain('offline-spectral-v3')
  expect(wrapper.text()).toContain('频段积分')
  expect(wrapper.text()).toContain('100.000%')
})
