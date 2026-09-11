import numpy as np

from app.services.filter_checkpoint_cache import FilterCheckpointCache


def _state(value: float):
    return (np.array([value]), None, [np.array([value])])


def test_checkpoint_cache_returns_nearest_earlier_state():
    cache = FilterCheckpointCache()
    key = ("recording-1", 500.0, 0.5, 70.0, None, ("Fz",))
    cache.put(key, 5000, _state(1.0))
    cache.put(key, 10000, _state(2.0))
    sample, state = cache.nearest(key, 7500)
    assert sample == 5000
    assert state is not None and state[0][0] == 1.0


def test_checkpoint_cache_limits_groups_and_checkpoints():
    cache = FilterCheckpointCache(max_groups=1, max_checkpoints_per_group=2)
    first = ("recording-1",)
    second = ("recording-2",)
    cache.put(first, 10, _state(1.0))
    cache.put(first, 20, _state(2.0))
    cache.put(first, 30, _state(3.0))
    assert cache.nearest(first, 15) == (0, None)
    cache.put(second, 10, _state(4.0))
    assert cache.nearest(first, 100) == (0, None)
