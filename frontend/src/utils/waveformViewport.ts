export interface Viewport {
  start: number
  duration: number
}

interface ZoomViewportInput extends Viewport {
  total: number
  anchorRatio: number
  deltaY: number
}

interface MoveViewportInput extends Viewport {
  total: number
  deltaX: number
  width: number
}

const MIN_VIEW_DURATION_S = 2

export function clampViewportStart(start: number, duration: number, total: number): number {
  return Math.max(0, Math.min(Math.max(0, total - duration), start))
}

export function zoomViewport({ start, duration, total, anchorRatio, deltaY }: ZoomViewportInput): Viewport {
  const safeTotal = Math.max(MIN_VIEW_DURATION_S, total)
  const safeRatio = Math.max(0, Math.min(1, anchorRatio))
  const nextDuration = Math.max(
    MIN_VIEW_DURATION_S,
    Math.min(safeTotal, duration * (deltaY > 0 ? 1.2 : 0.82)),
  )
  const anchorTime = start + duration * safeRatio
  return {
    duration: nextDuration,
    start: clampViewportStart(anchorTime - nextDuration * safeRatio, nextDuration, safeTotal),
  }
}

export function moveViewportByPixels({ start, duration, total, deltaX, width }: MoveViewportInput): Viewport {
  const nextStart = start - deltaX / Math.max(1, width) * duration
  return {
    duration,
    start: clampViewportStart(nextStart, duration, total),
  }
}
