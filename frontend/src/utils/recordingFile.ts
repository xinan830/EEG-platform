const SUPPORTED_RECORDING_EXTENSIONS = new Set(['bdf', 'edf'])

export function isSupportedRecordingFile(fileName: string): boolean {
  const extension = fileName.split('.').at(-1)?.toLowerCase()
  return extension !== undefined && SUPPORTED_RECORDING_EXTENSIONS.has(extension)
}
