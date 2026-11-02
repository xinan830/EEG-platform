# Design

`DesktopWorkspaceViewModel` owns exactly one `RecordingSessionManager`. `MainWindow` uses that owner when opening Review. `AlgorithmListViewModel` receives the same manager and retains one lease for the selected recording while it is registered.

The TNK lease validates the local session ID, source unit, sampling rate, sample bounds, and source channel table before the backend registration request. The backend remains authoritative for scientific execution and reads the registered source through its own unified recording-access service. A future WPF-to-backend sample transport is a separate contract because dynamic TNK replay may require context ranges and stateful ordering.

Lease acquisition is cancelled/released when the selected recording changes or the algorithm catalog is disposed. The existing backend recording ID remains the request identity; no duplicate desktop reader is opened by Review and TNK.
