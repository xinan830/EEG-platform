## Why

Algorithm definitions are safely constrained, but the platform has no explicit
machine-readable contract for where a local module came from, what scientific
types it declares, or which extension features are intentionally unsupported.

## What changes

- Add a validate-only local module manifest and capabilities endpoint.
- Support `local_official`, `research_template`, and `user_private` origins.
- Allow only closed definition-graph or metadata-only execution declarations.
- Make supported scientific value types, declarative permissions, platform
  compatibility, validation state, and future-security exclusions explicit.

## Non-goals

No arbitrary Python, third-party plugin loading, networking, process spawning,
file-system permissions, marketplace, signature verification, organization
roles, or multi-tenancy is implemented in this local workstation change.
