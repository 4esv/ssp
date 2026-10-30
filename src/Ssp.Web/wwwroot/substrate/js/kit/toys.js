import * as K from './controls.js'
import { DISH, inside, rimGrid, decode, applyDelta, paintDish, PARAMS } from '../shared/slime.js'
import { EMPTY, lineOf, tiltOf } from '../shared/playground.js'

const SVG = 'http://www.w3.org/2000/svg'
const el = (tag, cls, attrs = {}) => {
  const e = Object.assign(document.createElement(tag), { className: cls })
  for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v)
  return e
}
function pressable(n, label, act) {
  n.setAttribute('role', 'button')
  n.tabIndex = 0
  n.setAttribute('aria-label', label)
  n.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' && e.key !== ' ') return
    e.preventDefault()
    act()
  })
}
function cellsPath(rows, on = '#') {
  let d = ''
  rows.forEach((r, y) =>
    [...r].forEach((c, x) => {
      if (c === on) d += `M${x} ${y}h1v1h-1z`
    })
  )
  return d
}
function sprite(rows, cls, on) {
  const s = document.createElementNS(SVG, 'svg')
  s.setAttribute('class', cls)
  s.setAttribute('viewBox', `0 0 ${rows[0].length} ${rows.length}`)
  s.setAttribute('shape-rendering', 'crispEdges')
  s.setAttribute('aria-hidden', 'true')
  s.setAttribute('focusable', 'false')
  const p = document.createElementNS(SVG, 'path')
  p.setAttribute('fill', 'currentColor')
  p.setAttribute('d', cellsPath(rows, on))
  s.append(p)
  return s
}
function names(text) {
  const n = el('div', 'name')
  for (const line of String(text ?? '').split('\n'))
    n.append(Object.assign(document.createElement('span'), { textContent: line }))
  return n
}

const MAURICE_PNG =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAMCAYAAABr5z2BAAABmElEQVR42qWSS0ubQRSGs+vKFjU68813zVVjjNCWgFo1pZV0oaggVFC76VaE4H9w00W7cdsuWippk/ph0oaKBrzFKkj0L/Qn9Bc8HbMRb1HwXQ2cc5553zkTCDTR8rMMuZfPOZ6a5s/4JF9GRgjcpqV0P0eZV9QjYeq2oNYTYi1l40cM1h2BnwjzKRa+HvRzeo5i4jFvW1somA/ZSHXz2RKsWpK80cFXU/DdNFhVHXybGrsKYbeKkp0IIShmMpws5vhXKWMbQVaicfKqnQ1LUTEkP2zvImB4oJ+Q7XBYq7IQc9lSis0Xo9RmZnkdbGU/2sWOqSjbJuuRHrbcSzG6oxFcZVHJf6TkhSgpA1/b/aWkPgvKpqTqKI5jHkPqER/Sg+eAZDyOowdsKXna2UZBWyzqvL4hWJNBKhq07brsWRb7OsJKInnV/vt3y6SSXYSk0LeqxtChjvSk5QGl3iQ1bf3Acyn1pW5e5enJQaP41y+w6XmNx/rtGFT1JrYHB3ijHZ3VJyfGb/8PZ5qfnbtbYzNls9n7Q5rpP7rdxhSgrySsAAAAAElFTkSuQmCC'
export const SPLAT = ['....bb..b.', '.bbbbbbb..', 'bbbbbbbbbb', '.bbbbbbb.b', 'b...bb....']

export function maurice(host, p, w) {
  const n = el('div', 'toy maurice')
  const mark = el('div', 'mark')
  const bug = el('img', 'bug', { src: MAURICE_PNG, width: 16, height: 12, alt: '' })
  mark.append(bug, sprite(SPLAT, 'splat', 'b'))
  n.append(mark, names(w.name))
  host.append(n)
  let until = 0
  let timer = 0
  const squished = () => until > p.now()
  function draw() {
    const s = squished()
    n.classList.toggle('squished', s)
    n.setAttribute('aria-label', s ? w.squishedLabel : w.label)
    n.setAttribute('aria-pressed', String(s))
  }
  function set(u) {
    until = u
    clearTimeout(timer)
    if (squished()) timer = setTimeout(draw, Math.max(0, until - p.now()) + 20)
    draw()
  }
  p.onSquish(set)
  const press = () => {
    if (squished() || !p.mayAct()) return
    if (p.squish()) set(p.now() + 5000)
  }
  pressable(n, w.label, press)
  n.addEventListener('click', press)
  draw()
  return { n, squished }
}

