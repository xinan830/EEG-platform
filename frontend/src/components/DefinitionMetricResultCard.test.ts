// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import DefinitionMetricResultCard from './DefinitionMetricResultCard.vue'

const result = {
  output: { label: 'Theta/Beta 比值', value: 1.8, unit: 'dimensionless', quality: { status: 'clean', reasons: [] } },
  inputs: {
    input_left: { feature: 'theta_power', value: 9, unit: 'uV^2', channel: 'F3' },
    input_right: { feature: 'beta_power', value: 5, unit: 'uV^2', channel: 'F3' },
  },
  channel: 'F3', actual_range: { start_s: 10, end_s: 40 }, source_quality: { clean_segments: 14, total_segments: 14 },
  chart: { kind: 'input_comparison', x_axis: { field: 'input_label' }, y_axis: { unit: 'uV^2' } },
}

describe('DefinitionMetricResultCard', () => {
  it('renders returned metric evidence without calculating a new EEG value', () => {
    const wrapper = mount(DefinitionMetricResultCard, { props: { result } })

    expect(wrapper.text()).toContain('Theta/Beta 比值')
    expect(wrapper.text()).toContain('1.8')
    expect(wrapper.text()).toContain('dimensionless')
    expect(wrapper.text()).toContain('F3')
    expect(wrapper.text()).toContain('10.000–40.000 s')
    expect(wrapper.get('[data-testid="metric-input-chart"]').text()).toContain('uV^2')
  })

  it('does not show an input chart when the backend marks it ineligible', () => {
    const wrapper = mount(DefinitionMetricResultCard, { props: { result: { ...result, chart: { ...result.chart, kind: 'none' } } } })
    expect(wrapper.find('[data-testid="metric-input-chart"]').exists()).toBe(false)
  })
})
