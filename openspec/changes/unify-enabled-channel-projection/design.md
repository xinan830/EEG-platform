# Design

## Context

Desktop recordings already carry an immutable acquisition channel snapshot, but raw review, impedance, and live analysis do not consistently project it. EDF/BDF imports retain source-order metadata in the backend and currently have no configuration binding. Sample storage is sample-major and V; channel metadata indexes the corresponding source columns.

## Goals / Non-Goals

**Goals:** use one desktop projection rule for enabled EEG display channels; retain exact source indices; persist a matched import-time configuration; keep source-file data and algorithm channel contracts intact.

**Non-Goals:** alter filtering, montage formulas, scientific algorithm execution, acquisition bytes, or file channel order; infer aliases or reorder backend analysis inputs.

## Decisions

- Place the projection helper in the Channels domain and make it consume an immutable `ChannelConfigurationProfile` plus source channel metadata. Return source stream indexes with display labels/order; callers use these indexes to read data rather than rewriting manifests.
- Apply the recorded snapshot for WPF recordings and an optional import snapshot for EDF/BDF display. If the snapshot is absent, malformed, or cannot uniquely bind the source EEG channel identities, keep the source channel view.
- ANT/eego impedance requests contain selected physical native indexes, while the SDK's impedance stream may report those physical `Reference` channels with compact stream-local indexes (observed request `0,1,2,3,19,21`, returned stream indexes `0–5`). Map the ordered SDK `Reference` entries back to the ordered request identities; keep `StreamIndex` for values and use the requested native index for labels/positions. Require equal requested/returned physical channel counts and fail on ambiguity rather than displaying a value on the wrong electrode. SDK index uniqueness is checked per `(index, type)` because impedance reference/ground roles may reuse an index.
- The checked-in ANT wrapper preserves the caller's channel array when opening impedance and exposes the SDK stream channel list in SDK order. Its public header does not guarantee that each returned physical-reference value corresponds positionally to the request array; count and identity mapping are validated in code, but that final value-to-electrode guarantee remains a hardware/SDK integration check.
- Match imported EEG channels by trimmed, case-insensitive exact labels, one-to-one. Desktop supplies the most recently applied profile snapshot with the multipart import request; the backend already reads the EDF/BDF channel metadata, so it performs the exact matching and creates the ordered source-column/native-index mapping. This keeps file parsing and the matching decision at one boundary.
- For a platform-exported EDF/BDF, desktop additionally sends only the channel-identity subset of its adjacent `.labels.json` sidecar. The exporter records the EDF/BDF SHA-256 in that sidecar; the importer accepts its mapping only when the digest, ordered exported labels, channel count, and referenced configuration entries match the uploaded file. The sidecar's original source label and native index allow display labels to be restored without changing source column names or sample order. Invalid supplied sidecar metadata is an import error, not a silent fallback.
- For an ordinary EDF/BDF without a verified export manifest, keep the existing all-or-nothing exact label match. A partial or ambiguous match must not silently hide only some source channels.
- The common projection validates native index, kind, and source label. Live waveform presentation must use it rather than maintaining a second native-index-only selection implementation. Missing or invalid snapshot fallback remains source-order EEG.
- `channel_units` on the imported recording remain source-file provenance. `PreparedSourceContract.channel_units` describes the numeric prepared arrays and is therefore `V` for all EEG analysis inputs, matching MNE's V-valued sample reader and `SampleBlock`.
- Add nullable import snapshot and source-column mapping JSON columns in schema migration 23. Return these fields in import/list/detail responses; the desktop access adapter combines them into a local manifest snapshot without changing the backend raw-sample response.
- Keep every backend sample request and matrix in source-file column order. The desktop reader selects the projected channel indexes only when constructing a view.

## Risks / Trade-offs

- [A same-label but different physical electrode could match] → require a one-to-one exact normalized label match for every EEG channel; otherwise do not bind a configuration.
- [Old imported rows have no binding] → nullable migration leaves them readable and the desktop displays their source channels unchanged.
- [Configuration metadata arrives from a client] → validate JSON size/shape and unique channel identities before matching; persist only a complete one-to-one EEG match, otherwise leave the recording unbound.

## Migration Plan

1. Add nullable metadata columns and migrate schema version 22 to 23 transactionally.
2. Extend import request and metadata responses; older clients omit the optional binding and preserve current behavior.
3. Populate display snapshots for newly imported records only after exact matching; existing rows remain unbound.
4. Rollback code can read nullable columns as absent; rolling back the database schema itself is not supported, and no existing recording data is deleted.

## Validation

Test projection order/index preservation, disabled-channel exclusion, unknown/malformed snapshot fallback, impedance filtering, live analysis filtering, exact/ambiguous import matching, migration 22→23, persisted metadata round-trip, and unchanged source-column raw sample reads.