const RIM = rimGrid()
function paintRim(rim) {
  const g = rim.getContext('2d')
  g.clearRect(0, 0, DISH, DISH)
  g.fillStyle = K.tone('--ink')
  RIM.forEach((r, y) => r.forEach((c, x) => c && g.fillRect(x, y, 1, 1)))
}

export function dish(host, p, w) {
  const n = el('div', 'toy dish')
  const cv = el('canvas', 'mould', { width: DISH, height: DISH })
  const rim = el('canvas', 'rim', { width: DISH, height: DISH })
  n.append(cv, rim, names(w.name))
  host.append(n)
  paintRim(rim)
  K.draws.add(() => paintRim(rim))
  let tones = null
  let food = []
  let tick = -1
  let painted = false
  let minute = null
  function paint(prev = null, prevFood = []) {
    if (!tones) return
    if (!painted) prev = null
    if (K.still) {
      const m = Math.floor(p.now() / 60000)
      if (m === minute) return
      minute = m
      prev = null
    }
    const ink = ['', K.tone('--pg-mid'), K.tone('--pg-mute'), K.tone('--ink')]
    paintDish(cv.getContext('2d'), tones, food, ink, K.tone('--pg-brick'), prev, prevFood)
    painted = true
  }
  K.draws.add(() => ((painted = false), (minute = null), paint()))
  cv.addEventListener('contextrestored', () => ((painted = false), paint()))
  let away = false
  const asleep = (on) => n.toggleAttribute('data-asleep', on)
  p.onWatch?.((on) => {
    away = !on
    if (!on) asleep(true)
  })
  p.onSlime((m) => {
    if (typeof m.g === 'string') {
      const d = decode(m.g, m.f)
      if (!d) return
      ;({ tones, food } = d)
      if (Number.isInteger(m.t)) tick = m.t
      painted = false
      paint()
      if (!away) asleep(false)
      return
    }
    if (!tones || m.b !== tick) return p.watch(true, true)
    const tn = applyDelta(tones, m.d)
    if (!tn) return p.watch(true, true)
    const prev = tones
    const prevFood = food
    tones = tn
    tick = m.t
    if (Array.isArray(m.f) && m.f.length % 2 === 0 && m.f.every(Number.isInteger)) {
      const next = []
      for (let k = 0; k < m.f.length; k += 2)
        if (inside(m.f[k], m.f[k + 1])) next.push(m.f[k], m.f[k + 1])
      food = next
    }
    paint(prev, prevFood)
  })
  let seen = false
  const say = () => p.watch(seen && !document.hidden)
  new IntersectionObserver(([e]) => ((seen = e.isIntersecting), say())).observe(cv)
  document.addEventListener('visibilitychange', say)
  function drop(x, y) {
    if (!inside(x, y) || !p.mayAct() || !p.feed(x, y)) return
    const r = PARAMS.pelletRadius
    const was = food
    food = food.slice()
    for (let dy = -r; dy <= r; dy++)
      for (let dx = -r; dx <= r; dx++)
        if (dx * dx + dy * dy <= r * r + 1 && inside(x + dx, y + dy)) food.push(x + dx, y + dy)
    minute = null
    paint(tones, was)
  }
  cv.addEventListener('click', (e) => {
    const r = cv.getBoundingClientRect()
    if (!r.width || !r.height) return
    drop(
      Math.floor(((e.clientX - r.left) / r.width) * DISH),
      Math.floor(((e.clientY - r.top) / r.height) * DISH)
    )
  })
  pressable(n, w.label, () => drop(DISH >> 1, DISH >> 1))
  p.onSpectate?.((off) => n.setAttribute('aria-disabled', String(off)))
  return { n }
}

const SW = 96
const SH = 36
const PIVOT = [SW / 2, 24]
const HALF = 44
const TILT = 0.2
const RIDER = 4

