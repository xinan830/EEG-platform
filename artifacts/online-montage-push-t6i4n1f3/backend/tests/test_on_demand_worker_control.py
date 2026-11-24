from app.services.on_demand_worker import WindowExecutionControl


class _Lease:
    waited_s = 0.0

    def __init__(self, events):
        self._events = events

    def release(self):
        self._events.append("release")


class _Coordinator:
    def __init__(self, events):
        self._events = events

    def acquire(self, lane, _check):
        self._events.append(("acquire", lane))
        return _Lease(self._events)


def test_on_demand_compute_slot_starts_after_source_preparation():
    events = []
    control = WindowExecutionControl(
        lambda: events.append("check"),
        lambda: events.append("prepared"),
        compute_coordinator=_Coordinator(events),
    )

    control.window_started()
    assert ("acquire", "interactive") not in events

    control.source_preparation_completed()
    assert events[-2:] == [("acquire", "interactive"), "prepared"]

    control.close()
    assert events[-1] == "release"
