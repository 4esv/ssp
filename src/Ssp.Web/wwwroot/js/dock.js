// Gives the width and the height of an element in pixels. A splitter drag needs them to turn pixels into fractions.
export function size(element) {
    const rect = element.getBoundingClientRect();
    return [rect.width, rect.height];
}

// Sets drag data on a tab. Firefox starts no drag without it.
export function allowDrag(element) {
    element.addEventListener("dragstart", e => {
        const tab = e.target.closest?.(".dock-tab");
        if (tab) {
            e.dataTransfer.setData("text/plain", tab.dataset.panel);
            e.dataTransfer.effectAllowed = "move";
        }
    });
}
