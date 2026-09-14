export type DynamicMetricPoint = {
  time_s: number
  window_start_s: number
  window_end_s: number
  value: number | null
  quality?: { status?: string; reasons?: string[] }
}

export type DynamicMetricPayload = {
  output: { label: string; unit: string }
  channel: string
  series: DynamicMetricPoint[]
  chart?: { y_axis?: { label?: string; unit?: string } }
}

export const DYNAMIC_WINDOW_OPTIONS = [5, 10, 20, 30] as const
export type DynamicWindowS = typeof DYNAMIC_WINDOW_OPTIONS[number]
const DEFAULT_DYNAMIC_METRIC_WINDOW_S: DynamicWindowS = 10
const DYNAMIC_METRIC_BOOTSTRAP_POINT_COUNT = 21

/** The real dynamic contract is a trailing 10-second window ending at playback time. */
export function playbackMetricWindow(positionS: number, windowS: DynamicWindowS = DEFAULT_DYNAMIC_METRIC_WINDOW_S): { startS: number; endS: number } | null {
  if (!Number.isFinite(positionS) || positionS < windowS) return null
  return { startS: Math.max(0, positionS - windowS), endS: positionS }
}

/**
 * Requests a bounded set of real one-second dynamic windows when a user turns
 * on playback analysis in the middle of a recording. The returned range is
 * still interpreted by the backend as 10-second trailing windows.
 */
export function dynamicMetricBootstrapRange(positionS: number, windowS: DynamicWindowS = DEFAULT_DYNAMIC_METRIC_WINDOW_S): { startS: number; endS: number } | null {
  if (!Number.isFinite(positionS) || positionS < windowS) return null
  const historyS = windowS + DYNAMIC_METRIC_BOOTSTRAP_POINT_COUNT - 1
  return { startS: Math.max(0, positionS - historyS), endS: positionS }
}

/**
 * Converts a gap in playback endpoints into one backend dynamic request. Its
 * first output is the second immediately after `previousEndS`, so no metric
 * time point is invented or silently skipped while a prior request runs.
 */
export function dynamicMetricCatchupRange(previousEndS: number, currentEndS: number, windowS: DynamicWindowS = DEFAULT_DYNAMIC_METRIC_WINDOW_S): { startS: number; endS: number } | null {
  if (!Number.isFinite(previousEndS) || !Number.isFinite(currentEndS) || currentEndS <= previousEndS) return null
  return {
    startS: Math.max(0, previousEndS + 1 - windowS),
    endS: currentEndS,
  }
}

/** Combine backend-returned points only; no metric or signal calculation happens here. */
export function appendDynamicMetricPoint(previous: DynamicMetricPayload | null, next: DynamicMetricPayload): DynamicMetricPayload {
  const byTime = new Map<number, DynamicMetricPoint>()
  for (const point of previous?.series ?? []) byTime.set(point.time_s, point)
  for (const point of next.series) byTime.set(point.time_s, point)
  return { ...next, series: [...byTime.values()].sort((left, right) => left.time_s - right.time_s) }
}
