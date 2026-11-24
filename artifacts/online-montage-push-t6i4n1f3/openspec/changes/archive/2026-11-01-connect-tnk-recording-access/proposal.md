# Share the workspace recording access session with TNK

Review and the desktop algorithm workbench currently open the same local recording through different lifetimes. Review owns the desktop access session, while TNK only sends a path to the backend and has no desktop-side source identity boundary.

This change gives the workspace one `RecordingSessionManager` owner and lets Review and TNK acquire leases from it. The lease is used for desktop identity and metadata validation; scientific TNK execution remains in the backend `RecordingAccessService`. The change does not invent a transport for sending raw samples through WPF.

## Scope

- Move `RecordingSessionManager` ownership to `DesktopWorkspaceViewModel`.
- Inject the workspace manager into Review and `AlgorithmListViewModel`.
- Make TNK registration validate the selected local recording through the shared access lease before registering it with the backend.
- Release the TNK lease on recording replacement and workbench disposal.
- Document that backend dynamic windows remain the scientific read boundary until an explicit sample transport contract is approved.

## Non-goals

- No EEG calculation or filtering in WPF.
- No raw sample JSON upload or new backend compatibility path.
- No live ring-buffer adapter.
