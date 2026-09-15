"""Compatibility facade for official algorithm shadow validation imports."""

from app.eeg_core.official_algorithms.validation import (
    shadow_brainbeat,
    shadow_brainbeat_ema,
    shadow_faa,
    shadow_iapf,
    shadow_rbp,
    shadow_theta_beta,
)

__all__ = ["shadow_rbp", "shadow_faa", "shadow_brainbeat", "shadow_brainbeat_ema", "shadow_theta_beta", "shadow_iapf"]
