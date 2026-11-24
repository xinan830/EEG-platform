import pytest
import numpy as np
import os
from concurrent.futures import ThreadPoolExecutor

from app.algorithm_runtime.on_demand import DynamicWindowIdentity, DynamicWindowResult

from app.algorithm_runtime.errors import DynamicWindowCapacityExceededError
from app.services.dynamic_window_artifacts import DynamicWindowArtifactStore


def test_directory_usage_tracks_published_and_removed_files(tmp_path):
    store = DynamicWindowArtifactStore(tmp_path, repository=object())
    artifact = tmp_path / "result.npz"
    artifact.write_bytes(b"artifact")
    store._track_file(artifact)
    reservation = store.MAX_PAYLOAD_BYTES + store.MAX_METADATA_BYTES

    with pytest.raises(DynamicWindowCapacityExceededError) as error:
        store.check_admission_budget(reservation + artifact.stat().st_size - 1, 1)
    assert error.value.detail["used_bytes"] == artifact.stat().st_size

    store._unlink_file(artifact)
    store._unlink_file(artifact)
    store.check_admission_budget(reservation, 1)


def _saved(store):
    identity = DynamicWindowIdentity(recording_id="r", source_sha256="s", algorithm_id="rbp",
        definition_id="rbp", definition_version="1", scientific_version="v",
        implementation_identity="impl", configuration_sha256="config",
        window_policy_version="policy", start_sample=0, end_sample=2000)
    result = DynamicWindowResult(identity=identity, state="ready", output_kind="algorithm_group",
        axes={"time_s": np.array([10.]), "frequency_hz": np.arange(1., 51.)},
        axis_units={"time_s": "s", "frequency_hz": "Hz"},
        arrays={"alpha": np.array([.2]), "spectral_psd": np.ones((1, 50)) * 1e-12},
        array_units={"alpha": "ratio", "spectral_psd": "V^2/Hz"},
        quality="clean", provenance={"source": "saved"})
    return store.write("w", result, projection_fields=("frequency_hz", "spectral_psd"))


@pytest.fixture
def store(tmp_path):
    repository = type("Repository", (), {"published_artifact_ids": lambda _: set()})()
    return DynamicWindowArtifactStore(tmp_path, repository)


def test_public_read_never_decompresses_backend_projection_arrays(store, monkeypatch):
    artifact = _saved(store)
    store.clear_cache()
    loaded = []
    original = np.lib.npyio.NpzFile.__getitem__

    def read_member(self, key):
        loaded.append(key)
        return original(self, key)

    monkeypatch.setattr(np.lib.npyio.NpzFile, "__getitem__", read_member)
    result = store.read(artifact)
    assert loaded == ["axis_time_s", "array_alpha"]
    for field in ("axes", "axis_units", "axis_shapes"):
        assert "frequency_hz" not in result[field]
    for field in ("arrays", "array_units", "array_shapes"):
        assert "spectral_psd" not in result[field]
    assert result["arrays"]["alpha"] == [.2]


def test_warm_source_and_public_reads_coexist_and_cannot_mutate_cache(store, monkeypatch):
    artifact = _saved(store)
    digest = store.manifest_digest(artifact)
    store.read(artifact, manifest_sha256=digest)
    monkeypatch.setattr(store, "_decode", lambda *_, **__: pytest.fail("warm read decoded again"))
    first = store.read(artifact, manifest_sha256=digest, include_projection_source=True)
    first["arrays"]["spectral_psd"][0, 0] = 123
    first["provenance"]["source"] = "changed"
    again = store.read(artifact, manifest_sha256=digest, include_projection_source=True)
    assert again["arrays"]["spectral_psd"][0, 0] == 1e-12
    assert again["provenance"]["source"] == "saved"
    assert store.read(artifact, manifest_sha256=digest)["arrays"]["alpha"] == [.2]


@pytest.mark.parametrize("target", ["metadata", "payload", "preserved_timestamp", "deleted", "digest"])
def test_warm_read_rejects_changed_corrupt_or_deleted_source(store, target):
    artifact = _saved(store)
    digest = store.manifest_digest(artifact)
    store.read(artifact, manifest_sha256=digest)
    if target == "metadata":
        path = store.directory / f"{artifact}.json"
        path.write_bytes(path.read_bytes().replace(b'saved', b'other'))
    elif target in {"payload", "preserved_timestamp"}:
        path = store.directory / f"{artifact}.npz"
        before = path.stat()
        content = bytearray(path.read_bytes())
        content[len(content) // 2] ^= 1
        path.write_bytes(content)
        if target == "preserved_timestamp":
            os.utime(path, ns=(before.st_atime_ns, before.st_mtime_ns))
            assert path.stat().st_size == before.st_size
            assert path.stat().st_mtime_ns == before.st_mtime_ns
    elif target == "deleted":
        (store.directory / f"{artifact}.npz").unlink()
    else:
        digest = "wrong"
    with pytest.raises(ValueError):
        store.read(artifact, manifest_sha256=digest)


def test_cache_enforces_bytes_entries_and_removal(store):
    first = _saved(store)
    store.MAX_CACHE_ENTRIES = 1
    second = _saved(store)
    assert len(store._read_cache) == 1
    assert next(iter(store._read_cache))[0] == second
    store.read(first, include_projection_source=True)
    assert next(iter(store._read_cache))[0] == first
    store.MAX_CACHE_BYTES = 1
    store.clear_cache()
    store.read(first)
    assert store._cache_bytes == 0 and not store._read_cache
    store.MAX_CACHE_BYTES = 32 * 1024 * 1024
    store.read(first)
    store.remove_unreferenced(first)
    assert store._cache_bytes == 0 and not store._read_cache


def test_concurrent_cold_reads_decode_once(store, monkeypatch):
    artifact = _saved(store)
    store.clear_cache()
    calls = []
    original = store._decode

    def decode(*args, **kwargs):
        calls.append(1)
        return original(*args, **kwargs)

    monkeypatch.setattr(store, "_decode", decode)
    with ThreadPoolExecutor(max_workers=8) as executor:
        results = list(executor.map(lambda _: store.read(artifact), range(16)))
    assert len(calls) == 1
    assert all(value["arrays"]["alpha"] == [.2] for value in results)


def test_cache_byte_budget_counts_numpy_buffers_and_evicts(store):
    first = _saved(store)
    size = store._cache_bytes
    source = store.read(first, manifest_sha256=store.manifest_digest(first), include_projection_source=True)
    assert size >= sum(value.nbytes for field in ("axes", "arrays") for value in source[field].values())
    store.MAX_CACHE_BYTES = size + size // 2
    second = _saved(store)
    assert len(store._read_cache) == 1
    assert next(iter(store._read_cache))[0] == second
    assert store._cache_bytes <= store.MAX_CACHE_BYTES


def test_close_releases_cache_and_prevents_inflight_publication_retention(store):
    artifact = _saved(store)
    store.close()
    assert not store._read_cache and store._cache_bytes == 0
    store.read(artifact)
    _saved(store)
    assert not store._read_cache and store._cache_bytes == 0
