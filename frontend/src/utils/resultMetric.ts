import type { RunSummary } from '../api/results'
import type { DefinitionMetricResult } from '../components/DefinitionMetricResultCard.vue'
import type { DynamicMetric } from '../components/DefinitionMetricTrendChart.vue'

type ResultRecord = Record<string, unknown>

function isRecord(value: unknown): value is ResultRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** Read the persisted backend metric payload; this deliberately performs no EEG calculation. */
export function definitionMetricFromRun(run: RunSummary | null | undefined): DefinitionMetricResult | DynamicMetric | null {
  if (run?.analysis_type !== 'definition_metric' || !isRecord(run.result_summary)) return null
  const metric = run.result_summary.metric
  if (!isRecord(metric) || !isRecord(metric.output) || typeof metric.output.label !== 'string') return null
  return metric as unknown as DefinitionMetricResult | DynamicMetric
}

export function isDynamicMetricResult(
  metric: DefinitionMetricResult | DynamicMetric | null,
): metric is DynamicMetric {
  return Array.isArray((metric as DynamicMetric | null)?.series)
}

export function resultRunLabel(run: RunSummary): string {
  const metric = definitionMetricFromRun(run)
  if (metric) return metric.output.label
  const labels: Record<string, string> = {
    spectrum: '频谱分析',
    spectrogram: '时频分析',
    legacy_analysis: '兼容分析结果',
  }
  return labels[run.analysis_type] ?? run.analysis_type
}
