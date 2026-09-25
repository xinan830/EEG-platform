"""Shared scientific quality gates."""

from .spectral import SpectralQualityGateError, WindowQuality, evaluate_spectral_window

__all__ = ["SpectralQualityGateError", "WindowQuality", "evaluate_spectral_window"]
