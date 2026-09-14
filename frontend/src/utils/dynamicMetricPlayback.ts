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

const DYNAMIC_METRIC_WINDOW_S = 10
const DYNAMIC_METRIC_BOOTSTRAP_HISTORY_S = 30

/** The real dynamic contract is a trailing 10-second window ending at playback time. */
export function playbackMetricWindow(positionS: number): { startS: number; endS: number } | null {
  if (!Number.isFinite(positionS) || positionS < DYNAMIC_METRIC_WINDOW_S) return null
  return { startS: Math.max(0, positionS - DYNAMIC_METRIC_WINDOW_S), endS: positionS }
}

/**
 * Requests a bounded set of real one-second dynamic windows when a user turns
 * on playback analysis in the middle of a recording. The returned range is
 * still interpreted by the backend as 10-second trailing windows.
 */
export function dynamicMetricBootstrapRange(positionS: number): { startS: number; endS: number } | null {
  if (!Number.isFinite(positionS) || positionS < DYNAMIC_METRIC_WINDOW_S) return null
  return { startS: Math.max(0, positionS - DYNAMIC_METRIC_BOOTSTRAP_HISTORY_S), endS: positionS }
}

/**
 * Converts a gap in playback endpoints into one backend dynamic request. Its
 * first output is the second immediately after `previousEndS`, so no metric
 * time point is invented or silently skipped while a prior request runs.
 */
export function dynamicMetricCatchupRange(previousEndS: number, currentEndS: number): { startS: number; endS: number } | null {
  if (!Number.isFinite(previousEndS) || !Number.isFinite(currentEndS) || currentEndS <= previousEndS) return null
  return {
    startS: Math.max(0, previousEndS + 1 - DYNAMIC_METRIC_WINDOW_S),
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
