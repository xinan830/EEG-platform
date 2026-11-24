## Design

The manifest is validation metadata, not executable code. `ExtensionGovernanceService`
checks typed Pydantic input against a closed vocabulary and returns a normalized
manifest plus explicit execution boundaries. The service neither saves manifests
nor grants their declared permissions; a future installed-plugin feature must
create its own security change.

Allowed execution modes are `closed_definition_graph` and `metadata_only`.
The only declared permissions are access to recording metadata or derived
artifacts. Network, subprocess, Python source, filesystem and secret access
are absent from the vocabulary and Pydantic rejects undeclared fields.

The API serves `/api/extensions/capabilities` and `/api/extensions/validate`.
They reuse the existing closed primitive type names. This makes module source,
compatibility and research-validation claims inspectable without changing the
definition executor or current analysis APIs.
