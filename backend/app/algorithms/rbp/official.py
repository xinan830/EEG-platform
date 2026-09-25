"""Official RBP band contract used by the Runtime-facing module."""

RBP_BANDS = (
    ("delta", 1.0, 4.0),
    ("theta", 4.0, 8.0),
    ("alpha", 8.0, 13.0),
    ("beta", 13.0, 30.0),
)

__all__ = ["RBP_BANDS"]
