# Design

Use the existing `ProjectWorkspaceViewModel` as the source of project and local
recording selection. Only a record marked completed can be registered. The
backend registration response supplies the channel list and duration; WPF
does not infer them from montage labels or the table row.

The PSD submission form maps explicit channel and requested start/end seconds
to the existing official Run contract. Validate basic finite/in-range input
locally for usability; the backend remains authoritative for scientific
constraints. A non-PSD catalog selection must not silently fall back to PSD.

The result view consumes the bounded structured-preview response. Plotting
maps the returned frequency and PSD values to screen coordinates only; it
does not run FFT, integrate bands, convert to dB, or turn null cells into zero.
