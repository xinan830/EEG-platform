"""Compatibility facade for the migrated official IAPF implementation."""

from app.algorithms.iapf.official import IAPFEstimate, estimate_iapf

__all__ = ["IAPFEstimate", "estimate_iapf"]
