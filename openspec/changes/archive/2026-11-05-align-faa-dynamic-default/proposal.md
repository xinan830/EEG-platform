# Align Official FAA Dynamic Window Default

Official FAA declares a 20-second dynamic window, but Official Run requests that omit the window inherit the shared 10-second model default. Since FAA requires at least 10 clean 2-second epochs with a 1-second step, a 10-second window cannot pass that gate. Align the Official Run default with the FAA manifest while preserving explicit caller overrides.

The separate minimum-clean-epoch-ratio question is intentionally deferred until a threshold is selected and validated.
