# Design

## Boundaries

Shared instrument resources own palette, field templates, tool buttons and an
opt-in EegWorkspaceShell template. Pages own menus and command bindings. An
attached presentation slot exposes each page's menu to EegSessionWindow, which
binds the menu to the page's DataContext rather than the owner's workspace.
The window uses WindowChrome for caption dragging/resizing and SystemCommands
for window buttons. Closing still goes through the existing confirmation and
session cleanup code.

Acquisition keeps its runtime permissions and selected immutable montage; review
keeps its compatible montage selector, analysis settings, event actions, time
selector, and timeline events. Parameter strips scroll horizontally when needed;
they do not wrap into taller rows or silently remove parameter controls.

## Alternatives

A global restyle was rejected because it would change unrelated dialogs and
algorithm views. A shared acquisition/review ViewModel was rejected because it
would merge independent state machines. Copying HTML controls was rejected:
the preview is not a scientific or functional implementation contract.

## Verification

Test field binding inheritance, shell layout at 1000 and 1600 pixels, menu
context transfer, and preservation of page command/binding declarations.
Build Release, run targeted recording/acquisition regression tests, validate
OpenSpec and file-size policy, and check whitespace. Native hardware and
interactive window/GPU acceptance remain separate from unit tests.
