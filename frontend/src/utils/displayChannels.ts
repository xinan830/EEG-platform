const PREFERRED_CHANNELS = [
  ['FZ'],
  ['PZ'],
  ['OZ', 'O2', 'O1'],
  ['F3'],
  ['F4'],
]

export function chooseWaveformChannels(available: string[], limit = 5): string[] {
  const selected: string[] = []
  const normalized = new Map(available.map((name) => [name.toUpperCase(), name]))

  for (const candidates of PREFERRED_CHANNELS) {
    const match = candidates.map((candidate) => normalized.get(candidate)).find(Boolean)
    if (match && !selected.includes(match)) selected.push(match)
    if (selected.length === limit) return selected
  }

  for (const name of available) {
    if (!selected.includes(name)) selected.push(name)
    if (selected.length === limit) break
  }
  return selected
}
