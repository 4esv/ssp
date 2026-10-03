// Gives the width and the height of an element in pixels. A splitter drag needs them to turn pixels into fractions.
export function size(element) {
    const rect = element.getBoundingClientRect();
    return [rect.width, rect.height];
}
