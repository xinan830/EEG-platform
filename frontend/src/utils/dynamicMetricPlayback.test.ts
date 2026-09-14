import { expect, it } from 'vitest'
import { appendDynamicMetricPoint, playbackMetricWindow } from './dynamicMetricPlayback'

it('derives one trailing ten-second window from the playback head', () => {
  expect(playbackMetricWindow(9.999)).toBeNull()
  expect(playbackMetricWindow(10)).toEqual({ startS: 0, endS: 10 })
  expect(playbackMetricWindow(46.2)).toEqual({ startS: 36.2, endS: 46.2 })
})

it('keeps one backend point per end time while preserving earlier dynamic results', () => {
  const first = { output: { label: 'Theta/Beta 比值', unit: 'dimensionless' }, channel: 'F3', series: [
    { time_s: 20, window_start_s: 10, window_end_s: 20, value: 1.25, quality: { status: 'clean' } },
  ] }
  const next = { output: { label: 'Theta/Beta 比值', unit: 'dimensionless' }, channel: 'F3', series: [
    { time_s: 21, window_start_s: 11, window_end_s: 21, value: 1.5, quality: { status: 'clean' } },
  ] }

  expect(appendDynamicMetricPoint(first, next).series).toEqual([
    { time_s: 20, window_start_s: 10, window_end_s: 20, value: 1.25, quality: { status: 'clean' } },
    { time_s: 21, window_start_s: 11, window_end_s: 21, value: 1.5, quality: { status: 'clean' } },
  ])
})

it('replaces the same playback second instead of drawing duplicate dynamic points', () => {
  const previous = { output: { label: 'Theta/Beta 比值', unit: 'dimensionless' }, channel: 'F3', series: [
    { time_s: 20, window_start_s: 10, window_end_s: 20, value: 1.25, quality: { status: 'clean' } },
  ] }
  const replacement = { output: { label: 'Theta/Beta 比值', unit: 'dimensionless' }, channel: 'F3', series: [
    { time_s: 20, window_start_s: 10, window_end_s: 20, value: 1.5, quality: { status: 'clean' } },
  ] }

  expect(appendDynamicMetricPoint(previous, replacement).series).toEqual([
    { time_s: 20, window_start_s: 10, window_end_s: 20, value: 1.5, quality: { status: 'clean' } },
  ])
})
