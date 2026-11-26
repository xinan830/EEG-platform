"""Source-bound physiological identities; reference selection never changes these roles."""
from dataclasses import replace
import hashlib
import json
import os
from pathlib import Path
from threading import RLock
from uuid import uuid4


_lock = RLock()
VALID_IDENTITIES = {"eeg", "eog", "ecg", "unconfirmed"}
VOLTAGE_UNITS = {"V", "mV", "uV", "µV", "μV", "\x83\xcaV"}


def validated_import_identities(snapshot, labels, units):
    """Validate identity metadata before publication or scientific use."""
    if not isinstance(snapshot, dict):
        raise ValueError("CHANNEL_IDENTITY_INVALID: 导入通道身份快照格式无效")
    entries = snapshot.get("PhysiologicalIdentities", [])
    if not isinstance(entries, list) or any(not isinstance(row, dict) for row in entries):
        raise ValueError("CHANNEL_IDENTITY_INVALID: 导入通道身份格式无效")
    seen = set()
    for row in entries:
        column, role = row.get("SourceColumn"), row.get("Identity")
        if (type(column) is not int or column in seen or not 0 <= column < len(labels)
                or not isinstance(role, str) or role not in VALID_IDENTITIES
                or (role != "unconfirmed" and (column >= len(units) or units[column] not in VOLTAGE_UNITS))):
            raise ValueError("CHANNEL_IDENTITY_INVALID: 导入通道身份列、类型或电压单位无效")
        seen.add(column)
    return entries


def identity_path(storage_dir, recording):
    # Recording IDs are opaque; never accept them as filesystem components.
    key = hashlib.sha256(str(recording.id).encode()).hexdigest()
    return Path(storage_dir) / ".channel-identities" / (key + ".json")


def identity_signature(storage_dir, recording):
    if storage_dir is None or not hasattr(recording, "extension"):
        return None
    try:
        stat = identity_path(storage_dir, recording).stat()
        return stat.st_size, stat.st_mtime_ns, stat.st_ino
    except FileNotFoundError:
        return None


def attach_recording_identities(storage_dir, recording):
    if storage_dir is None:
        return recording
    path = identity_path(storage_dir, recording)
    with _lock:
        if not path.is_file():
            return recording
        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
            if not isinstance(payload, dict) or payload.get("schema_version") != "recording-channel-identities-v1":
                raise ValueError
            rows = payload["channels"]
            if not isinstance(rows, list) or any(not isinstance(row, dict) for row in rows):
                raise ValueError
            roles = tuple((row["name"], row["identity"]) for row in rows)
            if (payload["source_sha256"] != recording.source_sha256 or
                    any(not isinstance(name, str) or not name.strip() or not isinstance(role, str) or role not in VALID_IDENTITIES for name, role in roles) or
                    len({name.casefold() for name, _ in roles}) != len(roles)):
                raise ValueError
        except (OSError, KeyError, TypeError, ValueError) as exc:
            raise ValueError("CHANNEL_IDENTITY_INVALID: 通道身份记录损坏或与录音源不符") from exc
    return replace(recording, channel_identities=roles)


