from on_demand_fixtures import configure_api, psd_config


def test_ordered_tnk_delivery_is_opt_in_and_reports_unsupported_algorithms(tmp_path):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    try:
        base = {
            "recording_id": recording.id,
            "algorithm_id": "psd",
            "config": psd_config(recording),
            "cursor_sample": 1200,
        }
        ordinary = client.post("/api/analysis-windows/ensure", json=base)
        opted_in = client.post("/api/analysis-windows/ensure", json={
            **base,
            "playback_delivery": True,
            "playback_delivery_id": "playback-test",
        })

        assert ordinary.status_code in {200, 202}
        assert "playback_delivery" not in ordinary.json()
        assert opted_in.status_code in {200, 202}
        assert opted_in.json()["playback_delivery"] == {
            "state": "unsupported", "code": "TNK_PLAYBACK_UNSUPPORTED",
        }
        unavailable = client.get("/api/analysis-windows/playback/playback-test/events")
        assert unavailable.status_code == 404
        assert unavailable.json()["code"] == "TNK_PLAYBACK_DELIVERY_NOT_FOUND"

        # Unsupported opt-in does not allocate a session buffer; closing it is
        # idempotent for an unknown delivery and does not affect other sessions.
        closed = client.delete("/api/analysis-windows/playback/playback-test/events")
        assert closed.status_code == 200
        assert closed.json() == {"delivery_id": "playback-test", "state": "closed"}
    finally:
        windows.close(wait=True)
