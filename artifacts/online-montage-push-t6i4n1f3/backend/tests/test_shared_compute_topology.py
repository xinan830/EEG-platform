from on_demand_fixtures import configure_api


def test_full_and_on_demand_services_share_the_same_compute_coordinator(tmp_path):
    client, _recording, windows, runs, _root = configure_api(tmp_path)
    try:
        assert runs.compute_coordinator is windows.compute_coordinator
        assert runs.compute_coordinator.snapshot()["max_active"] == 1
    finally:
        windows.close()
        windows._executor.shutdown(wait=True)
        client.close()
