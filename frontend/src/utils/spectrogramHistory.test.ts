import { describe, expect, it } from 'vitest'
import type { SpectrogramResponse } from '../types/spectrogram'
import { mergeSpectrogramHistory, shouldResetSpectrogramHistoryForPlaybackJump, visibleSpectrogramHistory } from './spectrogramHistory'

function response(times: number[], offset = 0): SpectrogramResponse {
  return {
    recording_id: 'recording-1', window_start_s: 0, window_duration_s: 10, channels: ['F3'],
    times_s: times, frequencies_hz: [1, 2], power: { F3: times.map((time) => [time + offset, time + offset + 0.5]) },
    power_linear: { F3: times.map((time) => [time + offset + 10, time + offset + 10.5]) },
    power_db: { F3: times.map((time) => [time + offset, time + offset + 0.5]) },
    band_power_timeseries: { F3: { delta: times.map((time) => time + offset), theta: times.map(() => 2), alpha: times.map(() => 3), beta: times.map(() => 4) } },
    units: 'dB re 1 uV^2/Hz', algorithm_version: 'offline-spectral-v4-configurable', segment_s: 4, step_s: 1,
    quality: { windows: times.map((center, index) => ({ center_s: center, start_s: center - 2, end_s: center + 2, status: index === 0 ? 'bad' : 'clean', reason: index === 0 ? 'flatline' : null, peak_uv: null })), clean_windows: Math.max(0, times.length - 1), total_windows: times.length, bad_windows: times.length ? 1 : 0 },
  }
}

describe('spectrogram history', () => {
  it('starts a new dynamic history when playback jumps backwards', () => {
    expect(shouldResetSpectrogramHistoryForPlaybackJump(null, 26.6)).toBe(false)
    expect(shouldResetSpectrogramHistoryForPlaybackJump(26.6, 27.1)).toBe(false)
    expect(shouldResetSpectrogramHistoryForPlaybackJump(26.6, 0)).toBe(true)
    expect(shouldResetSpectrogramHistoryForPlaybackJump(26.6, 12)).toBe(true)
  })

  it('merges overlapping backend columns by center and keeps the newer returned column', () => {
    const history = mergeSpectrogramHistory(response([2, 3, 4]), response([3, 4, 5], 100), 60)

    expect(history.times_s).toEqual([2, 3, 4, 5])
    expect(history.power.F3).toEqual([[2, 2.5], [103, 103.5], [104, 104.5], [105, 105.5]])
    expect(history.quality?.windows.map((item) => item.center_s)).toEqual([2, 3, 4, 5])
  })

  it('changes only the visible result time range and preserves backend quality columns', () => {
    const history = mergeSpectrogramHistory(response([2, 3, 4]), response([3, 4, 5]), 60)
    const visible = visibleSpectrogramHistory(history, 2)

    expect(visible.times_s).toEqual([3, 4, 5])
    expect(visible.matrix_shape).toEqual([3, 2])
    expect(visible.quality?.windows[0]).toMatchObject({ center_s: 3, status: 'bad', reason: 'flatline' })
  })
})
