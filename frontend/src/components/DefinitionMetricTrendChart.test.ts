// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'

const chartSetOption = vi.hoisted(() => vi.fn())

vi.mock('echarts/core', () => ({
  init: vi.fn(() => ({ setOption: chartSetOption, resize: vi.fn(), dispose: vi.fn() })),
  use: vi.fn(),
}))

import DefinitionMetricTrendChart from './DefinitionMetricTrendChart.vue'

class ResizeObserverStub {
  observe() {}
  disconnect() {}
}

beforeEach(() => {
  chartSetOption.mockClear()
  vi.stubGlobal('ResizeObserver', ResizeObserverStub)
})

it('shows an empty first-window chart without fabricating a metric value', async () => {
  const wrapper = mount(DefinitionMetricTrendChart, {
    props: {
      result: null,
      pending: { label: 'Theta/Beta 比值', unit: 'dimensionless', channel: 'F3', windowS: 10 },
    } as never,
  })

  expect(wrapper.text()).toContain('等待第一个完整分析范围：0.000–10.000 s')
  expect(wrapper.text()).toContain('当前：等待完整分析范围')
  expect(chartSetOption).toHaveBeenCalledWith(expect.objectContaining({
    series: [expect.objectContaining({ data: [] })],
    xAxis: expect.objectContaining({ min: 0, max: 10 }),
  }), true)
})
