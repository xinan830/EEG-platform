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

/** The real dynamic contract is a trailing 10-second window ending at playback time. */
export function playbackMetricWindow(positionS: number): { startS: number; endS: number } | null {
  if (!Number.isFinite(positionS) || positionS < 10) return null
  return { startS: Math.max(0, positionS - 10), endS: positionS }
}

/** Combine backend-returned points only; no metric or signal calculation happens here. */
export function appendDynamicMetricPoint(previous: DynamicMetricPayload | null, next: DynamicMetricPayload): DynamicMetricPayload {
  const byTime = new Map<number, DynamicMetricPoint>()
  for (const point of previous?.series ?? []) byTime.set(point.time_s, point)
  for (const point of next.series) byTime.set(point.time_s, point)
  return { ...next, series: [...byTime.values()].sort((left, right) => left.time_s - right.time_s) }
}
