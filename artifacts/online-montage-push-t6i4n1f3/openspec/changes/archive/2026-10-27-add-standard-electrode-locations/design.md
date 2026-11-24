## Context

See `proposal.md` for motivation and `specs/desktop/electrode-locations/spec.md` for observable behavior. `ChannelConfigurationEntry` currently stores an electrode label, selection state, and order. Channel profiles are JSON snapshots; `ChannelConfigurationFingerprint` defines signal/configuration identity, and recording manifests retain profile snapshots. Profiles may be locked after montage references exist, and older JSON must remain readable. The backend already depends on MNE (locked at 1.12.1) and uses it for BDF/EDF input; current production code does not call MNE montage resolution or topomap plotting. The WPF client uses SciChart.

Time, sample counters, channel order, values in volts, and signal-reference computations are unchanged. The added fields describe spatial metadata only.

## Goals / Non-Goals

**Goals:**
- Resolve each supported scalp electrode through the existing MNE standard montage source and retain a reproducible three-dimensional location snapshot.
- Preserve the mapping in current profiles and historical recording snapshots.
- Make unavailable positions explicit and reject contradictory location data.
- Show resolved standard-template scalp locations in a head-top view in channel settings.
- Provide a modeless floating position window where a selected custom EEG label can receive a user-placed 2D diagram position, saved independently from standard or measured coordinates.

**Non-Goals:**
- Topomap rendering or interpolation, selecting a time/frequency/statistic value, or clinical interpretation.
- Assigning locations to bipolar derivations or converting bipolar values into electrode potentials.
- Measured manual digitizer entry, arbitrary 10–10/10–5 coordinate resolution, or changing acquisition/reference behavior.

## Decisions

- Resolve exclusively through MNE `standard_1020`. Coordinates are XYZ in meters in MNE's declared `mri` coordinate frame, sourced from the montage bundled with the reported MNE version. This is a standard-template position, not a measured patient/cap location. The typed read-only `POST /api/electrode-locations/resolve` boundary accepts up to 128 labels and returns one result per input, including unavailable results. Normalize only by trimming outer whitespace and case-insensitive exact matching; do not alias T3/T7, T4/T8, T5/P7, or T6/P8. MNE objects remain backend-internal.
- The repository lock resolves MNE to 1.12.1 (the project range remains `mne>=1.8`); runtime provenance is returned with every result so environments using another allowed release remain identifiable. The installed MNE 1.12.1 `standard_1020` montage currently exposes 94 positions. The coordinate table is supplied by the installed MNE package and remains under that dependency's license and provenance; no local coordinate copy or independent redistribution is introduced. Deployments must retain the applicable MNE package/license notices.
- Store the resolved location snapshot on each `ChannelConfigurationEntry` as optional typed metadata (canonical site ID, XYZ, frame ID, source/version). The channel label remains the operator/device mapping. Resolution is performed through a typed boundary adapter, not by placing MNE objects in API, persistence, or WPF contracts. This keeps physical input identity and spatial metadata together while allowing later custom positions without conflating label text and coordinates.
- Include all spatial fields in `ChannelConfigurationFingerprint`; otherwise two configurations with different scalp assignments could share identity. Recording snapshots already serialize the immutable profile, so the additive optional fields travel with historical data without a new API or raw format.
- Legacy fields deserialize as unavailable. Do not silently backfill historical snapshots at load time, because that would change their interpreted fingerprint and make provenance depend on the currently installed MNE data. A user can explicitly resolve labels in the editable draft and save a new profile version after reviewing the returned template locations.
- Add an explicit location column/status to channel settings. Unknown labels remain valid for waveform use, but cannot be consumed spatially. Existing channel-label uniqueness validation plus snapshot validation prevent duplicate canonical sites and malformed or label-contradicting snapshots. The desktop validates declared frame, unit, source, version presence, finiteness, and label/site identity; authoritative coordinates come only from the typed backend resolver, avoiding a second coordinate table in the client.
- Render a head-top position illustration from the saved `mri` XYZ snapshots only. Project MRI/RAS X/Y to the plane (positive Y anterior/up, negative X on the viewer's left); Z is intentionally omitted from this top view. Include a nose and ears for orientation and electrode labels. Exclude unavailable, non-finite, unsupported-source, or non-`mri`/meter snapshots. Label the illustration as a standard-template diagram, not measured placement. Keep projection as a small pure function and drawing in a dedicated Channels view control, not in the large workspace view model.
- Open the map in a modeless owned tool window so the channel settings stay interactive. Selecting a row and clicking/dragging inside the head outline assigns normalized 2D coordinates only when the row has no standard location. Persist these separately as `ManualMapPosition`; do not populate `ElectrodeLocation` or send manual positions to the MNE resolver. Editing the channel label clears both forms of position. Display standard points in blue and manually placed diagram points in amber.
- Keep spatial identity in `Modules/Channels`; montage outputs remain a separate derivation/reference layer. Future topomap support must define which per-electrode values it accepts before interpolation.

Alternatives considered: check in a duplicate local 10–20 table (rejected because the backend already ships MNE's standard montage data and a second table can drift); calculate coordinates from labels at every read without persisting snapshots (rejected because future MNE/data updates could reinterpret old recordings); store only a site name and look coordinates up later (rejected because historical position would drift); add site locations to montage outputs (rejected because outputs may be bipolar or rereferenced and are not physical input electrodes). MNE's Matplotlib topomap renderer is not a native WPF control, so this design does not select it as the desktop renderer or add plotting dependencies.

## Risks / Trade-offs

- [Template and hand-placed positions can be mistaken for measured cap-specific sensor positions] → Label the template and the manual 2D diagram positions separately; never describe either as a measurement.
- [A standard dataset may not cover all configured labels] → Persist an explicit unavailable state; never interpolate a guessed position.
- [Adding metadata changes fingerprints and profile lock behavior] → Include a migration/compatibility test and ensure old profile deserialization remains stable.
- [MNE's bundled standard montage data or coordinate conventions may not cover the configured cap or labels] → Use only `standard_1020`, disclose the template and coordinate frame, and expose uncovered labels as unavailable. This does not certify physical sensor placement.

## Verification boundary

The checked-out backend environment resolved MNE 1.12.1 and 94 named positions from `standard_1020`; the installed distribution includes its MNE-Python license text, whose redistribution conditions apply when shipping that dependency. Project dependency constraints permit other MNE versions, so each API result records the runtime version. Unit and integration tests verify engineering contracts only. They do not establish that a specific patient's electrodes match the standard template, validate topomap interpolation, or constitute clinical validation.

## Migration Plan

1. Add optional location fields with defaults that deserialize legacy profiles as unavailable.
2. Add a backend MNE montage adapter and typed location-resolution boundary; verify channel label normalization and explicit coordinate frame/version before returning coordinates.
3. Persist returned location snapshots, settings display, and validation; never re-resolve old snapshots silently.
4. Add tests for legacy round-trip, canonical mapping, fingerprints, snapshot independence, unsupported/bipolar labels, and MNE source/version behavior.
5. Rollback is code rollback only: older builds may ignore additive JSON fields, but must not save over newer profiles without preserving unknown fields. Verify the serializer behavior before claiming downgrade-safe profile writes; no destructive migration is allowed.

## Open Questions

- Which value types a future topomap may accept (for example, per-electrode potential or channel-wise spectral metrics) and what reference semantics apply. This is outside this change and must be specified before implementing topomap interpolation or rendering. Bipolar montage outputs are spatially ineligible unless a separate scientific rule is defined.
