/**
 * Returns the selected dynamic duration as an absolute-time trend viewport.
 * The metric contract uses file time in seconds; this helper only controls
 * display bounds and never converts the underlying timestamps.
 */
export function metricTrendTimeAxis(timesS: number[], windowS = 10): { min: number; max: number } {
  const finiteTimes = timesS.filter((timeS) => Number.isFinite(timeS))
  if (!finiteTimes.length) return { min: 0, max: windowS }

  const lastTimeS = Math.max(...finiteTimes)
  return {
    min: Math.max(0, lastTimeS - windowS),
    max: lastTimeS,
  }
}

/**
 * Pads returned metric values before charting them, so a nearly-flat series
 * still has a small, readable set of distinct Y-axis ticks.
 */
export function metricTrendValueAxis(values: Array<number | null>): { min: number; max: number } {
  const finiteValues = values.filter((value): value is number => value !== null && Number.isFinite(value))
  if (!finiteValues.length) return { min: 0, max: 1 }

  const minimum = Math.min(...finiteValues)
  const maximum = Math.max(...finiteValues)
  const magnitude = Math.max(Math.abs(minimum), Math.abs(maximum), Number.EPSILON)
  const padding = Math.max((maximum - minimum) * 0.2, magnitude * 0.01)
  return { min: minimum - padding, max: maximum + padding }
}
