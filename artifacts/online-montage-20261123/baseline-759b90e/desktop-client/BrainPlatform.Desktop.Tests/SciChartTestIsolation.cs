using Xunit;

// WPF/SciChart dependency-property metadata is shared across the STA chart tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
