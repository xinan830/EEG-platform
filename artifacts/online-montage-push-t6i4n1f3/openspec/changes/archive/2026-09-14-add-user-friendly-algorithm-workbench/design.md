# Design

## Two presentation modes

The default is the **user mode**. It shows the selected official algorithm's
Chinese name, purpose, backend calculation steps, result/unit meaning, and a
non-clinical-use note. It deliberately hides internal graph adapter names.

The opt-in **developer mode** retains the existing workbench intact. It provides
all fields needed to create, validate, preview, publish, compare, and inspect
algorithm definitions. Official composite definitions remain non-editable.

## Data boundary

Readable text is a frontend display catalogue keyed by the persisted definition
name. It is not a replacement for algorithm provenance. The API definitions,
their IDs, versions, nodes, configuration digests, and execution semantics
remain unchanged.

The frontend only presents API data and static explanatory copy. No EEG,
frequency, PSD, band-power, or ratio is recomputed in the browser.

## Failure behavior

Changing the selected definition clears status text and preview output from the
previous definition. This prevents a successful validation message for one
algorithm from being visually attributed to another.