export function seesaw(host, p, w) {
  const n = el('div', 'toy seesaw')
  const cv = el('canvas', 'plank', { width: SW, height: SH })
  const ends = [-1, 1].map((s) => {
    const b = el('button', `end ${s < 0 ? 'l' : 'r'}`, { type: 'button' })
    b.setAttribute('aria-label', s < 0 ? w.left : w.right)
    b.setAttribute('aria-pressed', 'false')
    b.dataset.s = s
    return b
  })
  n.append(cv, ...ends, names(w.name))
  host.append(n)
  let l = 0
  let r = 0
  let mine = 0
  let shown = null
  let raf = 0
  const target = () => (K.still && l === r ? 0 : tiltOf(l, r, p.now()))
  function draw() {
    raf = 0
    n.dataset.riders = `${l},${r}`
    const g = cv.getContext('2d')
    g.clearRect(0, 0, SW, SH)
    const t = target()
    shown = shown === null || K.still ? t : shown + (t - shown) * 0.18
    const a = shown * TILT
    const [px, py] = PIVOT
    const cos = Math.cos(a)
    const sin = Math.sin(a)
    const ink = K.tone('--ink')
    g.fillStyle = K.tone('--pg-mute')
    for (let j = 0; j <= SH - py - 2; j++) g.fillRect(px - 1 - j, py + 1 + j, 2 + 2 * j, 1)
    g.fillStyle = ink
    for (let k = -HALF; k <= HALF; k += 0.5)
      for (const d of [0, -1]) {
        const x = Math.round(px + k * cos - d * sin)
        const y = Math.round(py + k * sin + d * cos)
        g.fillRect(x, y - 1, 1, 1)
      }
    for (const [s, c] of [
      [-1, l],
      [1, r],
    ])
      for (let i = 0; i < Math.min(c, 6); i++) {
        const k = s * (HALF - 3 - i * (RIDER + 1))
        const x = Math.round(px + k * cos + 2 * sin)
        const y = Math.round(py + k * sin - 2 * cos)
        g.fillStyle = mine === s && i === 0 ? K.tone('--pg-brick') : ink
        g.fillRect(x - RIDER / 2, y - RIDER - 1, RIDER, RIDER)
      }
    if (!K.still && (l === r ? l > 0 : Math.abs(shown - t) > 0.002))
      raf = requestAnimationFrame(draw)
  }
  const again = () => raf || (raf = requestAnimationFrame(draw))
  p.onSeesaw?.((nl, nr) => {
    l = nl
    r = nr
    if (!mine) for (const b of ends) b.setAttribute('aria-pressed', 'false')
    again()
  })
  for (const b of ends)
    b.addEventListener('click', () => {
      if (!p.mayAct()) return
      const s = Number(b.dataset.s)
      const next = mine === s ? 0 : s
      if (!p.sit(next)) return
      if (mine < 0) l = Math.max(0, l - 1)
      if (mine > 0) r = Math.max(0, r - 1)
      if (next < 0) l++
      if (next > 0) r++
      mine = next
      for (const e of ends) e.setAttribute('aria-pressed', String(Number(e.dataset.s) === mine))
      again()
    })
  K.draws.add(again)
  draw()
  return { n, seat: () => mine }
}

const MARKS = {
  x: ['#.....#', '.#...#.', '..#.#..', '...#...', '..#.#..', '.#...#.', '#.....#'],
  o: ['..###..', '.#...#.', '#.....#', '#.....#', '#.....#', '.#...#.', '..###..'],
}

export function ttt(host, p, w) {
  const n = el('div', 'toy ttt')
  const grid = el('div', 'board', { role: 'grid', 'aria-label': w.name })
  const squares = []
  for (let i = 0; i < 9; i++) {
    const b = el('button', 'sq', { type: 'button' })
    b.dataset.i = i
    squares.push(b)
    grid.append(b)
  }
  n.append(grid, names(w.name))
  host.append(n)
  let board = EMPTY
  let won = ''
  function draw() {
    const line = lineOf(board) || []
    n.toggleAttribute('data-won', !!won)
    squares.forEach((b, i) => {
      const m = board[i]
      b.replaceChildren()
      if (m === 'x' || m === 'o') b.append(sprite(MARKS[m], 'mk'))
      b.classList.toggle('line', line.includes(i))
      b.setAttribute('aria-label', m === 'x' || m === 'o' ? m : String(i + 1))
      b.disabled = m !== '.' || !!won
    })
  }
  p.onTtt?.((b, wn) => {
    board = b
    won = wn
    draw()
  })
  grid.addEventListener('click', (e) => {
    const b = e.target.closest('.sq')
    if (!b || b.disabled || !p.mayAct()) return
    p.mark(Number(b.dataset.i))
  })
  draw()
  return { n }
}
