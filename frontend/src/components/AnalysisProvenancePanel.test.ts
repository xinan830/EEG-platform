// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import AnalysisProvenancePanel from './AnalysisProvenancePanel.vue'

const provenance = {
  contract_version: 'analysis-provenance-v1' as const,
  status: 'completed', analysis_type: 'definition_metric', definition_version: '1.0.0',
  scientific_algorithm_version: 'offline-spectral-v3', implementation_version: 'build-1', config_sha256: 'ABC123',
  mode: 'dynamic', requested_range: { start_s: 0, end_s: 30 }, actual_range: { start_s: 5, end_s: 15 },
  channel: 'F3', channel_mapping: { channels: ['F3'] }, analysis_reference: 'original_recording_no_software_rereference',
  sfreq_hz: 500, filter: { bandpass_hz: [1, 30], preprocessing_phase: 'zero_phase' },
  welch: { segment_s: 4, window: 'hann', overlap_fraction: 0.5, step_s: 2 },
  frequency: { low_hz: 1, high_hz: 30, point_count: 117 },
  quality: { clean_segments: 4, total_segments: 4, clean_ratio: 1, gate_failed: false }, extensions: [],
}

it('renders backend-provided Welch step and immutable base evidence', () => {
  const wrapper = mount(AnalysisProvenancePanel, { props: { provenance } })

  expect(wrapper.text()).toContain('配置指纹')
  expect(wrapper.text()).toContain('实际分析区间')
  expect(wrapper.text()).toContain('5.000–15.000 s')
  expect(wrapper.text()).toContain('4 s Hann，50% overlap（2 s 步进）')
  expect(wrapper.text()).toContain('1–30 Hz（117 个频率点）')
  expect(wrapper.text()).toContain('original_recording_no_software_rereference')
})

it('renders missing backend evidence as a dash without inferring values', () => {
  const wrapper = mount(AnalysisProvenancePanel, { props: { provenance: { ...provenance, welch: null, quality: null } } })

  expect(wrapper.text()).toContain('Welch')
  expect(wrapper.text()).toContain('—')
  expect(wrapper.text()).not.toContain('（2 s 步进）')
})
