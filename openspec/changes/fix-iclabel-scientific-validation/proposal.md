# Why
ICLabel can classify a silently changed reference, infer convergence from rounded logs, require an unnecessary eye reference, or fail with an unstructured model error. The dependency lock also omits its runtime.

# What Changes
- Verify complete CAR and preserve ICA fit samples during classification.
- Use exact Infomax stopping evidence; keep EOG/ECG scoring optional for ICA.
- Report known model/runtime failures explicitly and synchronize the dependency lock.
- Invalidate affected fit/application cache identities and verify persistence with a numerical tolerance.
- Refresh model hash evidence when its file state changes; verify synthetic rhythm preservation after applying actual recommendations and reconcile remaining plan versions.

# Capabilities
## New Capabilities
None.
## Modified Capabilities
- `signal/preprocessing`: trustworthy ICA classification, stopping diagnostics and optional reference scoring.

# Impact
Backend scientific helpers, review cache identity, focused tests, desktop status text and uv.lock. Internal samples remain sample-major float64 V, time/order and HTTP response shapes remain unchanged. No WebSocket changes, acquisition/display filtering changes, data migration, or automatic clinical decisions. Old reviewed fingerprints require review again; stored recordings are untouched.
