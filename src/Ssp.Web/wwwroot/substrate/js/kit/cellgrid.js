import { figure, INK, DIM, NONE } from './lattice.js'
import * as K from './controls.js'

export const N = 11
const blank = () => Array.from({ length: N }, () => Array(N).fill(0))
const copy = (px) => px.map((r) => r.slice())
const norm = (px) =>
  Array.from({ length: N }, (_, y) => Array.from({ length: N }, (_, x) => (px?.[y]?.[x] ? 1 : 0)))

export function path(px) {
  let d = ''
  px.forEach((r, y) => r.forEach((c, x) => c && (d += `M${x} ${y}h1v1h-1z`)))
  return d
}
export const file = (px) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 11 11" shape-rendering="crispEdges"><style>path{fill:#18181b}</style><path d="${path(px)}"/></svg>`

const SAYS = {
  readout: (lit, x, y, ink) =>
    `${String(lit).padStart(3)} of 121 lit · cell ${x}, ${y} ${ink ? 'ink' : 'ground'}`,
  label: (lit, x, y, ink) =>
    `icon, 11 by 11, ${lit} cells lit; cell ${x}, ${y} is ${ink ? 'ink' : 'ground'}`,
}

export function cellgrid(host, pv, opts = {}) {
  const { readout = null, scope = host.parentElement, change = () => {}, says = SAYS } = opts
  let CELL = 12
  let cells = norm(opts.pixels || blank())
  let shown = null
  const undo = []
  let at = [5, 5]
  let gridF = null
  let prevF = []
  let blink = true
  host.textContent = ''

  function draw(first = false) {
    const fits = Math.max(
      6,
      Math.min(12, Math.floor((host.parentElement.clientWidth / K.unit() - 1) / N))
    )
    if (gridF && fits !== CELL) ((gridF = null), (host.textContent = ''))
    CELL = fits
    const S = N * CELL + 1
    if (!gridF) ((gridF = figure(host, S, S)), (first = true))
    gridF.clear()
    for (let i = 0; i <= N; i++)
      for (let p = 0; p < S; p += 2) (gridF.put(i * CELL, p, DIM), gridF.put(p, i * CELL, DIM))
    for (let y = 0; y < N; y++)
      for (let x = 0; x < N; x++)
        if (cells[y][x]) gridF.rect(x * CELL + 1, y * CELL + 1, CELL - 1, CELL - 1, INK)
    if (document.activeElement === host && blink) {
      const [cx, cy] = at
      const t = cells[cy][cx] ? NONE : INK
      for (let i = 2; i < CELL - 1; i++) {
        gridF.put(cx * CELL + i, cy * CELL + 2, t)
        gridF.put(cx * CELL + i, cy * CELL + CELL - 2, t)
        gridF.put(cx * CELL + 2, cy * CELL + i, t)
        gridF.put(cx * CELL + CELL - 2, cy * CELL + i, t)
      }
    }
    gridF.show(first)
    previews()
    const lit = cells.flat().filter(Boolean).length
    const ink = cells[at[1]][at[0]]
    if (readout) readout.textContent = says.readout(lit, at[0] + 1, at[1] + 1, ink)
    host.setAttribute('aria-label', says.label(lit, at[0] + 1, at[1] + 1, ink))
    change(copy(cells))
  }
  function previews() {
    if (!prevF.length) {
      pv.innerHTML = [1, 2, 4, 'dark'].map((s) => `<div class="pv" data-s="${s}"></div>`).join('')
      prevF = K.$$('.pv', pv).map((h) => {
        const dark = h.dataset.s === 'dark'
        const s = dark ? 3 : +h.dataset.s
        return { f: figure(h, N * s + (dark ? 4 : 0), N * s + (dark ? 4 : 0)), s, dark }
      })
    }
    const px = shown || cells
    for (const { f, s, dark } of prevF) {
      f.clear()
      const o = dark ? 2 : 0
      if (dark) f.rect(0, 0, N * s + 4, N * s + 4, INK)
      for (let y = 0; y < N; y++)
        for (let x = 0; x < N; x++)
          if (px[y][x]) f.rect(o + x * s, o + y * s, s, s, dark ? NONE : INK)
      f.show(true)
    }
  }

  const cellAt = (e) => {
    const r = K.$('canvas', host).getBoundingClientRect()
    const x = Math.floor(((e.clientX - r.left) / r.width) * N)
    const y = Math.floor(((e.clientY - r.top) / r.height) * N)
    return x >= 0 && y >= 0 && x < N && y < N ? [x, y] : null
  }
  let ink = null
  host.addEventListener('pointerdown', (e) => {
    const c = cellAt(e)
    if (!c) return
    host.setPointerCapture(e.pointerId)
    undo.push(copy(cells))
    ink = cells[c[1]][c[0]] ? 0 : 1
    cells[c[1]][c[0]] = ink
    at = c
    draw()
  })
  host.addEventListener('pointermove', (e) => {
    if (ink === null) return
    const c = cellAt(e)
    if (c && cells[c[1]][c[0]] !== ink) ((cells[c[1]][c[0]] = ink), (at = c), draw())
  })
  host.addEventListener('pointerup', () => (ink = null))
  host.addEventListener('pointercancel', () => (ink = null))
  host.addEventListener('keydown', (e) => {
    const d = { ArrowRight: [1, 0], ArrowLeft: [-1, 0], ArrowDown: [0, 1], ArrowUp: [0, -1] }[e.key]
    if (d) {
      e.preventDefault()
      at = [Math.max(0, Math.min(N - 1, at[0] + d[0])), Math.max(0, Math.min(N - 1, at[1] + d[1]))]
      blink = true
      return draw()
    }
    if (e.key === ' ' || e.key === 'Enter') {
      e.preventDefault()
      undo.push(copy(cells))
      cells[at[1]][at[0]] = cells[at[1]][at[0]] ? 0 : 1
      return draw()
    }
  })
  let t = 0
  host.addEventListener('focus', () => {
    blink = true
    draw()
    if (!K.still) t = setInterval(() => ((blink = !blink), draw()), 500)
  })
  host.addEventListener('blur', () => (clearInterval(t), draw()))
  document.addEventListener('keydown', (e) => {
    if (e.key !== 'z' || !(e.metaKey || e.ctrlKey) || e.shiftKey) return
    const a = document.activeElement
    if (a !== host && (!a?.closest || !scope.contains(a) || a.matches('input, textarea'))) return
    if (!undo.length) return
    e.preventDefault()
    cells = undo.pop()
    draw()
  })

  const ed = {
    get cells() {
      return copy(cells)
    },
    get at() {
      return at.slice()
    },
    get strokes() {
      return undo.length
    },
    set(px) {
      undo.push(copy(cells))
      cells = norm(px)
      draw()
    },
    edit(fn) {
      ed.set(fn(copy(cells)))
    },
    undo() {
      if (!undo.length) return false
      cells = undo.pop()
      draw()
      return true
    },
    show(px) {
      shown = px ? norm(px) : null
      previews()
    },
    svg: () => file(cells),
    draw: () => draw(),
    redraw() {
      for (const f of [gridF, ...prevF.map((p) => p.f)]) f?.redraw()
    },
  }
  draw(true)
  return ed
}
