"""EEG montage definitions and signal derivation.

Montages are expressed as named linear terms instead of raw array indexes.  The
same definitions are used by static windows and continuous playback.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np


ALIASES = {"T3": "T7", "T4": "T8", "T5": "P7", "T6": "P8"}


@dataclass(frozen=True)
class MontageTerm:
    channel: str
    weight: float


@dataclass(frozen=True)
class MontageChannel:
    name: str
    terms: tuple[MontageTerm, ...]


@dataclass(frozen=True)
class MontageDefinition:
    id: str
    label: str
    channels: tuple[MontageChannel, ...]
    required_channels: tuple[str, ...]
    excluded_channels: tuple[str, ...] = ()


MONTAGE_CATALOG = (
    ("original", "原始记录（不重参考）"),
    ("average", "平均参考"),
    ("linked_mastoids", "双乳突平均参考"),
    ("longitudinal_bipolar", "纵向双极"),
    ("transverse_bipolar", "横向双极"),
    ("cz_reference", "Cz 参考"),
    ("standard_16", "标准 16 通道参考"),
    ("standard_18", "标准 18 通道参考"),
    ("standard_20", "标准 20 通道参考"),
    ("custom_bipolar", "自定义双极"),
)

STANDARD_CHANNEL_SETS = {
    "standard_16": ("Fp1", "Fp2", "F7", "F3", "F4", "F8", "T7", "C3", "Cz", "C4", "T8", "P7", "P3", "Pz", "P4", "P8"),
    "standard_18": ("Fp1", "Fp2", "F7", "F3", "F4", "F8", "T7", "C3", "Cz", "C4", "T8", "P7", "P3", "Pz", "P4", "P8", "O1", "O2"),
    "standard_20": ("Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8", "T7", "C3", "Cz", "C4", "T8", "P7", "P3", "Pz", "P4", "P8", "O1", "O2", "Oz"),
}


def canonical_name(name: str) -> str:
    value = str(name).strip().upper()
    return ALIASES.get(value, value)


def resolve_channel(name: str, available: list[str]) -> str | None:
    wanted = canonical_name(name)
    for candidate in available:
        if canonical_name(candidate) == wanted:
            return candidate
    return None


def _unique(names: list[str]) -> tuple[str, ...]:
    result: list[str] = []
    for name in names:
        if name not in result:
            result.append(name)
    return tuple(result)


def _requested(available: list[str], requested: list[str] | None) -> list[str]:
    if requested is None:
        return list(available)
    result: list[str] = []
    for name in requested:
        resolved = resolve_channel(name, available)
        if resolved and resolved not in result:
            result.append(resolved)
    if not result:
        raise ValueError("所选显示通道不存在于录制文件")
    return result


def _difference(name: str, positive: str, negative: str) -> MontageChannel:
    return MontageChannel(name, (MontageTerm(positive, 1.0), MontageTerm(negative, -1.0)))


def _referential_set(montage_id: str, label: str, available: list[str]) -> MontageDefinition:
    requested = STANDARD_CHANNEL_SETS[montage_id]
    selected = _require(list(requested), available)
    channels = tuple(MontageChannel(name, (MontageTerm(name, 1.0),)) for name in selected)
    return MontageDefinition(montage_id, label, channels, tuple(selected))


def _require(names: list[str], available: list[str]) -> list[str]:
    missing = [name for name in names if resolve_channel(name, available) is None]
    if missing:
        raise ValueError(f"导联方案缺少通道：{', '.join(missing)}")
    return [resolve_channel(name, available) or name for name in names]


def build_montage(montage_id: str, available_names: list[str], requested_names: list[str] | None = None, excluded_names: list[str] | None = None) -> MontageDefinition:
    """Build a validated montage for a concrete recording."""
    available = [str(name) for name in available_names]
    montage_id = str(montage_id or "original").strip()
    if montage_id.startswith("reference:"):
        reference = montage_id.split(":", 1)[1]
        reference_name = resolve_channel(reference, available)
        if reference_name is None:
            raise ValueError("参考通道不存在")
        selected = _requested(available, requested_names)
        channels = tuple(_difference(f"{name}-{reference_name}", name, reference_name) for name in selected if name != reference_name)
        if not channels:
            raise ValueError("参考导联至少需要一个非参考通道")
        required = _unique([term.channel for item in channels for term in item.terms])
        return MontageDefinition(montage_id, f"{reference_name} 参考", channels, required)

    if montage_id == "original":
        selected = _requested(available, requested_names)
        channels = tuple(MontageChannel(name, (MontageTerm(name, 1.0),)) for name in selected)
        return MontageDefinition(montage_id, "原始记录（不重参考）", channels, tuple(selected))

    if montage_id == "average":
        selected = _requested(available, requested_names)
        excluded = []
        for name in excluded_names or []:
            resolved = resolve_channel(name, available)
            if resolved is None:
                raise ValueError(f"平均参考排除通道不存在：{name}")
            if resolved not in excluded:
                excluded.append(resolved)
        participants = [name for name in available if name not in excluded]
        if not participants:
            raise ValueError("平均参考至少需要一个参与通道")
        terms = tuple(MontageTerm(name, -1.0 / len(participants)) for name in participants)
        channels = tuple(MontageChannel(f"{name}-AVG", (MontageTerm(name, 1.0),) + terms) for name in selected)
        return MontageDefinition(montage_id, "平均参考", channels, tuple(available), tuple(excluded))

    if montage_id == "linked_mastoids":
        mastoids = _require(["M1", "M2"], available)
        selected = [name for name in _requested(available, requested_names) if name not in mastoids]
        if not selected:
            selected = [name for name in available if name not in mastoids]
        terms = (MontageTerm(mastoids[0], -0.5), MontageTerm(mastoids[1], -0.5))
        channels = tuple(MontageChannel(f"{name}-M1M2", (MontageTerm(name, 1.0),) + terms) for name in selected)
        required = _unique([*mastoids, *selected])
        return MontageDefinition(montage_id, "双乳突平均参考", channels, required)

    if montage_id == "longitudinal_bipolar":
        pairs = (("Fp1", "F7"), ("F7", "T7"), ("T7", "P7"), ("P7", "O1"),
                 ("Fp1", "F3"), ("F3", "C3"), ("C3", "P3"), ("P3", "O1"),
                 ("Fp2", "F8"), ("F8", "T8"), ("T8", "P8"), ("P8", "O2"),
                 ("Fp2", "F4"), ("F4", "C4"), ("C4", "P4"), ("P4", "O2"),
                 ("Fz", "Cz"), ("Cz", "Pz"))
        missing = sorted({name for pair in pairs for name in pair if resolve_channel(name, available) is None})
        if missing:
            raise ValueError(f"纵向双极缺少通道：{', '.join(missing)}")
        channels = tuple(_difference(f"{a}-{b}", resolve_channel(a, available) or a, resolve_channel(b, available) or b) for a, b in pairs)
        required = _unique([term.channel for item in channels for term in item.terms])
        return MontageDefinition(montage_id, "纵向双极", channels, required)

    if montage_id == "transverse_bipolar":
        pairs = (("F7", "Fp1"), ("Fp1", "Fp2"), ("Fp2", "F8"),
                 ("F7", "F3"), ("F3", "Fz"), ("Fz", "F4"), ("F4", "F8"),
                 ("T7", "C3"), ("C3", "Cz"), ("Cz", "C4"), ("C4", "T8"),
                 ("P7", "P3"), ("P3", "Pz"), ("Pz", "P4"), ("P4", "P8"),
                 ("P7", "O1"), ("O1", "O2"), ("O2", "P8"))
        missing = sorted({name for pair in pairs for name in pair if resolve_channel(name, available) is None})
        if missing:
            raise ValueError(f"横向双极缺少通道：{', '.join(missing)}")
        channels = tuple(_difference(f"{a}-{b}", resolve_channel(a, available) or a, resolve_channel(b, available) or b) for a, b in pairs)
        required = _unique([term.channel for item in channels for term in item.terms])
        return MontageDefinition(montage_id, "横向双极", channels, required)

    if montage_id == "cz_reference":
        reference_name = resolve_channel("Cz", available)
        if reference_name is None:
            raise ValueError("Cz 参考缺少通道：Cz")
        selected = [name for name in _requested(available, requested_names) if name != reference_name]
        if not selected:
            raise ValueError("Cz 参考至少需要一个非参考通道")
        channels = tuple(_difference(f"{name}-{reference_name}", name, reference_name) for name in selected)
        required = _unique([term.channel for item in channels for term in item.terms])
        return MontageDefinition(montage_id, "Cz 参考", channels, required)

    if montage_id in STANDARD_CHANNEL_SETS:
        labels = {"standard_16": "标准 16 通道参考", "standard_18": "标准 18 通道参考", "standard_20": "标准 20 通道参考"}
        return _referential_set(montage_id, labels[montage_id], available)

    if montage_id == "custom_bipolar":
        selected = _requested(available, requested_names)
        if len(selected) < 2:
            raise ValueError("自定义双极至少需要两个通道")
        channels = tuple(_difference(f"{left}-{right}", left, right) for left, right in zip(selected, selected[1:]))
        return MontageDefinition(montage_id, "自定义双极", channels, tuple(selected))

    raise ValueError("不支持的导联方案")


def apply_montage(data: np.ndarray, available_names: list[str], definition: MontageDefinition) -> np.ndarray:
    """Apply a montage to a `(samples, channels)` voltage matrix."""
    values = np.asarray(data, dtype=float)
    if values.ndim != 2 or values.shape[1] != len(available_names):
        raise ValueError("导联输入数据形状不正确")
    indexes = {name: index for index, name in enumerate(available_names)}
    output = np.zeros((values.shape[0], len(definition.channels)), dtype=float)
    for output_index, channel in enumerate(definition.channels):
        for term in channel.terms:
            try:
                output[:, output_index] += values[:, indexes[term.channel]] * term.weight
            except KeyError as exc:
                raise ValueError(f"导联缺少通道：{term.channel}") from exc
    return output


def describe_montages(available_names: list[str]) -> list[dict[str, object]]:
    result = []
    for montage_id, label in MONTAGE_CATALOG:
        try:
            definition = build_montage(montage_id, available_names)
            result.append({"id": montage_id, "label": label, "available": True, "channels": [item.name for item in definition.channels], "missing": []})
        except ValueError as exc:
            result.append({"id": montage_id, "label": label, "available": False, "channels": [], "missing": [str(exc)]})
    return result


def montage_formulas(definition: MontageDefinition) -> dict[str, str]:
    """Return human-readable linear formulas for diagnostics."""
    formulas: dict[str, str] = {}
    for channel in definition.channels:
        terms = []
        for term in channel.terms:
            sign = "+" if term.weight >= 0 else "-"
            magnitude = abs(term.weight)
            coefficient = "" if magnitude == 1 else f"{magnitude:g}*"
            terms.append(f"{sign} {coefficient}{term.channel}")
        formulas[channel.name] = " ".join(terms).lstrip("+ ")
    return formulas
