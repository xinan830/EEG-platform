// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import SpectrumAlgorithmDialog from './SpectrumAlgorithmDialog.vue'
import AnalysisProvenancePanel from './AnalysisProvenancePanel.vue'

const result = { recording_id: 'r1', window_start_s: 0, window_duration_s: 30, sfreq_hz: 100, channels: ['Fp1'], analysis_reference: 'original', algorithm_version: 'offline-spectral-v4-configurable', units: { psd: 'uV^2/Hz', absolute_power: 'uV^2', relative_power: 'ratio' }, frequencies_hz: [1, 30], psd: { Fp1: [1, 2] }, band_power: { Fp1: { delta: 1, theta: 2, alpha: 3, beta: 4 } }, relative_band_power: { Fp1: { delta: .1, theta: .2, alpha: .3, beta: .4 } }, quality: { clean_segments: 14, total_segments: 14, clean_ratio: 1, gate_failed: null }, analysis_provenance: { contract_version: 'analysis-provenance-v1' as const, status: 'completed', analysis_type: 'spectrum', definition_version: null, scientific_algorithm_version: 'offline-spectral-v3', implementation_version: 'test-build', config_sha256: 'test-config', mode: 'static', requested_range: { start_s: 0, end_s: 30 }, actual_range: { start_s: 0, end_s: 30 }, channel: 'Fp1', channel_mapping: { channels: ['Fp1'] }, analysis_reference: 'original', sfreq_hz: 100, filter: { bandpass_hz: [1, 30] }, welch: { segment_s: 4, window: 'hann', overlap_fraction: .5, step_s: 2 }, frequency: { low_hz: 1, high_hz: 30, point_count: 117 }, quality: { clean_segments: 14, total_segments: 14 }, extensions: [] } }

it('shows backend algorithm contract and band values', () => {
  const wrapper = mount(SpectrumAlgorithmDialog, { props: { result, channel: 'Fp1' } })
  expect(wrapper.findComponent(AnalysisProvenancePanel).exists()).toBe(true)
  expect(wrapper.text()).toContain('offline-spectral-v3')
  expect(wrapper.text()).toContain('4 s Hann，50% overlap（2 s 步进）')
  expect(wrapper.text()).toContain('频段积分')
  expect(wrapper.text()).toContain('100.000%')
})
