# Design

The first user builder exposes curated absolute and relative Delta, Theta,
Alpha, and Beta power features. A user selects two features, one approved
binary operation, and an output name. The client translates this selection to
the existing canonical definition draft and submits it to backend validation,
then creates a normal definition and immutable version.

Feature values are not calculated by the client. A later execution endpoint
will resolve feature identifiers against a recording, analysis range, channel,
and the frozen offline-spectral-v3 contract. Until that endpoint exists, save
means definition creation, not a fabricated EEG result.
