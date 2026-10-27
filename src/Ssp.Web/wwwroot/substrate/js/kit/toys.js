import { figure, INK, lit } from './lattice.js'
import * as K from './controls.js'
import {
  DISH,
  CELLS,
  inside,
  rimGrid,
  decode,
  applyDelta,
  randomCell,
  PARAMS,
} from '../shared/slime.js'

export const MAURICE = [
  '....#.....#....',
  '.....#...#.....',
  '......###......',
  '.....#####.....',
  '...#########...',
  '..#....#....#..',
  '##..##.#.##..##',
  '.#..##.#.##..#.',
  '.#.....#.....#.',
  '##.##..#..##.##',
  '..#....#....#..',
  '...#...#...#...',
  '....#######....',
]
export const SPLAT = ['....bb..b.', '.bbbbbbb..', 'bbbbbbbbbb', '.bbbbbbb.b', 'b...bb....']
const S = 2
const MW = MAURICE[0].length * S
const MH = MAURICE.length * S

function sprite(f, rows, x0, y0, on = '#') {
  rows.forEach((r, j) =>
    [...r].forEach((c, i) => {
      if (c !== on) return
      for (let a = 0; a < S; a++) for (let b = 0; b < S; b++) f.put(x0 + i * S + a, y0 + j * S + b)
    })
  )
}

export function maurice(host, p, w) {
  const pad = Object.assign(document.createElement('div'), { className: 'pad maurice' })
  pad.setAttribute('role', 'button')
  pad.tabIndex = 0
  const t = Object.assign(document.createElement('p'), { className: 't' })
  host.append(pad, t)
  const f = figure(pad, MW, MH)
  let until = 0
  let timer = 0
  const squished = () => until > p.now()
  function draw(first = false) {
    f.clear()
    const foot = f.rows
    if (squished()) sprite(f, SPLAT, (MW - SPLAT[0].length * S) / 2, foot - SPLAT.length * S, 'b')
    else sprite(f, MAURICE, 0, foot - MH)
    f.show(first)
    pad.setAttribute('aria-label', squished() ? w.squishedLabel : w.label)
    pad.setAttribute('aria-pressed', squished())
    K.swap(t, squished() ? w.squished : w.name)
  }
  function set(u) {
    until = u
    clearTimeout(timer)
    if (squished()) timer = setTimeout(() => draw(), Math.max(0, until - p.now()) + 20)
    draw()
  }
  p.onSquish(set)
  const press = () => {
    if (squished() || !p.mayAct()) return
    if (p.squish()) set(p.now() + 5000)
  }
  pad.addEventListener('pointerdown', (e) => (e.preventDefault(), press()))
  pad.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' && e.key !== ' ') return
    e.preventDefault()
    press()
  })
  K.draws.add(() => f.redraw())
  draw(true)
  return { pad, squished }
}

const RIM = rimGrid()
const LEVEL = [0, 4, 8, 16]

export function dish(host, p, w) {
  const pad = Object.assign(document.createElement('div'), { className: 'pad dish' })
  pad.setAttribute('role', 'button')
  pad.tabIndex = 0
  pad.setAttribute('aria-label', w.label)
  const t = Object.assign(document.createElement('p'), { className: 't' })
  host.append(pad, t)
  const f = figure(pad, DISH, DISH)
  let tones = null
  let food = []
  let tick = -1
  let fedT = 0
  function draw() {
    f.clear()
    for (let y = 0; y < DISH; y++) for (let x = 0; x < DISH; x++) if (RIM[y][x]) f.put(x, y, INK)
    if (tones)
      for (let k = 0; k < CELLS.length; k++) {
        const v = tones[k]
        if (!v) continue
        const x = CELLS[k] % DISH
        const y = (CELLS[k] - x) / DISH
        if (lit(x, y, LEVEL[v])) f.put(x, y, INK)
      }
    for (let k = 0; k < food.length; k += 2) f.put(food[k], food[k + 1], INK)
    f.show(true)
  }
  p.onSlime((m) => {
    if (typeof m.g === 'string') {
      const d = decode(m.g, m.f)
      if (!d) return
      ;({ tones, food } = d)
      if (Number.isInteger(m.t)) tick = m.t
      return draw()
    }
    if (!tones || m.b !== tick) return p.watch(true, true)
    const tn = applyDelta(tones, m.d)
    if (!tn) return p.watch(true, true)
    tones = tn
    tick = m.t
    if (Array.isArray(m.f) && m.f.length % 2 === 0 && m.f.every(Number.isInteger)) {
      const next = []
      for (let k = 0; k < m.f.length; k += 2)
        if (inside(m.f[k], m.f[k + 1])) next.push(m.f[k], m.f[k + 1])
      food = next
    }
    draw()
  })
  let seen = false
  const say = () => p.watch(seen && !document.hidden)
  new IntersectionObserver(([e]) => ((seen = e.isIntersecting), say())).observe(pad)
  document.addEventListener('visibilitychange', say)
  function drop(x, y) {
    if (!inside(x, y) || !p.mayAct() || !p.feed(x, y)) return
    const r = PARAMS.pelletRadius
    for (let dy = -r; dy <= r; dy++)
      for (let dx = -r; dx <= r; dx++)
        if (dx * dx + dy * dy <= r * r + 1 && inside(x + dx, y + dy)) food.push(x + dx, y + dy)
    draw()
    K.swap(t, w.fed)
    clearTimeout(fedT)
    fedT = setTimeout(() => K.swap(t, w.name), 1600)
  }
  pad.addEventListener('pointerdown', (e) => {
    e.preventDefault()
    const r = pad.getBoundingClientRect()
    drop(
      Math.floor(((e.clientX - r.left) / r.width) * DISH),
      Math.floor(((e.clientY - r.top) / r.height) * DISH)
    )
  })
  pad.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' && e.key !== ' ') return
    e.preventDefault()
    const [x, y] = randomCell(Math.random)
    drop(x, y)
  })
  K.draws.add(() => f.redraw())
  K.swap(t, w.name)
  draw()
  return { pad }
}
