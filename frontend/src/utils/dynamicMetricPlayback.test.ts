import { expect, it } from 'vitest'
import { appendDynamicMetricPoint, dynamicMetricBootstrapRange, dynamicMetricCatchupRange, playbackMetricWindow } from './dynamicMetricPlayback'

it('derives one trailing ten-second window from the playback head', () => {
  expect(playbackMetricWindow(9.999)).toBeNull()
  expect(playbackMetricWindow(10)).toEqual({ startS: 0, endS: 10 })
  expect(playbackMetricWindow(46.2)).toEqual({ startS: 36.2, endS: 46.2 })
})

it('derives trailing windows from the selected dynamic duration', () => {
  expect(playbackMetricWindow(19.999, 20)).toBeNull()
  expect(playbackMetricWindow(20, 20)).toEqual({ startS: 0, endS: 20 })
  expect(playbackMetricWindow(46, 20)).toEqual({ startS: 26, endS: 46 })
})

it('backfills a bounded real history when dynamic analysis starts mid-recording', () => {
  expect(dynamicMetricBootstrapRange(9)).toBeNull()
  expect(dynamicMetricBootstrapRange(25)).toEqual({ startS: 0, endS: 25 })
  expect(dynamicMetricBootstrapRange(442)).toEqual({ startS: 412, endS: 442 })
  expect(dynamicMetricBootstrapRange(442, 20)).toEqual({ startS: 402, endS: 442 })
})

it('requests every missed one-second endpoint after an asynchronous dynamic run', () => {
  expect(dynamicMetricCatchupRange(25, 26)).toEqual({ startS: 16, endS: 26 })
  expect(dynamicMetricCatchupRange(25, 29)).toEqual({ startS: 16, endS: 29 })
  expect(dynamicMetricCatchupRange(25, 29, 20)).toEqual({ startS: 6, endS: 29 })
  expect(dynamicMetricCatchupRange(25, 25)).toBeNull()
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
