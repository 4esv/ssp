// Pointer helpers for the schematic editor. index.html loads this module at start. Blazor has no pointer capture, so it keeps the pointer events on the part or palette entry that
// started a drag, also when the pointer leaves it.
document.addEventListener("pointerdown", e => {
    const target = e.target.closest?.(".palette-item, .schematic-pins .part, .schematic-pins .knob") ?? (e.target.matches?.("svg.schematic-pins") ? e.target : null);
    if (target) target.setPointerCapture(e.pointerId);
});

// The pane box in client pixels: left, top, width, height. The box is the padding box, which is where the stage is positioned.
export function paneBox(pane) {
    const rect = pane.getBoundingClientRect();
    return [rect.left + pane.clientLeft, rect.top + pane.clientTop, pane.clientWidth, pane.clientHeight];
}
