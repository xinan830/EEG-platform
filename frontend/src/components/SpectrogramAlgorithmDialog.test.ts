// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import SpectrogramAlgorithmDialog from './SpectrogramAlgorithmDialog.vue'
import AnalysisProvenancePanel from './AnalysisProvenancePanel.vue'

const result = {
  recording_id: 'r1', window_start_s: 0, window_duration_s: 4, sfreq_hz: 100,
  channels: ['Fz'], times_s: [2], frequencies_hz: [1, 30], power: { Fz: [[1, 2]] }, power_linear: { Fz: [[1, 2]] },
  units: 'uV^2/Hz', algorithm_version: 'spectrogram-v2', segment_s: 4, step_s: 1,
  quality: { windows: [{ center_s: 2, start_s: 0, end_s: 4, status: 'clean' as const, reason: null, peak_uv: 1 }], clean_windows: 1, total_windows: 1, bad_windows: 0 },
  analysis_provenance: {
    contract_version: 'analysis-provenance-v1' as const, status: 'completed', analysis_type: 'spectrogram', definition_version: null,
    scientific_algorithm_version: 'offline-spectral-v3', implementation_version: 'test-build', config_sha256: 'test-config', mode: 'spectrogram',
    requested_range: { start_s: 0, end_s: 4 }, actual_range: { start_s: 0, end_s: 4 }, channel: 'Fz', channel_mapping: { channels: ['Fz'] },
    analysis_reference: 'original', sfreq_hz: 100, filter: { bandpass_hz: [1, 30] }, welch: { segment_s: 4, window: 'hann', overlap_fraction: null, step_s: 1 },
    frequency: { low_hz: 1, high_hz: 30, point_count: 117 }, quality: { clean_windows: 1, total_windows: 1 }, extensions: [],
  },
}

it('uses shared provenance for configured spectrogram evidence', () => {
  const wrapper = mount(SpectrogramAlgorithmDialog, { props: { result, channel: 'Fz' } })
  expect(wrapper.findComponent(AnalysisProvenancePanel).exists()).toBe(true)
  expect(wrapper.text()).toContain('4 s Hann（1 s 步进）')
  expect(wrapper.text()).toContain('时频契约版本')
})
