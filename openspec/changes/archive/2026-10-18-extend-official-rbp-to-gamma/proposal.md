# Extend official RBP to Gamma

## Why

Official RBP currently normalizes Delta, Theta, Alpha and Beta over 1–30 Hz, while the PSD band-share view reports five bands over 1–50 Hz. The product now requires Gamma (30–50 Hz) in official RBP. Merely adding a fifth chart item would misrepresent the old four-band denominator.

## What Changes

- Publish a new five-band RBP scientific contract and Definition version `2.0.1`; leave historical four-band Runs and Definition version `1.0.0` intact. Preserve the already-published development `2.0.0` Definition rather than mutating immutable provenance.
- Calculate static and dynamic Delta/Theta/Alpha/Beta/Gamma powers from one 1–50 Hz PSD and divide each by their five-band sum.
- Record the actual filter, notch, band boundaries, denominator, source quality and intermediate band powers.
- Show five backend-authored shares and trend series in WPF; retain correct four-band labels for historical Runs.

## Impact

- Existing four shares will change under the new denominator. The output shape changes from four to five values; caches and Definition identity must not be reused.
- 50 Hz analysis requires Nyquist frequency strictly above 50 Hz. Gamma is particularly sensitive to EMG, mains interference and filtering at its upper edge. The output is a measured frequency-band share, not a clinical inference.
- Other algorithms and the legacy 1–30 Hz spectral service remain unchanged.
