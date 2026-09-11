export interface ChannelMapping {
  fz: string
  pz: string
  oz: string
  f3: string | null
  f4: string | null
}

export interface Recording {
  id: string
  original_name: string
  stored_name: string
  extension: '.bdf' | '.edf'
  created_at: string
  sfreq: number | null
  duration_s: number | null
  channels: string[]
  mapping: ChannelMapping | null
}
