// Pointer helpers for the schematic editor. index.html loads this module at start. Blazor has no pointer capture, so it keeps the pointer events on the part or palette entry that
// started a drag, also when the pointer leaves it.
document.addEventListener("pointerdown", e => {
    const target = e.target.closest?.(".palette-item, .schematic-pins .part");
    if (target) target.setPointerCapture(e.pointerId);
});

// The point of a client position in the units of the view box, and whether the point is inside the canvas.
export function toSvg(svg, clientX, clientY) {
    const rect = svg.getBoundingClientRect();
    const inside = clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom;
    // NOTE: The empty canvas is a plain element. A point on it is in pixels.
    if (!svg.createSVGPoint) return [clientX - rect.left, clientY - rect.top, inside ? 1 : 0];
    const point = svg.createSVGPoint();
    point.x = clientX;
    point.y = clientY;
    const at = point.matrixTransform(svg.getScreenCTM().inverse());
    return [at.x, at.y, inside ? 1 : 0];
}

// Units of the view box for one client pixel.
export function unitsPerPixel(svg) {
    return 1 / svg.getScreenCTM().a;
}
