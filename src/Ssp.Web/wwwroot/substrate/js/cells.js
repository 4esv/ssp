import { createWorld, kick, burst, tick, place as setDown } from './scatter.js'

const TPS = 24
const INK = 2
const DIM = 1
const MARGIN = 48
const CHART_H = 11
const CEIL = 0.06
const BLOCK = 24
const AIR = 10
const SCATTER = 24
const WANDER = 3
const BLOB = 6
const PART = 16
const TRAIL = 4
const FRAY = 2
const FRAYED = 3
export const pickPixel = (short, ratio) => (short < 600 ? 3 : ratio < 1.5 ? 1 : 2)
export const defaultPixel = () =>
  pickPixel(Math.min(window.screen.width, window.screen.height), window.devicePixelRatio || 1)

function fit(want, ratio) {
  let best = 0
  for (let n = Math.max(1, Math.floor(want / 1.5)); n <= Math.ceil(want * 1.5); n++) {
    const half = (2 * n) / ratio
    if (
      Math.abs(half - Math.round(half)) < 1e-6 &&
      (!best || Math.abs(n - want) <= Math.abs(best - want))
    )
      best = n
  }
  return best || Math.max(1, Math.round(want))
}

export const CAP = { side: 8192, area: 16777216 }
const whole = (limit, dpr) => {
  const most = Math.max(1, Math.floor(limit / dpr))
  for (let t = most; t > 0 && t > most - 1000; t--)
    if (Math.abs(t * dpr - Math.round(t * dpr)) < 1e-6) return t
  return most
}
export function tiling(w, h, dpr, cap = CAP) {
  const W = Math.round(w * dpr)
  const H = Math.round(h * dpr)
  const tw = W <= cap.side ? w : whole(cap.side, dpr)
  const TW = W <= cap.side ? W : Math.round(tw * dpr)
  const room = Math.min(cap.side, Math.floor(cap.area / Math.max(1, TW)))
  const th = H <= room ? h : whole(room, dpr)
  const TH = H <= room ? H : Math.round(th * dpr)
  const nx = Math.max(1, Math.ceil(W / Math.max(1, TW)))
  const ny = Math.max(1, Math.ceil(H / Math.max(1, TH)))
  return { w, h, W, H, tw, th, TW, TH, nx, ny }
}
export function tileOf(p, k) {
  const i = k % p.nx
  const j = Math.floor(k / p.nx)
  return {
    left: i * p.tw,
    top: j * p.th,
    width: i < p.nx - 1 ? p.tw : p.w - i * p.tw,
    height: j < p.ny - 1 ? p.th : p.h - j * p.th,
    ox: i * p.TW,
    oy: j * p.TH,
    dw: Math.min(p.TW, p.W - i * p.TW),
    dh: Math.min(p.TH, p.H - j * p.TH),
  }
}

export const SOLID = 'svg, img, .diagram'
export function inkRects(root, solid = SOLID) {
  const out = []
  const range = document.createRange()
  const walk = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
  for (let n = walk.nextNode(); n; n = walk.nextNode()) {
    if (!n.data.trim()) continue
    range.selectNodeContents(n)
    for (const r of range.getClientRects())
      if (r.width && r.height) out.push([r, n.parentElement, true])
  }
  for (const el of document.querySelectorAll(solid || SOLID))
    for (const r of el.getClientRects()) if (r.width && r.height) out.push([r, el, false])
  return out
}

const mounted = new WeakMap()
export function mountCells(article, opts) {
  if (!mounted.has(article)) mounted.set(article, mount(article, opts))
  return mounted.get(article)
}

