import type { RunSummary } from '../api/results'

export function resultRunLabel(run: RunSummary): string {
  const labels: Record<string, string> = {
    spectrum: '频谱分析',
    spectrogram: '时频分析',
    official_algorithm: '官方算法',
  }
  return labels[run.analysis_type] ?? run.analysis_type
}
