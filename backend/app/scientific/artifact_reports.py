"""Project immutable recording-level artifact reports without redetecting signals."""

from dataclasses import replace
from typing import Sequence

from app.scientific.artifact_segments import ArtifactSegmentReport
from app.scientific.contracts.preprocessing import ARTIFACT_DETECTION_VERSION


def project_artifact_report(
    report: ArtifactSegmentReport, start_sample: int, end_sample: int,
    channels: Sequence[str] | None = None,
) -> ArtifactSegmentReport:
    """Clip absolute half-open intervals and channel scopes; retain original detector context."""
    if not report.start_sample_offset <= start_sample < end_sample <= report.start_sample_offset + report.sample_count:
        raise ValueError('坏段报告投影范围超出检测范围')
    requested = None if channels is None else {ch.casefold() for ch in channels}
    segments = []
    for seg in report.segments:
        start, end = max(start_sample, seg.start_sample), min(end_sample, seg.end_sample)
        if start >= end:
            continue
        scoped = seg.channels if requested is None else tuple(ch for ch in seg.channels if ch.casefold() in requested)
        if seg.channels and not scoped:
            continue
        segments.append(replace(seg, start_sample=start, end_sample=end, channels=scoped))
    covered_until, rejected = start_sample, 0
    for seg in sorted(segments, key=lambda item: item.start_sample):
        rejected += max(0, seg.end_sample - max(covered_until, seg.start_sample))
        covered_until = max(covered_until, seg.end_sample)
    return replace(report, start_sample_offset=start_sample, sample_count=end_sample - start_sample,
                   segments=tuple(segments), rejected_sample_count=rejected,
                   clean_ratio=1 - rejected / (end_sample - start_sample))
