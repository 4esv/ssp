// Stops the browser acting on the editor shortcut keys (Ctrl+K, Ctrl+Z, ...). The table comes from Shortcuts.All.
export function typing() {
    const el = document.activeElement;
    return !!el && (el.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName));
}

let handler;

export function guard(table) {
    if (handler) document.removeEventListener("keydown", handler);
    handler = e => {
        const mod = e.ctrlKey || e.metaKey;
        const hit = table.find(s => s.key.toLowerCase() === e.key.toLowerCase()
            && (s.bare ? !mod && !e.altKey : mod && !e.altKey && e.shiftKey === s.shift));
        // NOTE: A field keeps its own undo and its own ? key.
        if (hit && !((hit.bare || hit.plain) && typing())) e.preventDefault();
    };
    document.addEventListener("keydown", handler);
}
