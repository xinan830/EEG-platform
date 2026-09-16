export interface Recording {
  id: string
  original_name: string
  stored_name: string
  extension: '.bdf' | '.edf'
  created_at: string
  sfreq: number | null
  duration_s: number | null
  channels: string[]
}