function mount(
  article,
  {
    faces,
    scope,
    reach = 40,
    group = () => null,
    touch = null,
    skip = '',
    field = null,
    quiet = '',
    tether = null,
  }
) {
  const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  const root = document.documentElement
  const canvas = document.createElement('canvas')
  canvas.className = 'cells'
  canvas.setAttribute('aria-hidden', 'true')
  document.body.append(canvas)
  const tiles = new Map()
  let plan = null
  let place = [0, 0]
  const tile = (k) => {
    if (tiles.has(k)) return tiles.get(k)
    const c = k ? document.createElement('canvas') : canvas
    if (k) {
      c.className = 'cells'
      c.setAttribute('aria-hidden', 'true')
      canvas.after(c)
    }
    const t = { c, ctx: c.getContext('2d'), lit: false, ...tileOf(plan, k) }
    Object.assign(c.style, {
      left: `${place[0] + t.left}px`,
      top: `${place[1] + t.top}px`,
      width: `${t.width}px`,
      height: `${t.height}px`,
    })
    c.width = t.dw
    c.height = t.dh
    tiles.set(k, t)
    return t
  }
  const family = (el) => getComputedStyle(el).fontFamily.split(',')[0].replace(/["']/g, '').trim()
  const loaded = (el) =>
    [el, ...el.querySelectorAll('*')].every(
      (e) => !faces[family(e)] || document.fonts.check(`16px "${family(e)}"`)
    )
  let world = null
  let live = []
  let origin = [0, 0]
  let boxes = new Map()
  let ground = null
  let ticks = 0
  let P = null
  let trail = []
  let frayed = null
  const names = new Map([[null, 0]])
  const keys = [null]
  const states = new Map()
  const idOf = (name) => {
    if (!names.has(name)) (names.set(name, keys.length), keys.push(name))
    return names.get(name)
  }
  const colourOf = (tone) => tone & 3
  const groupOf = (q) => q.tone >> 2
  let waits = []
  let dpr = window.devicePixelRatio || 1
  let dev = defaultPixel()
  let cell = dev / dpr
  let sx = 0
  let sy = 0

  function layout() {
    for (const [k, t] of tiles) if (k) t.c.remove()
    tiles.clear()
    canvas.style.width = canvas.style.height = '0'
    for (const el of live) el.classList.remove('cells-on')
    live = (still ? [...article.querySelectorAll('[data-chart]')] : scope()).filter(loaded)
    const charts = [...article.querySelectorAll('[data-chart]')].map((el) => {
      const [kind, list] = el.dataset.chart.split(':')
      const cells = chart(kind, list.split(',').map(Number))
      el.style.width = `${(Math.max(...cells.map(([x]) => x)) + 1) * cell}px`
      el.style.height = `${CHART_H * cell}px`
      return { el, cells }
    })

    article.style.translate = ''
    const doc = root.getBoundingClientRect()
    const a0 = article.getBoundingClientRect()
    const frac = (v) => (v * dpr - Math.floor(v * dpr)) / dpr
    const nx = frac(a0.left - doc.left)
    const ny = frac(a0.top - doc.top)
    if (nx || ny) article.style.translate = `${-nx}px ${-ny}px`
    const a = article.getBoundingClientRect()
    const x0 = a.left - doc.left - Math.floor((a.left - doc.left) / cell) * cell
    const y0 = a.top - doc.top - Math.round(MARGIN / cell) * cell
    sx = Math.round((x0 - Math.floor(x0)) * dpr)
    sy = Math.round((y0 - Math.floor(y0)) * dpr)
    const cols = Math.floor((root.clientWidth - x0 - 1) / cell)
    const rows = Math.ceil((a.bottom - doc.top - y0) / cell) + Math.round(MARGIN / cell)
    const w = Math.ceil(cols * cell + 1)
    const h = Math.min(Math.ceil(rows * cell + 1), Math.floor(root.scrollHeight - Math.floor(y0)))
    place = [Math.floor(x0), Math.floor(y0)]
    origin = [x0, y0]
    plan = tiling(w, h, dpr)
    tile(0)
    const cx = (x) => Math.round((x - doc.left - x0) / cell)
    const cy = (y) => Math.round((y - doc.top - y0) / cell)

    const ink = getComputedStyle(root).color
    const tone = (el) =>
      (getComputedStyle(el).color === ink ? INK : DIM) + 4 * idOf(group(el) ?? null)
    const homes = new Map()
    const clips = new Map()
    const clipOf = (el) => {
      if (!el || el === article) return null
      if (!clips.has(el)) {
        const st = getComputedStyle(el)
        let c = clipOf(el.parentElement)
        if (st.overflowX !== 'visible' || st.overflowY !== 'visible') {
          const r = el.getBoundingClientRect()
          const [l, t] = [r.left + el.clientLeft, r.top + el.clientTop]
          const own = [cx(l), cy(t), cx(l + el.clientWidth), cy(t + el.clientHeight)]
          c = c
            ? [
                Math.max(c[0], own[0]),
                Math.max(c[1], own[1]),
                Math.min(c[2], own[2]),
                Math.min(c[3], own[3]),
              ]
            : own
        }
        clips.set(el, c)
      }
      return clips.get(el)
    }
    let clip = null
    const put = (x, y, k) =>
      x >= 0 &&
      x < cols &&
      (!clip || (x >= clip[0] && y >= clip[1] && x < clip[2] && y < clip[3])) &&
      homes.set(y * cols + x, [x, y, k])
    const glyph = (g, x, y, k) =>
      g[1].forEach((row, ry) => {
        for (let i = 0; i < row.length; i++) if (row[i] === '#') put(x + g[2] + i, y + g[0] + ry, k)
      })

    for (const { el, cells } of charts) {
      if (!live.some((s) => s.contains(el))) continue
      const r = el.getBoundingClientRect()
      const k = tone(el)
      for (const [x, y] of cells) put(cx(r.left) + x, cy(r.top) + y, k)
    }

    const range = document.createRange()
    const rects = (n, i, j) => (range.setStart(n, i), range.setEnd(n, j), range.getClientRects())
    for (const scoped of live) {
      const walk = document.createTreeWalker(scoped, NodeFilter.SHOW_TEXT)
      for (let n = walk.nextNode(); n; n = walk.nextNode()) {
        const el = n.parentElement
        const face = faces[family(el)]
        if (!face || el.closest('[data-chart]') || (skip && el.closest(skip))) continue
        const k = tone(el)
        const G = face.GLYPHS
        clip = clipOf(el)
        for (const m of n.data.matchAll(/\S+/g)) {
          const box = rects(n, m.index, m.index + m[0].length)
          if (box.length === 1) {
            let x = cx(box[0].left)
            const y = cy(box[0].top)
            const w = m[0]
            for (let i = 0; i < w.length; i++) {
              const lig = face.LIGA?.[w.slice(i, i + 2)]
              const g = lig || G[w[i]]
              if (lig) i++
              if (g) (glyph(g, x, y, k), (x += g[3]))
            }
          } else {
            for (let i = 0; i < m[0].length; i++) {
              const r = rects(n, m.index + i, m.index + i + 1)[0]
              if (r && G[m[0][i]]) glyph(G[m[0][i]], cx(r.left), cy(r.top), k)
            }
          }
        }
      }
      for (const link of [scoped, ...scoped.querySelectorAll('a')]) {
        if (!link.matches('a') || (skip && link.closest(skip))) continue
        const top = (faces[family(link)] || { TOP: 0 }).TOP + 2
        const k = DIM + 4 * idOf(group(link) ?? null)
        clip = clipOf(link)
        for (const r of link.getClientRects())
          for (let x = cx(r.left); x < cx(r.right); x++) put(x, cy(r.top) + top, k)
      }
    }

    world = createWorld({ cols, rows, homes: [...homes.values()] })
    Object.assign(world.opts, {
      radius: Math.max(1, Math.round(reach / cell)),
      maxSpeed: Math.max(2, Math.round((reach * 0.35) / cell)),
      fallMax: Math.max(2, Math.round(10 / cell)),
    })
    boxes = new Map()
    for (const s of world.spots) {
      const g = s.tone >> 2
      const b = boxes.get(g) || [s.x, s.y, s.x, s.y, 0]
      boxes.set(g, [
        Math.min(b[0], s.x),
        Math.min(b[1], s.y),
        Math.max(b[2], s.x),
        Math.max(b[3], s.y),
        0,
      ])
    }
    for (const el of live) el.classList.add('cells-on')
    ground = field ? lay(cols, rows, clipOf, cx, cy, x0, y0) : null
    if (ground) first = false
    if (ground && !hover) view()
    settled(true)
    draw()
    wake()
    return world.p.length
  }

  const hash = (i, j, k = 0) => {
    let h = (i * 374761393 + j * 668265263 + k * 2147483647) | 0
    h = Math.imul(h ^ (h >>> 13), 1274126177)
    return ((h ^ (h >>> 16)) >>> 0) / 4294967296
  }
  const noise = (x, y, s) => {
    const i = Math.floor(x / s)
    const j = Math.floor(y / s)
    const fx = x / s - i
    const fy = y / s - j
    const u = fx * fx * (3 - 2 * fx)
    const v = fy * fy * (3 - 2 * fy)
    const a = hash(i, j, 7) + (hash(i + 1, j, 7) - hash(i, j, 7)) * u
    const b = hash(i, j + 1, 7) + (hash(i + 1, j + 1, 7) - hash(i, j + 1, 7)) * u
    return a + (b - a) * v
  }
  const CLEAR = 1
  const TEXT = 2
  function lay(cols, rows, clipOf, cx, cy, x0, y0) {
    const mask = new Uint8Array(cols * rows)
    const fill = (l, t, r, b, bit, air, clip) => {
      let [x1, y1, x2, y2] = [cx(l) - air, cy(t) - air, cx(r) + air, cy(b) + air]
      if (clip) {
        x1 = Math.max(x1, clip[0] - air)
        y1 = Math.max(y1, clip[1] - air)
        x2 = Math.min(x2, clip[2] + air)
        y2 = Math.min(y2, clip[3] + air)
      }
      x1 = Math.max(0, x1)
      y1 = Math.max(0, y1)
      x2 = Math.min(cols, x2)
      y2 = Math.min(rows, y2)
      for (let y = y1; y < y2; y++) for (let x = x1; x < x2; x++) mask[y * cols + x] |= bit
    }
    const air = Math.max(1, Math.round(AIR / cell))
    for (const el of document.querySelectorAll(field.clear))
      for (const r of el.getClientRects())
        if (r.width && r.height) fill(r.left, r.top, r.right, r.bottom, CLEAR, air, clipOf(el))
    for (const [r, el, text] of inkRects(article, field.solid)) {
      const clip = clipOf(el)
      if (text) fill(r.left, r.top, r.right, r.bottom, CLEAR, air, clip)
      fill(r.left, r.top, r.right, r.bottom, TEXT, 0, clip)
    }
    const endY = field.end()
    const end = Math.max(0, Math.min(rows, Math.ceil((endY - y0) / cell)))
    const top = Math.max(1, Math.round(end / 4))
    const [colL, colR, M] = field.column()
    const env = (px, py) => {
      const d = Math.max(0, colL - px, px - colR)
      return d >= M || py >= endY ? 0 : (1 - d / M) ** 2 * (1 - Math.max(0, py) / endY) ** 2
    }
    const B = BLOCK
    const per = (B / cell) ** 2
    const taken = new Map()
    const blockOf = (x, y) =>
      `${Math.floor((x0 + x * cell) / B)},${Math.floor((y0 + y * cell) / B)}`
    const capOf = (k) => {
      const [bx, by] = k.split(',').map(Number)
      return Math.floor(CEIL * per * env((bx + 0.5) * B, (by + 0.5) * B))
    }
    const xs = []
    const ys = []
    const born = []
    let gaps = 0
    let topGaps = 0
    let topCells = 0
    const S = 24
    for (let y = 0; y < end; y++) {
      const py = y0 + y * cell
      const gy = Math.round(py / cell)
      for (let x = 0; x < cols; x++) {
        if (mask[y * cols + x] & CLEAR) continue
        gaps++
        if (y < top) topGaps++
        const px = x0 + x * cell
        const gx = Math.round(px / cell)
        const c = Math.min(1, Math.max(0, (noise(px, py, S) - 0.55) / 0.2))
        if (hash(gx, gy) >= CEIL * env(px, py) * c) continue
        const k = blockOf(x, y)
        const t = taken.get(k) || 0
        if (t >= capOf(k)) continue
        taken.set(k, t + 1)
        xs.push(x)
        ys.push(y)
        born.push(Math.floor(hash(gx, gy, 3) * SCATTER))
        if (y < top) topCells++
      }
    }
    const n = xs.length
    const occ = new Int32Array(cols * rows)
    for (let i = 0; i < n; i++) occ[ys[i] * cols + xs[i]] = i + 1
    return {
      cols,
      rows,
      mask,
      end,
      n,
      gaps,
      top: { rows: top, cells: topCells, gaps: topGaps },
      lx: Int32Array.from(xs),
      ly: Int32Array.from(ys),
      hx: Int32Array.from(xs),
      hy: Int32Array.from(ys),
      x: Int32Array.from(xs),
      y: Int32Array.from(ys),
      hid: new Uint8Array(n),
      born: Int32Array.from(born),
      occ,
      taken,
      blockOf,
      capOf,
      R: Math.max(2, Math.round(PART / cell)),
      age: first && !still ? 0 : SCATTER,
    }
  }
  let first = true
  const free = (f, x, y) =>
    x >= 0 &&
    y >= 0 &&
    x < f.cols &&
    y < f.end &&
    !(f.mask[y * f.cols + x] & CLEAR) &&
    !f.occ[y * f.cols + x]
  const go = (f, i, x, y) => {
    if (f.occ[f.y[i] * f.cols + f.x[i]] === i + 1) f.occ[f.y[i] * f.cols + f.x[i]] = 0
    f.x[i] = x
    f.y[i] = y
    f.occ[y * f.cols + x] = i + 1
  }
  const near = (f, i, x, y) => {
    let k = 0
    for (let dy = -1; dy <= 1; dy++)
      for (let dx = -1; dx <= 1; dx++) {
        if (!dx && !dy) continue
        const xx = x + dx
        const yy = y + dy
        if (xx < 0 || yy < 0 || xx >= f.cols || yy >= f.rows) continue
        const o = f.occ[yy * f.cols + xx]
        if (o && o !== i + 1) k++
      }
    return k
  }
  function drift(f, P) {
    const k = Math.min(12, 3 + Math.floor(f.n / 400))
    for (let t = 0; t < k && f.n; t++) {
      const i = rnd(f.n)
      if (f.hid[i] || f.x[i] !== f.hx[i] || f.y[i] !== f.hy[i]) continue
      const d = rnd(8)
      const dx = [-1, 0, 1, -1, 1, -1, 0, 1][d]
      const dy = [-1, -1, -1, 0, 0, 1, 1, 1][d]
      const x = f.hx[i] + dx
      const y = f.hy[i] + dy
      if (Math.abs(x - f.lx[i]) > WANDER || Math.abs(y - f.ly[i]) > WANDER) continue
      if (!free(f, x, y) || near(f, i, x, y) < 1 || (P && Math.hypot(x - P[0], y - P[1]) <= f.R))
        continue
      const from = f.blockOf(f.hx[i], f.hy[i])
      const to = f.blockOf(x, y)
      if (to !== from) {
        const t = f.taken.get(to) || 0
        if (t >= f.capOf(to)) continue
        f.taken.set(to, t + 1)
        f.taken.set(from, f.taken.get(from) - 1)
      }
      f.hx[i] = x
      f.hy[i] = y
      go(f, i, x, y)
    }
  }
  function part(f, P) {
    const R = f.R
    for (let i = 0; i < f.n; i++) {
      const inside = P && Math.hypot(f.x[i] - P[0], f.y[i] - P[1]) <= R
      if (inside && !f.hid[i]) {
        const a = Math.atan2(f.y[i] - P[1], f.x[i] - P[0])
        let to = null
        for (let r = R + 1; !to && r <= R + 4; r++)
          for (const da of [0, 0.3, -0.3, 0.6, -0.6]) {
            const x = Math.round(P[0] + Math.cos(a + da) * r)
            const y = Math.round(P[1] + Math.sin(a + da) * r)
            if (Math.hypot(x - P[0], y - P[1]) > R && free(f, x, y)) {
              to = [x, y]
              break
            }
          }
        if (to) go(f, i, ...to)
        else {
          f.hid[i] = 1
          if (f.occ[f.y[i] * f.cols + f.x[i]] === i + 1) f.occ[f.y[i] * f.cols + f.x[i]] = 0
        }
        continue
      }
      if (f.x[i] === f.hx[i] && f.y[i] === f.hy[i] && !f.hid[i]) continue
      if (P && Math.hypot(f.hx[i] - P[0], f.hy[i] - P[1]) <= R + 1) continue
      if (f.hid[i]) {
        if (free(f, f.hx[i], f.hy[i]))
          ((f.hid[i] = 0), (f.x[i] = f.hx[i]), (f.y[i] = f.hy[i]), go(f, i, f.hx[i], f.hy[i]))
        continue
      }
      const x = f.x[i] + Math.sign(f.hx[i] - f.x[i])
      const y = f.y[i] + Math.sign(f.hy[i] - f.y[i])
      if ((x === f.hx[i] && y === f.hy[i]) || free(f, x, y)) {
        if (P && Math.hypot(x - P[0], y - P[1]) <= R) continue
        go(f, i, x, y)
      }
    }
  }
  function blob(f) {
    const out = []
    const seen = new Set()
    const r0 = BLOB / cell
    trail.forEach((P, k) => {
      const r = r0 * (1 - k / (TRAIL + 1))
      for (let y = Math.floor(P[1] - r); y <= Math.ceil(P[1] + r); y++)
        for (let x = Math.floor(P[0] - r); x <= Math.ceil(P[0] + r); x++) {
          if (x < 0 || y < 0 || x >= f.cols || y >= f.rows) continue
          const at = y * f.cols + x
          if (seen.has(at) || f.mask[at] & TEXT) continue
          const d = Math.hypot(x + 0.5 - P[0], y + 0.5 - P[1])
          if (d > r) continue
          const step = k + (d > r * 0.5 ? 1 : 0)
          if (step && (x + y) % (step + 1)) continue
          seen.add(at)
          out.push([x, y, !k && d <= r * 0.5 ? INK : DIM])
        }
    })
    return out
  }

  function settled(all = false) {
    if (!waits.length) return
    const away = new Set()
    if (!all) for (const q of world.p) if (!q.home) away.add(groupOf(q))
    waits = waits.filter(([ids, done]) => {
      if ([...ids].some((i) => away.has(i))) return true
      done()
      return false
    })
  }
  const pixelsOf = (ids) => world.p.filter((q) => ids.has(groupOf(q)))
  const rnd = (n) => Math.floor(Math.random() * n)

  function draw() {
    const s = Math.round(cell * dpr)
    const { TW, TH, nx, ny } = plan
    const by = new Map()
    const unseen = new Set([...states.keys()].map((k) => names.get(k)))
    const f = ground
    const extra = []
    if (f && P && !still) for (const [x, y, c] of blob(f)) extra.push({ x, y, tone: c })
    if (f)
      for (let i = 0; i < f.n; i++)
        if (!f.hid[i] && f.born[i] <= f.age) extra.push({ x: f.x[i], y: f.y[i], tone: DIM })
    for (const q of extra.length ? [...extra, ...world.p] : world.p) {
      if (unseen.size && q.spot !== undefined && unseen.has(groupOf(q))) continue
      const X = sx + q.x * s
      const Y = sy + q.y * s
      for (
        let j = Math.floor(Y / TH), jn = Math.min(ny - 1, Math.floor((Y + s - 1) / TH));
        j <= jn;
        j++
      )
        for (
          let i = Math.floor(X / TW), n = Math.min(nx - 1, Math.floor((X + s - 1) / TW));
          i <= n;
          i++
        ) {
          const k = j * nx + i
          if (!by.has(k)) by.set(k, [])
          by.get(k).push(q)
        }
    }
    for (const [k, t] of tiles)
      if (t.lit && !by.has(k)) (t.ctx.clearRect(0, 0, t.dw, t.dh), (t.lit = false))
    const css = getComputedStyle(root)
    for (const [k, ps] of by) {
      const t = tile(k)
      t.ctx.clearRect(0, 0, t.dw, t.dh)
      t.lit = true
      for (const [tone, colour] of [
        [INK, '--ink'],
        [DIM, '--dim'],
      ]) {
        t.ctx.fillStyle = css.getPropertyValue(colour)
        for (const q of ps)
          if (colourOf(q.tone) === tone)
            t.ctx.fillRect(sx + q.x * s - t.ox, sy + q.y * s - t.oy, s, s)
      }
    }
  }

  let raf = 0
  let last = 0
  let acc = 0
  function frame(now) {
    acc += Math.min(0.25, (now - last) / 1000)
    last = now
    let away = 1
    let n = 0
    for (; acc >= 1 / TPS; acc -= 1 / TPS) ((away = step()), n++)
    if (n) (settled(), draw())
    raf = away || busy() ? requestAnimationFrame(frame) : 0
  }
  function step() {
    ticks++
    if (frayed !== null && !states.has(frayed)) fray1(frayed)
    const away = tick(world)
    if (tethered) for (const q of world.p) if (!q.home) hold(q)
    trail = P ? [P, ...trail].slice(0, TRAIL + 1) : trail.slice(0, -1)
    const f = ground
    if (f) {
      f.age++
      drift(f, P)
      part(f, P)
    }
    return away
  }
  const busy = () =>
    !!ground &&
    !document.hidden &&
    (window.scrollY < origin[1] + ground.end * cell || !!P || trail.length > 0 || frayed !== null)
  function wake() {
    if (raf || still) return
    last = performance.now()
    acc = 0
    raf = requestAnimationFrame(frame)
  }
  document.addEventListener('visibilitychange', wake)
  window.addEventListener('scroll', () => (hover ? null : view(), wake()), { passive: true })
  const tethered = !!tether
  function hold(q) {
    const g = groupOf(q)
    const r = Math.round((tether(keys[g]) || 0) / cell)
    if (!r) return
    const b = boxes.get(g)
    const x = Math.max(b[0] - r, Math.min(b[2] + r, q.x))
    const y = Math.max(b[1] - r, Math.min(b[3] + r, q.y))
    if (x !== q.x || y !== q.y) setDown(world, q, x, y, q.wait)
  }
  function fray1(name) {
    const g = names.get(name)
    const b = boxes.get(g)
    if (!b) return
    const own = byGroup(g)
    const e = Math.max(1, Math.round(2 / cell))
    const r = Math.max(1, Math.round(FRAYED / cell))
    for (let n = 0, t = 0; n < FRAY && t < 40 && own.length; t++) {
      const q = own[rnd(own.length)]
      if (!q.home) continue
      if (q.x > b[0] + e && q.x < b[2] - e && q.y > b[1] + e && q.y < b[3] - e) continue
      setDown(world, q, q.x + rnd(2 * r + 1) - r, q.y + rnd(2 * r + 1) - r, 2 + rnd(6))
      n++
    }
  }
  const groupCache = new Map()
  const byGroup = (g) => {
    if (groupCache.get(g)?.[0] !== world)
      groupCache.set(g, [world, world.p.filter((q) => groupOf(q) === g)])
    return groupCache.get(g)[1]
  }
  const hover = window.matchMedia('(hover: hover)').matches
  const lattice = (docX, docY) => [(docX - origin[0]) / cell, (docY - origin[1]) / cell]
  function view() {
    if (!ground || still) return
    const d = root.getBoundingClientRect()
    P = lattice(window.innerWidth / 2 - d.left, window.innerHeight / 2 - d.top)
  }

  const at = (e) => {
    const r = canvas.getBoundingClientRect()
    return [
      Math.floor((e.clientX - r.left - sx / dpr) / cell),
      Math.floor((e.clientY - r.top - sy / dpr) / cell),
    ]
  }
  const touched = () => {
    if (!touch) return world
    const ok = keys.map((k) => !states.has(k) && touch(k))
    return { ...world, p: world.p.filter((q) => ok[groupOf(q)]) }
  }
  let prev = null
  window.addEventListener('pointermove', (e) => {
    if (!world) return
    if (ground && hover && e.pointerType === 'mouse' && !still) {
      const d = root.getBoundingClientRect()
      P = lattice(e.clientX - d.left, e.clientY - d.top)
      wake()
    }
    if (prev && e.timeStamp > prev.t) {
      const f = (e.timeStamp - prev.t) / 1000 / (1 / TPS) / cell
      kick(touched(), ...at(e), (e.clientX - prev.x) / f, (e.clientY - prev.y) / f)
      wake()
    }
    prev = { x: e.clientX, y: e.clientY, t: e.timeStamp }
  })
  window.addEventListener('pointerdown', (e) => {
    if (!world) return
    burst(touched(), ...at(e))
    prev = { x: e.clientX, y: e.clientY, t: e.timeStamp }
    wake()
  })
  window.addEventListener('pointerup', (e) => e.pointerType !== 'mouse' && (prev = null))
  document.documentElement.addEventListener('pointerleave', () => hover && (P = null))

  let pending = 0
  async function apply() {
    const ratio = window.devicePixelRatio || 1
    if (ratio !== dpr) dev = fit((dev * ratio) / dpr, ratio)
    dpr = ratio
    cell = dev / dpr
    root.style.setProperty('--px', cell)
    const mine = ++pending
    await document.fonts.ready
    if (mine !== pending) return null
    const n = layout()
    seen = now()
    return n
  }

  let seen = ''
  const now = () => {
    const a = article.getBoundingClientRect()
    const d = root.getBoundingClientRect()
    return `${root.clientWidth} ${window.devicePixelRatio} ${a.left - d.left},${a.top - d.top} ${a.width}x${a.height}`
  }
  let queued = 0
  const refresh = () =>
    (queued ||= requestAnimationFrame(() => {
      queued = 0
      if (now() !== seen) apply()
    }))
  const watch = new ResizeObserver(refresh)
  watch.observe(root)
  watch.observe(article)
  const watchRatio = () =>
    window
      .matchMedia(`(resolution: ${window.devicePixelRatio || 1}dppx)`)
      .addEventListener('change', () => (refresh(), watchRatio()), {
        once: true,
      })
  watchRatio()
  document.fonts.addEventListener('loadingdone', apply)
  let scrolled = 0
  article.addEventListener(
    'scroll',
    (e) =>
      (quiet && e.target.closest?.(quiet)) ||
      (scrolled ||= requestAnimationFrame(() => ((scrolled = 0), apply()))),
    { capture: true, passive: true }
  )

  function set(name, state) {
    idOf(name)
    if (state === 'shown') states.delete(name)
    else states.set(name, state)
    if (world) draw()
  }
  function arrive(names, { rise = 40, spread = 36 } = {}) {
    for (const n of names) set(n, 'shown')
    if (still || !world) return Promise.resolve()
    const ids = new Set(names.map(idOf))
    const up = Math.max(1, Math.round(rise / cell))
    for (const q of pixelsOf(ids))
      setDown(world, q, q.x + rnd(3) - 1, q.y - 1 - rnd(up), rnd(spread))
    const done = new Promise((r) => waits.push([ids, r]))
    draw()
    wake()
    return done
  }
  function stir(name, n = 6) {
    if (still || !world || states.has(name)) return
    const own = pixelsOf(new Set([idOf(name)])).filter((q) => q.home)
    for (let i = 0; i < n && own.length; i++) {
      const q = own.splice(rnd(own.length), 1)[0]
      setDown(world, q, q.x + rnd(5) - 2, q.y - 1 - rnd(3), 2 + rnd(8))
    }
    wake()
  }

  function fray(name) {
    frayed = still || name === undefined ? null : name
    if (frayed !== null) (idOf(frayed), wake())
  }
  const toPage = (x, y) => [origin[0] + x * cell, origin[1] + y * cell]
  return {
    layout: apply,
    draw,
    set,
    arrive,
    stir,
    fray,
    state: (name) => states.get(name) || 'shown',
    field: () => {
      const f = ground
      if (!f) return null
      const list = []
      for (let i = 0; i < f.n; i++)
        if (!f.hid[i] && f.born[i] <= f.age) list.push(toPage(f.x[i], f.y[i]))
      return {
        cells: f.n,
        gaps: f.gaps,
        top: { ...f.top, y: toPage(0, f.top.rows)[1] },
        end: toPage(0, f.end)[1],
        list,
        pointer: P && !still ? blob(f).map(([x, y]) => toPage(x, y)) : [],
        at: P && !still ? toPage(...P) : null,
        radius: f.R * cell,
        cell,
        ticks,
      }
    },
    pixels: (name) =>
      world && names.has(name)
        ? byGroup(names.get(name)).map((q) => [...toPage(q.x, q.y), q.home])
        : [],
    box: (name) => {
      const b = world && boxes.get(names.get(name))
      return b ? [...toPage(b[0], b[1]), ...toPage(b[2] + 1, b[3] + 1)] : null
    },
    get ticks() {
      return ticks
    },
    groups: () =>
      keys.map((k, i) => {
        const own = world ? world.p.filter((q) => groupOf(q) === i) : []
        return [k, states.get(k) || 'shown', own.length, own.filter((q) => q.home).length]
      }),
    get pixel() {
      return dev
    },
    set pixel(n) {
      dev = Math.max(1, Math.round(n))
      apply()
    },
  }
}

export function chart(kind, v, h = CHART_H) {
  const out = []
  if (kind === 'b')
    v.forEach((n, i) => {
      for (let y = h - Math.max(1, Math.round((n / 100) * h)); y < h; y++)
        for (let x = 0; x < 3; x++) out.push([i * 4 + x, y])
    })
  else if (kind === 'l') {
    const pts = v.map((n, i) => [i * 3, Math.round((1 - n / 100) * (h - 1))])
    for (let i = 1; i < pts.length; i++) line(pts[i - 1], pts[i], out)
  } else if (kind === 'p') {
    const r = (h - 1) / 2
    for (let y = 0; y < h; y++)
      for (let x = 0; x < h; x++) {
        const d = Math.hypot(x - r, y - r)
        const turn = (Math.atan2(x - r, r - y) / (2 * Math.PI) + 1) % 1
        if (Math.abs(d - r) < 0.5 || (d > 0 && d <= r && turn <= v[0] / 100)) out.push([x, y])
      }
  }
  return out
}

function line([x0, y0], [x1, y1], out) {
  const dx = Math.abs(x1 - x0)
  const dy = -Math.abs(y1 - y0)
  const sx = x0 < x1 ? 1 : -1
  const sy = y0 < y1 ? 1 : -1
  for (let e = dx + dy; ;) {
    out.push([x0, y0])
    if (x0 === x1 && y0 === y1) return
    const e2 = 2 * e
    if (e2 >= dy) ((e += dy), (x0 += sx))
    if (e2 <= dx) ((e += dx), (y0 += sy))
  }
}
