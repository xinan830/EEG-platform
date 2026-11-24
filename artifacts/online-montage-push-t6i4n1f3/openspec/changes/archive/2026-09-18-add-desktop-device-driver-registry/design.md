## Decision

`AcquisitionDriverRegistry` owns the explicit set of installed drivers.
`ConfiguredAcquisitionRuntime` receives an `AcquisitionDriverConfiguration`,
asks the registry to create an `IAcquisitionDeviceAdapter`, and otherwise uses
only common acquisition contracts. A desktop composition catalog registers the
ANT/eego driver today.

The settings bag is opaque to the runtime. The ANT driver alone converts its
required SDK path and optional ranges to `AntEegoAdapterOptions`. Channel label
mapping stays in the common connection envelope because it is a platform
configuration applied to a device-returned native channel index.

## Alternatives Rejected

Using reflection or scanning DLLs for plugins was rejected: vendor SDK loading
must remain explicit and auditable. Moving all ANT settings into a speculative
generic form was rejected because no second SDK establishes a stable common
parameter vocabulary.

## Lifecycle

Every adapter implements `IAsyncDisposable`. The runtime disposes its
coordinator before disposing the adapter, preserving vendor stream teardown
ordering. A missing driver fails before discovery or stream opening and never
creates a synthetic device.

## Verification

Tests configure the runtime with a non-ANT test driver, verify discovery flows
through it, reject duplicate and unknown drivers, and retain ANT connection
configuration tests.
