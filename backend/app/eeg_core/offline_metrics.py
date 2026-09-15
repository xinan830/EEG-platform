"""Compatibility facade for frozen IAPF and three-channel official metrics."""

from app.eeg_core.official_algorithms.iapf import IAPFEstimate, estimate_iapf
from app.eeg_core.official_algorithms.theta_beta import metric_values

__all__ = ["IAPFEstimate", "estimate_iapf", "metric_values"]
