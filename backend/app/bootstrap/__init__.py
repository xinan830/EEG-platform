"""Application composition roots.

Concrete built-in algorithms are wired here so the generic Runtime remains
independent of the official algorithm packages.
"""

from .builtin_registry import build_builtin_registry

__all__ = ["build_builtin_registry"]
