import { expect, it } from 'vitest'
import { metricTrendTimeAxis } from './metricTrendAxis'

it('keeps a live metric chart centred on its returned playback points', () => {
  expect(metricTrendTimeAxis([67, 68])).toEqual({ min: 66, max: 69 })
})

it('gives a single first dynamic point a readable local time span', () => {
  expect(metricTrendTimeAxis([10])).toEqual({ min: 9, max: 11 })
})
