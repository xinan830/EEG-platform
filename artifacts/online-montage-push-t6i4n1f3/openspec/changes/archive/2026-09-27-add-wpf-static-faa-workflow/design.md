# Design

Reuse the WPF recording registration and static Run pipeline. The primary
channel selector is the F3 source; FAA adds an F4 selector visible only when
FAA is selected. Both channels must exist in the registered recording and be
different. No client-side Alpha calculation is introduced.
