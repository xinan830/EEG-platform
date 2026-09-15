// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import AlgorithmMetricDebugDialog from './AlgorithmMetricDebugDialog.vue'

const dynamicRun = {
  run_id: 'run-22',
  status: 'completed',
  definition_version: '1.0.0',
  scientific_version: 'user-metric-dynamic-v1',
  implementation_version: 'build-abc',
  config_sha256: 'abc123',
  requested_range: { start_s: 12, end_s: 22 },
  actual_range: { start_s: 12, end_s: 22 },
  reference: { mode: 'original_recording_no_software_rereference' },
  result_summary: { metric: {
    mode: 'dynamic', channel: 'F3', output: { label: 'Theta/Beta 比值', unit: 'dimensionless' },
    dynamic_contract: { window_s: 10, step_s: 1 },
    series: [{ time_s: 22, window_start_s: 12, window_end_s: 22, value: 1.8,
      inputs: {
        input_left: { feature: 'theta_power', value: 9, unit: 'uV^2', channel: 'F3' },
        input_right: { feature: 'beta_power', value: 5, unit: 'uV^2', channel: 'F3' },
      },
      quality: { status: 'clean', reasons: [] },
      spectral_evidence: {
        sfreq_hz: 500, analysis_reference: 'original_recording_no_software_rereference', algorithm_version: 'offline-spectral-v3',
        welch_contract: { welch_segment_s: 4, welch_segment_overlap: 0.5, welch_window: 'hann' },
        filter_contract: { bandpass_hz: [1, 30] },
        frequencies_hz: Array.from({ length: 117 }, (_, index) => 1 + index * 0.25),
        psd_uV2_per_hz: Array.from({ length: 117 }, () => 0.5),
        band_power: { delta: 2, theta: 9, alpha: 3, beta: 5 },
        relative_band_power: { delta: 0.1, theta: 0.45, alpha: 0.15, beta: 0.25 },
        quality: { clean_segments: 4, total_segments: 4, clean_ratio: 1 },
      },
    }],
  } },
}

it('renders selected dynamic evidence without calculating an EEG value', () => {
  const wrapper = mount(AlgorithmMetricDebugDialog, { props: { run: dynamicRun, definitionName: 'Theta/Beta 比值' } as never })

  expect(wrapper.text()).toContain('实际分析区间')
  expect(wrapper.text()).toContain('分析范围结束时间')
  expect(wrapper.text()).toContain('频谱计算片段：4 s Hann')
  expect(wrapper.text()).toContain('12.000–22.000 s')
  expect(wrapper.text()).toContain('Theta 功率')
  expect(wrapper.text()).toContain('4 s Hann')
  expect(wrapper.text()).toContain('117 个频率点')
  expect(wrapper.text()).toContain('1.8')
})
