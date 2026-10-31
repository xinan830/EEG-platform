# Design

The TNK coordinator keeps its current one-call shared spectrum path. When that
call raises `SpectralQualityGateError`, it retries the exact same sample range
for each mapped channel. The retries use the same filter, bounded context, and
quality contract, so the change only changes the rejection granularity.

If at least one channel succeeds, the coordinator builds one in-memory spectrum
with the successful PSD rows and NaN rows for rejected channels. The evidence
stores a quality record keyed by channel. Before a child module receives this
spectrum, the coordinator checks the rows it requested; a rejected row raises a
structured `TNK_QUALITY_REJECTED` result for that child. RBP therefore retains
valid channels independently, while multi-channel metrics remain unavailable
when one of their required channels is invalid.

If every channel is rejected, the existing group-level rejection remains. No
scientific value is fabricated.

The fallback is intentionally limited to TNK's shared prewarm path. Other
algorithms keep their existing quality semantics.
