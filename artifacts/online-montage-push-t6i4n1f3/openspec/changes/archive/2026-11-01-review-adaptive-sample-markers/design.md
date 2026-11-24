## Context

The review renderer has two line paths. The bounded `RecordingReviewFrame` contains the already montage-projected display samples and explicit sample counters/gaps. A persistent display level may replace the visible line with a pixel-bucket extrema projection; those extrema are intentionally not a sample-preserving representation. The point layer must therefore consume only the bounded review frame and remain independent of the line projection.

The requested behavior is display-only. Sample values remain V/float64, channel ordering remains the frame's output ordering, and X coordinates remain absolute recording time in seconds. Existing filter, montage, cache identity, recording data, and algorithm inputs do not change.

## Decisions

- Add a local view toggle whose initial state is off. Keep it on the waveform canvas as presentation state; it does not belong in the review session or persisted recording settings.
- Keep each existing extrema-reduced line series. Add a separate marker series per channel populated from finite values in `RecordingReviewFrame.Segments`, using the same baseline and display-scale calculation as the line.
- Compute sample spacing from sampling rate, the current visible time range, and plot width in WPF DIP. Show markers only at spacing >= 5 DIP. Re-evaluate visibility when mode, viewport, plot size, or recording frame changes.
- Offer subsecond review timebase choices (0.1, 0.25, and 0.5 seconds per screen) so users can zoom far enough to see markers at typical EEG sampling rates; the existing paper-speed control remains separate.
- Clip markers through the existing chart axes. Do not create markers for NaN values or sample-counter gaps. The display-level extrema frame never supplies marker values.
- Avoid per-sample Ellipse WPF elements; use SciChart's native point-marker rendering and bounded source frames. At the 5 DIP threshold, the visible marker count is bounded by plot width/5 per channel.

## Risks and Trade-offs

- At zoom levels where markers are hidden, the toggle remains enabled but has no visible dots. A tooltip explains that zooming in reveals individual sample markers.
- A frame may cover more than one viewport. SciChart clips the point series to the visible range; the sample-spacing policy uses the viewport, so markers outside the view do not affect density.
- The 5 DIP criterion is a readability default, not a clinical recommendation. It can be adjusted after usability feedback without changing signal data or scientific semantics.
- SciChart point-marker API compatibility must be verified by building the desktop project and exercising the focused renderer tests.

## Validation

- Test adaptive visibility at, above, and below the 5 DIP boundary.
- Test marker projection against actual frame sample counters, finite values, and gaps; assert that extrema-only display points are not used.
- Build and run focused WPF review tests, strict-validate the OpenSpec change, and run `git diff --check`.
- These checks establish engineering behavior only, not clinical validation.

## Rollback

Remove the toolbar toggle and separate marker-series projection. Existing continuous lines and the display-level cache remain unchanged; no persisted data requires migration.
