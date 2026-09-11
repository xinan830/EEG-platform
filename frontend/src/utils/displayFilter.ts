import type { DisplaySettings } from './displaySettings'

export function playbackFilterPayload(settings: DisplaySettings) {
  return {
    low_cut_hz: settings.lowCutHz,
    high_cut_hz: settings.highCutHz,
    notch_hz: settings.notchHz,
    baseline_stabilization: settings.baselineStabilization,
  }
}
