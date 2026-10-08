## ADDED Requirements

### Requirement: Describe actual preprocessing independently of analysis bands
An algorithm execution snapshot SHALL record the preprocessing actually
performed. Search or integration band fields SHALL NOT be inferred to be
filter settings. Explicit notch settings SHALL override default policies.

#### Scenario: Record a peak search
- **WHEN** peak frequency searches 8-13 Hz over its frozen 1-30 Hz signal
- **THEN** the snapshot records filter 1-30 Hz and search band 8-13 Hz separately.

#### Scenario: Record an explicit notch selection
- **WHEN** PSD or STFT executes with notch disabled or 60 Hz
- **THEN** its execution snapshot records null or 60 Hz respectively, consistent with actual spectral evidence.