def source_channel_identities(storage_dir, recording):
    """Return raw labels and recorded/user identity without guessing physiological role from names."""
    recording = attach_recording_identities(storage_dir, recording)
    roles = {name.casefold(): role for name, role in getattr(recording, "channel_identities", ())}
    sources = {}
    if recording.extension == ".wpf":
        root = Path(recording.stored_name.split(":", 1)[1])
        manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8-sig"))
        for row in sorted(manifest["Channels"], key=lambda row: row["StreamIndex"]):
            name = str(row.get("Label") or "").strip()
            if row.get("Unit") != "V" or not name:
                continue
            kind = str(row["Kind"]).casefold()
            role = str(row.get("PhysiologicalIdentity", "")).casefold()
            if role not in VALID_IDENTITIES:
                role = "eeg" if kind in {"1", "2", "reference", "bipolar"} else "unconfirmed"
            sources[name] = role
    else:
        sources = dict(zip(recording.raw_channel_labels or recording.channels, recording.channel_types))
        if not sources:
            sources = {name: "eeg" for name in recording.channels}
        sources = {name: kind.casefold() if kind.casefold() in {"eeg", "eog", "ecg"} else "unconfirmed"
                   for name, kind in sources.items()}
        for name, unit in zip(sources, recording.channel_units):
            if unit not in VOLTAGE_UNITS:
                sources[name] = "unconfirmed"
        snapshot = json.loads(recording.import_channel_configuration_snapshot_json or "{}")
        entries = validated_import_identities(snapshot, recording.raw_channel_labels, recording.channel_units)
        for row in entries:
            column, role = row.get("SourceColumn"), row.get("Identity")
            sources[recording.raw_channel_labels[column]] = role
    if len({name.casefold() for name in sources}) != len(sources):
        raise ValueError("CHANNEL_IDENTITY_INVALID: 原始电压源通道名称重复")
    if any(name.casefold() not in {label.casefold() for label in sources} for name, _ in getattr(recording, "channel_identities", ())):
        raise ValueError("CHANNEL_IDENTITY_INVALID: 已保存通道身份与源列不匹配")
    return {name: (roles.get(name.casefold(), role), "user_confirmed" if name.casefold() in roles else
                   "recorded_metadata" if role != "unconfirmed" else "unconfirmed")
            for name, role in sources.items()}


def role_fingerprint(storage_dir, recording):
    roles = source_channel_identities(storage_dir, recording)
    if not getattr(recording, "channel_identities", ()) and not any(role != "eeg" for role, _ in roles.values()):
        return None
    return hashlib.sha256(json.dumps(sorted((name.casefold(), role) for name, (role, _) in roles.items())).encode()).hexdigest()


def bind_session_roles(session, storage_dir, recording, expected_signature):
    with _lock:
        current = attach_recording_identities(storage_dir, recording)
        if (identity_signature(storage_dir, recording) != expected_signature or
                getattr(current, "channel_identities", ()) != getattr(recording, "channel_identities", ())):
            session.close()
            raise ValueError("CHANNEL_IDENTITY_CHANGED: 通道身份已变化，请重新开始分析")
        fingerprint = role_fingerprint(storage_dir, recording)
        path = identity_path(storage_dir, recording) if storage_dir is not None else None
        signature = path.read_bytes() if path is not None and path.exists() else None
    if fingerprint:
        combined = hashlib.sha256(json.dumps([session.identity.signal_view_sha256, fingerprint]).encode()).hexdigest()
        session.identity = replace(session.identity, signal_view_sha256=combined)
    if storage_dir is not None:
        validate = session._validate_source
        def validate_roles():
            validate()
            if (path.read_bytes() if path.exists() else None) != signature:
                raise ValueError("CHANNEL_IDENTITY_CHANGED: 通道身份已变化，请重新开始分析")
        session._validate_source = validate_roles
    return session


def save_recording_identities(recordings, recording, update):
    from app.services.artifact_references import discover_artifact_references
    if update.source_sha256 != recording.source_sha256:
        raise ValueError("CHANNEL_IDENTITY_SOURCE_MISMATCH: 录音源已变化，请重新读取通道")
    available = {row["name"].casefold(): row["name"]
                 for row in discover_artifact_references(recordings, recording)["channels"]}
    chosen = {}
    for row in update.channels:
        key = row.name.strip().casefold()
        if key not in available or key in chosen:
            raise ValueError("CHANNEL_IDENTITY_INVALID: 通道不存在、非电压源或重复")
        chosen[key] = (available[key], row.identity)
    path = identity_path(recordings.storage_dir, recording)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix("." + uuid4().hex + ".tmp")
    with _lock:
        roles = dict(attach_recording_identities(recordings.storage_dir, recording).channel_identities)
        for name, role in chosen.values():
            roles = {old: value for old, value in roles.items() if old.casefold() != name.casefold()}
            roles[name] = role
        payload = dict(schema_version="recording-channel-identities-v1", source_sha256=recording.source_sha256,
                       channels=[dict(name=name, identity=role) for name, role in sorted(roles.items())])
        try:
            with temporary.open("x", encoding="utf-8") as stream:
                json.dump(payload, stream, ensure_ascii=False, allow_nan=False)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, path)
        finally:
            temporary.unlink(missing_ok=True)
    recordings.offline_scientific_preprocessing.invalidate_recording(recording.id)
    return discover_artifact_references(recordings, replace(recording, channel_identities=tuple(roles.items())))


