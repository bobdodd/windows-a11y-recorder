// The slice 4h external module of the timer origin fixture. See
// docs/architecture/page-recreation.md, "Slice 4h: the page's script source".
export const scheduled = setTimeout(function fromExternalModule() {}, 600000);
