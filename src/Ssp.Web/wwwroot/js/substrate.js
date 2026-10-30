// The titles are drawn as cells by the vendored substrate (wwwroot/substrate, see its README). Blazor renders into
// #app after this module runs, and it renders again on navigation and on every editor change, so a MutationObserver
// on #app calls layout() once per frame after the DOM changes. Without JS, or when a face does not load, the titles
// stay as text. Pixelta Cells is not vendored (see VendoredSubstrateTests), so the faces are the two Handjet faces only.
import { mountCells, defaultPixel } from '../substrate/js/cells.js'
import * as prose from '../substrate/js/shared/faces/prose.js'
import * as mono from '../substrate/js/shared/faces/mono.js'
import { ditherFor } from '../substrate/js/kit/dither.js'

const app = document.getElementById('app')

// NOTE: A title is drawn only where the user can see it: it has a box, and no dialog or floating panel covers it.
// Points outside the viewport are not tested, so a title below the fold of a long page is still drawn.
function shown(el) {
    const r = el.getBoundingClientRect()
    if (r.width < 2 || r.height < 2) return false
    const y = r.top + r.height / 2
    for (const x of [r.left + 2, r.right - 2]) {
        if (x < 0 || y < 0 || x >= innerWidth || y >= innerHeight) continue
        const hit = document.elementFromPoint(x, y)
        if (hit && hit !== el && !el.contains(hit)) return false
    }
    return true
}

const titles = () => [...app.querySelectorAll('h1, h2')].filter(shown)

// NOTE: The kit draws the pixel of a small screen at three device pixels. On a phone at a ratio of 1 or 2 that is three
// or one and a half CSS pixels, and the controls then leave the schematic too little of the screen. So the pixel is
// never more device pixels than the ratio, rounded: one CSS pixel on a phone, and the kit's own pixel on a desk.
const pixel = Math.min(defaultPixel(), Math.max(1, Math.round(window.devicePixelRatio || 1)))
ditherFor(pixel)
const FACES = { 'Handjet Cells': prose, 'Handjet Cells Mono': mono }
const cells = mountCells(app, { faces: FACES, scope: titles, reach: 6 })
if (cells.pixel !== pixel) cells.pixel = pixel
const state = { layouts: 0 }
window.sspCells = state

let queued = false
function relayout() {
    if (queued) return
    queued = true
    requestAnimationFrame(() => {
        queued = false
        state.layouts++
        Promise.resolve(cells.layout()).then(() => ditherFor(cells.pixel))
    })
}

// NOTE: The schematic SVG changes on every drag, and the cells layer sets classes on the titles, so neither starts a
// layout. A panel that opens changes its hidden attribute or its inline style.
const ignored = (node) => {
    const el = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement
    return !el || el === app || el.closest('svg') !== null
}
new MutationObserver((records) => {
    if (records.some((r) => !ignored(r.target))) relayout()
}).observe(app, {
    subtree: true,
    childList: true,
    characterData: true,
    attributes: true,
    attributeFilter: ['hidden', 'style', 'open', 'aria-expanded', 'data-sheet'],
})
addEventListener('popstate', relayout)
relayout()