def source_identity_lookup(storage_dir, recording):
    roles = source_channel_identities(storage_dir, recording)
    lookup = {name.casefold(): role for name, (role, _) in roles.items()}
    if recording.extension != ".wpf" and recording.import_channel_column_mapping_json:
        from app.services.imported_analysis_signals import imported_source_channel_labels
        for raw, alias in zip(recording.raw_channel_labels, imported_source_channel_labels(recording), strict=True):
            if lookup.get(raw.casefold()) == "eeg":
                lookup[alias.casefold()] = "eeg"
    return lookup


def scientific_channel_names(recordings, recording):
    """Predict the role partition for callers without changing raw recording metadata."""
    storage = getattr(recordings, "storage_dir", None)
    if not hasattr(recording, "extension") or not role_fingerprint(storage, recording):
        return tuple(recording.channels)
    roles = source_channel_identities(storage, recording)
    if recording.stored_name.startswith("wpf-montage:") or recording.selected_montage_id:
        from app.services.artifact_references import exclude_reference_outputs
        return exclude_reference_outputs(recording.channels, recording.raw_channel_labels,
            [name for name, (role, _) in roles.items() if role != "eeg"],
            recordings.analysis_signal_provenance(recording))
    labels = tuple(roles)
    if recording.extension != ".wpf" and recording.import_channel_column_mapping_json:
        from app.services.imported_analysis_signals import imported_source_channel_labels
        labels = imported_source_channel_labels(recording)
    return tuple(label for label, (role, _) in zip(labels, roles.values(), strict=True) if role == "eeg")


def dedicated_reference_sources(storage_dir, recording, selected=()):
    if not hasattr(recording, "extension"):
        return tuple(selected)
    roles = source_channel_identities(storage_dir, recording)
    lookup = source_identity_lookup(storage_dir, recording)
    for name in selected:
        entry = lookup.get(name.casefold())
        if entry is None:
            raise ValueError("ARTIFACT_REFERENCE_UNAVAILABLE: 选择的参考通道不存在")
        if entry == "unconfirmed":
            raise ValueError("CHANNEL_IDENTITY_CONFIRMATION_REQUIRED: 请先确认参考通道是 EEG、专用 EOG 还是专用 ECG")
    return tuple(name for name, (role, _) in roles.items() if role != "eeg")


def resolve_removal_references(recordings, recording, spec):
    """Resolve declared auxiliary identities; EEG proxies remain in the EEG matrix."""
    roles = source_channel_identities(getattr(recordings, "storage_dir", None), recording)
    canonical = {name.casefold(): name for name in roles}
    eog = tuple(canonical.get(name.casefold(), name) for name in spec.eog_channels) or tuple(
        name for name, (role, _) in roles.items() if role == "eog")
    if recording.extension != ".wpf" and recording.import_channel_column_mapping_json:
        from app.services.imported_analysis_signals import imported_source_channel_labels
        mapped = imported_source_channel_labels(recording)
        aliases = {name.casefold(): mapped[i] for i, name in enumerate(recording.raw_channel_labels)
                   if roles.get(name, (None,))[0] == "eeg"}
        eog = tuple(aliases.get(name.casefold(), name) for name in eog)
    ecg = (tuple(canonical.get(name.casefold(), name) for name in spec.ecg_channels) or tuple(
        name for name, (role, _) in roles.items() if role == "ecg")) if spec.method == "ica_infomax" else ()
    return replace(spec, eog_channels=eog, ecg_channels=ecg).normalized()
