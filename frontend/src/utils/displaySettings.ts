export type DisplaySettings = {
  timebaseSeconds: 5 | 10 | 15 | 30
  sensitivityUvPerMm: 2 | 3 | 5 | 7 | 10 | 15 | 20
  lowCutHz: number
  highCutHz: number
  notchHz: 50 | 60 | null
  baselineStabilization: boolean
  reference?: 'original' | 'average' | string
}

/** EEG 文件阅图的保守默认值：显示宽频，不默认叠加工频陷波。 */
export const DEFAULT_DISPLAY_SETTINGS: DisplaySettings = {
  timebaseSeconds: 10,
  sensitivityUvPerMm: 7,
  lowCutHz: 0.5,
  highCutHz: 70,
  notchHz: null,
  baselineStabilization: false,
}

export function isValidDisplaySettings(settings: Pick<DisplaySettings, 'lowCutHz' | 'highCutHz'>): boolean {
  return Number.isFinite(settings.lowCutHz)
    && Number.isFinite(settings.highCutHz)
    && settings.lowCutHz > 0
    && settings.lowCutHz < settings.highCutHz
}
