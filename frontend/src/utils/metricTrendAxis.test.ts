import { expect, it } from 'vitest'
import { metricTrendTimeAxis, metricTrendValueAxis } from './metricTrendAxis'

it('uses the selected dynamic duration as the trend time viewport', () => {
  expect(metricTrendTimeAxis([30, 31, 32], 10)).toEqual({ min: 22, max: 32 })
  expect(metricTrendTimeAxis([30, 31, 32], 20)).toEqual({ min: 12, max: 32 })
})

it('uses the available recording beginning as the first trend viewport boundary', () => {
  expect(metricTrendTimeAxis([10], 10)).toEqual({ min: 0, max: 10 })
})

it('pads a nearly-flat metric series so the chart has readable tick spacing', () => {
  const axis = metricTrendValueAxis([2.2341, 2.235])
  expect(axis.min).toBeCloseTo(2.21175, 10)
  expect(axis.max).toBeCloseTo(2.25735, 10)
})
