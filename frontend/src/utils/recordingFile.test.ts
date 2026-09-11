import { describe, expect, it } from 'vitest'
import { isSupportedRecordingFile } from './recordingFile'

describe('isSupportedRecordingFile', () => {
  it('仅允许导入 BDF 或 EDF 文件，且不受扩展名大小写影响', () => {
    expect(isSupportedRecordingFile('meditation.BDF')).toBe(true)
    expect(isSupportedRecordingFile('session.edf')).toBe(true)
    expect(isSupportedRecordingFile('notes.csv')).toBe(false)
    expect(isSupportedRecordingFile('无扩展名')).toBe(false)
  })
})
