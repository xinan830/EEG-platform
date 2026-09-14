import { expect, it } from 'vitest'
import { metricTrendTimeAxis, metricTrendValueAxis } from './metricTrendAxis'

it('keeps a live metric chart centred on its returned playback points', () => {
  expect(metricTrendTimeAxis([67, 68])).toEqual({ min: 66, max: 69 })
})

it('gives a single first dynamic point a readable local time span', () => {
  expect(metricTrendTimeAxis([10])).toEqual({ min: 9, max: 11 })
})

it('pads a nearly-flat metric series so the chart has readable tick spacing', () => {
  const axis = metricTrendValueAxis([2.2341, 2.235])
  expect(axis.min).toBeCloseTo(2.21175, 10)
  expect(axis.max).toBeCloseTo(2.25735, 10)
})
