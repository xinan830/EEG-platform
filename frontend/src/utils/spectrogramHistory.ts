import type { SpectrogramResponse } from '../types/spectrogram'

type SourceColumn = { response: SpectrogramResponse; index: number }

function centerKey(center: number): string { return center.toFixed(9) }

function sameAxis(left: SpectrogramResponse, right: SpectrogramResponse): boolean {
  return left.channels.join('\u0000') === right.channels.join('\u0000')
    && left.frequencies_hz.length === right.frequencies_hz.length
    && left.frequencies_hz.every((value, index) => Math.abs(value - right.frequencies_hz[index]) < 1e-9)
}

function selectColumns(response: SpectrogramResponse, columns: SourceColumn[]): SpectrogramResponse {
  const matrix = (field: 'power' | 'power_linear' | 'power_db') => Object.fromEntries(response.channels.map((channel) => [channel, columns.map(({ response: item, index }) => item[field]?.[channel]?.[index] ?? [])]))
  const bandPower = response.band_power_timeseries && Object.fromEntries(response.channels.map((channel) => [channel, {
    delta: columns.map(({ response: item, index }) => item.band_power_timeseries?.[channel]?.delta[index] ?? Number.NaN),
    theta: columns.map(({ response: item, index }) => item.band_power_timeseries?.[channel]?.theta[index] ?? Number.NaN),
    alpha: columns.map(({ response: item, index }) => item.band_power_timeseries?.[channel]?.alpha[index] ?? Number.NaN),
    beta: columns.map(({ response: item, index }) => item.band_power_timeseries?.[channel]?.beta[index] ?? Number.NaN),
  }]))
  const qualityWindows = columns.map(({ response: item, index }) => item.quality?.windows[index]).filter((item): item is NonNullable<SpectrogramResponse['quality']>['windows'][number] => Boolean(item))
  const customBand = response.custom_band_power_timeseries && Object.fromEntries(response.channels.map((channel) => [channel, columns.map(({ response: item, index }) => item.custom_band_power_timeseries?.[channel]?.[index] ?? Number.NaN)]))
  return {
    ...response,
    times_s: columns.map(({ response: item, index }) => item.times_s[index]),
    power: matrix('power'),
    power_linear: matrix('power_linear'),
    power_db: matrix('power_db'),
    band_power_timeseries: bandPower,
    custom_band_power_timeseries: customBand,
    quality: response.quality ? {
      windows: qualityWindows,
      clean_windows: qualityWindows.filter((item) => item.status === 'clean').length,
      total_windows: qualityWindows.length,
      bad_windows: qualityWindows.filter((item) => item.status === 'bad').length,
    } : undefined,
    time_bins: columns.length,
    frequency_bins: response.frequencies_hz.length,
    matrix_shape: [columns.length, response.frequencies_hz.length],
    first_center_s: columns[0] ? columns[0].response.times_s[columns[0].index] : null,
    last_center_s: columns.at(-1) ? columns.at(-1)!.response.times_s[columns.at(-1)!.index] : null,
  }
}

/** Merge only backend-returned time columns; no EEG or PSD calculation occurs here. */
export function mergeSpectrogramHistory(previous: SpectrogramResponse | null, incoming: SpectrogramResponse, maxHistoryS: number): SpectrogramResponse {
  if (!previous || !sameAxis(previous, incoming)) return visibleSpectrogramHistory(incoming, maxHistoryS)
  const byCenter = new Map<string, SourceColumn>()
  previous.times_s.forEach((center, index) => byCenter.set(centerKey(center), { response: previous, index }))
  incoming.times_s.forEach((center, index) => byCenter.set(centerKey(center), { response: incoming, index }))
  const columns = [...byCenter.values()].sort((left, right) => left.response.times_s[left.index] - right.response.times_s[right.index])
  const latest = columns.at(-1)
  const cutoff = latest ? latest.response.times_s[latest.index] - maxHistoryS : Number.NEGATIVE_INFINITY
  return selectColumns(incoming, columns.filter(({ response, index }) => response.times_s[index] >= cutoff))
}

/** Apply the user's chart viewport without modifying the retained backend history. */
export function visibleSpectrogramHistory(history: SpectrogramResponse, displayRangeS: number): SpectrogramResponse {
  const latest = history.times_s.at(-1)
  if (latest === undefined) return history
  const cutoff = latest - displayRangeS
  const columns = history.times_s.map((center, index) => ({ response: history, index })).filter(({ index }) => history.times_s[index] >= cutoff)
  return selectColumns(history, columns)
}
