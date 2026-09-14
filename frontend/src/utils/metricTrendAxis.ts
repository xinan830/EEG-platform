/**
 * Returns a compact absolute-time viewport for dynamic metric points.
 * The metric contract uses file time in seconds; this helper only controls
 * display bounds and never converts the underlying timestamps.
 */
export function metricTrendTimeAxis(timesS: number[]): { min: number; max: number } {
  const finiteTimes = timesS.filter((timeS) => Number.isFinite(timeS))
  if (!finiteTimes.length) return { min: 0, max: 1 }

  const firstTimeS = Math.min(...finiteTimes)
  const lastTimeS = Math.max(...finiteTimes)
  return {
    min: Math.max(0, firstTimeS - 1),
    max: lastTimeS + 1,
  }
}
