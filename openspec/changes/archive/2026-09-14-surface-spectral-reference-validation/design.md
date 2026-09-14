## Design

`ResultsDrawer` receives the viewer's active analysis range and selected source
channel order. It calls the existing backend endpoint and displays returned
engineering evidence without recalculation. Export URLs include the returned
validation ID only when a run is selected.
