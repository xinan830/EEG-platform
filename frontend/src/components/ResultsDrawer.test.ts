// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'

const listRunSummaries = vi.hoisted(() => vi.fn())
const getResultView = vi.hoisted(() => vi.fn())

vi.mock('../api/results', () => ({
  exportUrl: vi.fn(() => 'http://example.test/export.zip'),
  getResultView,
  listRunSummaries,
}))

vi.mock('../api/spectralValidation', () => ({
  validateSpectralReference: vi.fn(),
}))

import ResultsDrawer from './ResultsDrawer.vue'

const metric = {
  output: { label: 'Theta/Beta 比值', value: 1.8, unit: 'dimensionless', quality: { status: 'clean', reasons: [] } },
  inputs: {
    theta: { feature: 'theta_power', value: 9, unit: 'uV^2', channel: 'F3' },
    beta: { feature: 'beta_power', value: 5, unit: 'uV^2', channel: 'F3' },
  },
  channel: 'F3',
  actual_range: { start_s: 10, end_s: 40 },
  source_quality: { clean_segments: 14, total_segments: 14 },
  chart: { kind: 'input_comparison', y_axis: { unit: 'uV^2' } },
}

beforeEach(() => {
  const run = {
    run_id: 'metric-run', recording_id: 'recording-1', analysis_type: 'definition_metric', status: 'completed',
    scientific_version: 'Theta/Beta@1.0.0', implementation_version: 'build-1', result_summary: { metric }, error: null,
  }
  listRunSummaries.mockReset().mockResolvedValue([run])
  getResultView.mockReset().mockResolvedValue({ run, artifacts: [], data_classification: {} })
})

it('renders the persisted user metric result in the results workbench', async () => {
  const wrapper = mount(ResultsDrawer, {
    props: { recordingId: 'recording-1', startS: 10, endS: 40, channels: ['F3'] },
  })
  await flushPromises()

  expect(wrapper.text()).toContain('Theta/Beta 比值')
  await wrapper.get('aside button:nth-child(2)').trigger('click')
  await flushPromises()

  expect(wrapper.text()).toContain('1.8')
  expect(wrapper.text()).toContain('输入功率对比')
  expect(wrapper.text()).toContain('10.000–40.000 s')
})
