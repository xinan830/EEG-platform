import { ref } from 'vue'
import { DEFAULT_DISPLAY_SETTINGS, isValidDisplaySettings, type DisplaySettings } from '../utils/displaySettings'

type DisplayPreset = 'general' | 'original' | 'custom'
type DisplayChange = 'timebase' | 'sensitivity' | 'filter' | 'baseline' | 'reference' | 'preset'

interface DisplayControlActions {
  hasRecording: () => boolean
  hasActivePlayback: () => boolean
  restartPlayback: () => Promise<void>
  reloadFromStart: () => Promise<void>
  applyTimebase: () => Promise<void>
  rebuildEmptySweep: () => void
  showError: (message: string) => void
}

/** 阅图参数状态与“改变参数后从文件开头生效”的统一规则。 */
export function useDisplayControls(actions: DisplayControlActions) {
  const settings = ref<DisplaySettings>({ ...DEFAULT_DISPLAY_SETTINGS, reference: 'original' })
  const preset = ref<DisplayPreset>('general')

  async function reloadForCurrentSettings() {
    if (!isValidDisplaySettings(settings.value)) {
      actions.showError('低切必须小于高切')
      return
    }
    if (actions.hasActivePlayback()) await actions.restartPlayback()
    else if (actions.hasRecording()) await actions.reloadFromStart()
    else actions.rebuildEmptySweep()
  }

  function applyTimebase() {
    void actions.applyTimebase()
  }

  function applySensitivity() {
    void reloadForCurrentSettings()
  }

  function applyFilter(value?: number | string | null) {
    if (typeof value === 'number') {
      if (value === 50 || value === 60) settings.value.notchHz = value
      else if ([0.1, 0.3, 0.5, 1, 2].includes(value)) settings.value.lowCutHz = value
      else settings.value.highCutHz = value
    } else {
      settings.value.notchHz = null
    }
    preset.value = 'custom'
    void reloadForCurrentSettings()
  }

  function applyReference(value?: number | string | null) {
    settings.value.reference = String(value ?? 'original')
    void reloadForCurrentSettings()
  }

  function applyPreset(value?: number | string | null) {
    preset.value = value as DisplayPreset
    if (preset.value === 'general') {
      settings.value.lowCutHz = 0.5
      settings.value.highCutHz = 70
      settings.value.notchHz = null
    } else if (preset.value === 'original') {
      settings.value.lowCutHz = 1
      settings.value.highCutHz = 30
      settings.value.notchHz = 50
    }
    void reloadForCurrentSettings()
  }

  function change(kind: DisplayChange, value?: number | string | boolean | null) {
    if (kind === 'timebase') {
      settings.value.timebaseSeconds = value as 5 | 10 | 15 | 30
      applyTimebase()
    } else if (kind === 'sensitivity') {
      settings.value.sensitivityUvPerMm = value as 2 | 3 | 5 | 7 | 10 | 15 | 20
      applySensitivity()
    } else if (kind === 'filter') {
      applyFilter(value as number | string | null | undefined)
    } else if (kind === 'baseline') {
      settings.value.baselineStabilization = Boolean(value)
      preset.value = 'custom'
      void reloadForCurrentSettings()
    } else if (kind === 'reference') {
      applyReference(value as number | string | null | undefined)
    } else {
      applyPreset(value as number | string | null | undefined)
    }
  }

  function restoreDefaults() {
    settings.value = { ...DEFAULT_DISPLAY_SETTINGS, reference: 'original' }
    preset.value = 'general'
    void reloadForCurrentSettings()
  }

  return { settings, preset, change, restoreDefaults }
}
