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

export type DynamicMetricPolicy = {
  minimumWindowS: number
  refreshStepS: number
  allowWarmup: boolean
}

export const DYNAMIC_WINDOW_OPTIONS = [5, 10, 20, 30] as const
export type DynamicWindowS = typeof DYNAMIC_WINDOW_OPTIONS[number]
const DEFAULT_DYNAMIC_METRIC_WINDOW_S: DynamicWindowS = 10
const DYNAMIC_METRIC_BOOTSTRAP_POINT_COUNT = 21
export const MIN_DYNAMIC_METRIC_WINDOW_S = 4
export const DEFAULT_DYNAMIC_METRIC_POLICY: DynamicMetricPolicy = {
  minimumWindowS: MIN_DYNAMIC_METRIC_WINDOW_S,
  refreshStepS: 1,
  allowWarmup: true,
}
export type DynamicMetricWindow = { startS: number; endS: number; warmup: boolean }

/**
 * Emits no point before one full Welch segment is available. Before the chosen
 * dynamic duration fills, the returned input is an explicitly marked warmup;
 * afterwards it is the fixed trailing analysis window.
 */
export function playbackMetricWindow(positionS: number, windowS: DynamicWindowS = DEFAULT_DYNAMIC_METRIC_WINDOW_S, policy: DynamicMetricPolicy = DEFAULT_DYNAMIC_METRIC_POLICY): DynamicMetricWindow | null {
  if (!Number.isFinite(positionS) || positionS < policy.minimumWindowS) return null
  if (positionS < windowS) return policy.allowWarmup ? { startS: 0, endS: positionS, warmup: true } : null
  return { startS: Math.max(0, positionS - windowS), endS: positionS, warmup: false }
}

/**
 * Requests a bounded set of real one-second dynamic windows when a user turns
 * on playback analysis in the middle of a recording. Before the selected
 * window fills it returns one marked warmup range; otherwise the backend
 * expands the bounded range into fixed trailing windows.
 */
export function dynamicMetricBootstrapRange(positionS: number, windowS: DynamicWindowS = DEFAULT_DYNAMIC_METRIC_WINDOW_S, policy: DynamicMetricPolicy = DEFAULT_DYNAMIC_METRIC_POLICY): DynamicMetricWindow | null {
  const warmup = playbackMetricWindow(positionS, windowS, policy)
  if (!warmup) return null
  if (warmup.warmup) return warmup
  const historyS = windowS + (DYNAMIC_METRIC_BOOTSTRAP_POINT_COUNT - 1) * policy.refreshStepS
  return { startS: Math.max(0, positionS - historyS), endS: positionS, warmup: false }
}

/**
 * Converts a gap in playback endpoints into one backend dynamic request. Its
 * first output is the second immediately after `previousEndS`, so no metric
 * time point is invented or silently skipped while a prior request runs.
 */
export function dynamicMetricCatchupRange(previousEndS: number, currentEndS: number, windowS: DynamicWindowS = DEFAULT_DYNAMIC_METRIC_WINDOW_S, refreshStepS = 1): { startS: number; endS: number } | null {
  if (!Number.isFinite(previousEndS) || !Number.isFinite(currentEndS) || currentEndS <= previousEndS) return null
  return {
    startS: Math.max(0, previousEndS + refreshStepS - windowS),
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

/**
 * A queued/running appended Run has no result yet. Retain the already rendered
 * backend history until a new backend payload arrives, then merge by endpoint.
 */
export function appendOrRetainDynamicMetric(
  previous: DynamicMetricPayload | null,
  next: DynamicMetricPayload | null,
): DynamicMetricPayload | null {
  if (previous === null) return next
  if (next === null) return previous
  return appendDynamicMetricPoint(previous, next)
}
