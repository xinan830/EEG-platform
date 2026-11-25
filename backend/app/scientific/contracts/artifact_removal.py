"""Immutable offline artifact-removal contract (EOG regression, SSP, reviewable ICA)."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import math
from typing import Literal


ArtifactRemovalMethod = Literal["none", "eog_regression", "ssp_eog", "ica_infomax"]
_VALID_METHODS = frozenset({"none", "eog_regression", "ssp_eog", "ica_infomax"})


def _dedupe_labels(labels: tuple[str, ...]) -> tuple[str, ...]:
    out: list[str] = []
    seen: set[str] = set()
    for raw in labels:
        label = str(raw).strip()
        if label and label.casefold() not in seen:
            seen.add(label.casefold())
            out.append(label)
    return tuple(out)


@dataclass(frozen=True)
class ArtifactRemovalSpec:
    """Explicit opt-in offline artifact-removal configuration for continuous EEG sources."""

    method: ArtifactRemovalMethod = "none"
    eog_channels: tuple[str, ...] = ()
    ecg_channels: tuple[str, ...] = ()
    allow_frontal_eog_proxy: bool = False
    ica_n_components: int | None = None
    ica_eog_threshold: float = 0.7
    ica_exclude_components: tuple[int, ...] | None = None
    ica_fit_fingerprint: str | None = None
    ssp_n_eeg: int = 1
    random_state: int = 42

    def normalized(self) -> ArtifactRemovalSpec:
        method = str(self.method).strip().casefold()
        if method not in _VALID_METHODS:
            raise ValueError(f"不支持的离线去伪迹方法：{self.method!r}")
        if self.ica_n_components is not None:
            if (
                isinstance(self.ica_n_components, bool)
                or not isinstance(self.ica_n_components, int)
                or self.ica_n_components < 2
            ):
                raise ValueError("ICA 成分数必须为大于等于 2 的整数")
        if not math.isfinite(self.ica_eog_threshold) or not (0.0 < self.ica_eog_threshold <= 1.0):
            raise ValueError("ICA 眼电相关阈值必须在 (0, 1] 范围内")
        if isinstance(self.ssp_n_eeg, bool) or not isinstance(self.ssp_n_eeg, int) or not 1 <= self.ssp_n_eeg <= 3:
            raise ValueError("SSP 投影维度必须在 1 到 3 之间")
        if isinstance(self.random_state, bool) or not isinstance(self.random_state, int):
            raise ValueError("random_state 必须为整数")

        exclude: tuple[int, ...] | None = None
        if self.ica_exclude_components is not None:
            parsed: list[int] = []
            for idx in self.ica_exclude_components:
                if isinstance(idx, bool) or not isinstance(idx, int) or idx < 0:
                    raise ValueError("ICA 排除成分索引必须为非负整数")
                parsed.append(int(idx))
            exclude = tuple(sorted(set(parsed)))

        fit_fp = str(self.ica_fit_fingerprint).strip() if self.ica_fit_fingerprint else None
        if method == "none":
            return ArtifactRemovalSpec(method="none")
        return ArtifactRemovalSpec(
            method=method,  # type: ignore[arg-type]
            eog_channels=_dedupe_labels(self.eog_channels),
            ecg_channels=_dedupe_labels(self.ecg_channels),
            allow_frontal_eog_proxy=bool(self.allow_frontal_eog_proxy),
            ica_n_components=self.ica_n_components,
            ica_eog_threshold=float(self.ica_eog_threshold),
            ica_exclude_components=exclude,
            ica_fit_fingerprint=fit_fp,
            ssp_n_eeg=int(self.ssp_n_eeg),
            random_state=int(self.random_state),
        )

    @property
    def is_active(self) -> bool:
        return self.method != "none"

    def fingerprint(self) -> str:
        norm = self.normalized()
        if not norm.is_active:
            return "none"
        payload = json.dumps(
            {
                "method": norm.method,
                "eog_channels": sorted(ch.casefold() for ch in norm.eog_channels),
                "ecg_channels": sorted(ch.casefold() for ch in norm.ecg_channels),
                "allow_frontal_eog_proxy": norm.allow_frontal_eog_proxy,
                "ica_n_components": norm.ica_n_components,
                "ica_eog_threshold": round(norm.ica_eog_threshold, 6),
                "ica_exclude_components": (
                    None if norm.ica_exclude_components is None else list(norm.ica_exclude_components)
                ),
                "ica_fit_fingerprint": norm.ica_fit_fingerprint,
                "ssp_n_eeg": norm.ssp_n_eeg,
                "random_state": norm.random_state,
            },
            sort_keys=True,
            separators=(",", ":"),
        )
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:16]

    def to_evidence(self) -> dict[str, object]:
        norm = self.normalized()
        return {
            "method": norm.method,
            "eog_channels": list(norm.eog_channels),
            "ecg_channels": list(norm.ecg_channels),
            "allow_frontal_eog_proxy": norm.allow_frontal_eog_proxy,
            "ica_n_components": norm.ica_n_components,
            "ica_eog_threshold": norm.ica_eog_threshold if norm.method == "ica_infomax" else None,
            "ica_exclude_components": (
                None if norm.ica_exclude_components is None else list(norm.ica_exclude_components)
            ),
            "ica_fit_fingerprint": norm.ica_fit_fingerprint if norm.method == "ica_infomax" else None,
            "ssp_n_eeg": norm.ssp_n_eeg if norm.method == "ssp_eog" else None,
            "random_state": norm.random_state if norm.method == "ica_infomax" else None,
            "spec_fingerprint": norm.fingerprint(),
        }
