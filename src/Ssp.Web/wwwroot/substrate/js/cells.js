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
const GROUP = 4
const WANDER = 3
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
  let dead = false
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
    if (dead) return ((live = []), 0)
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
        if (!link.matches('a') || link.matches('.cover') || (skip && link.closest(skip))) continue
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
    for (const el of live)
      if (states.get(group(el) ?? null) !== 'gone') el.classList.add('cells-on')
    ground = field ? lay(cols, rows, clipOf, cx, cy, x0, y0) : null
    if (ground) first = false
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
  const groupAt = (occ, cols, rows, x, y) => {
    const out = [occ[y * cols + x]]
    const had = new Set(out)
    const q = [[x, y]]
    while (q.length) {
      const [x0, y0] = q.pop()
      for (let dy = -1; dy <= 1; dy++)
        for (let dx = -1; dx <= 1; dx++) {
          const xx = x0 + dx
          const yy = y0 + dy
          if (xx < 0 || yy < 0 || xx >= cols || yy >= rows) continue
          const o = occ[yy * cols + xx]
          if (!o || had.has(o)) continue
          had.add(o)
          out.push(o)
          q.push([xx, yy])
        }
    }
    return out
  }
  const CLEAR = 1
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
    for (const [r, el, text] of inkRects(article, field.solid))
      if (text) fill(r.left, r.top, r.right, r.bottom, CLEAR, air, clipOf(el))
    const endY = field.end()
    const end = Math.max(0, Math.min(rows, Math.ceil((endY - y0) / cell)))
    const start = field.start
      ? Math.max(0, Math.min(end, Math.ceil((field.start() - y0) / cell)))
      : 0
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
    for (let y = start; y < end; y++) {
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
    const at = new Int32Array(cols * rows)
    for (let i = 0; i < xs.length; i++) at[ys[i] * cols + xs[i]] = i + 1
    const keep = new Uint8Array(xs.length)
    const seen = new Uint8Array(xs.length)
    for (let i = 0; i < xs.length; i++) {
      if (seen[i]) continue
      const group = groupAt(at, cols, rows, xs[i], ys[i])
      for (const j of group) seen[j - 1] = 1
      if (group.length >= GROUP) for (const j of group) keep[j - 1] = 1
      else
        for (const j of group) {
          const k = blockOf(xs[j - 1], ys[j - 1])
          taken.set(k, taken.get(k) - 1)
        }
    }
    for (let i = xs.length - 1; i >= 0; i--)
      if (!keep[i]) {
        if (ys[i] < top) topCells--
        xs.splice(i, 1)
        ys.splice(i, 1)
        born.splice(i, 1)
      }
    const n = xs.length
    const occ = new Int32Array(cols * rows)
    for (let i = 0; i < n; i++) occ[ys[i] * cols + xs[i]] = i + 1
    return {
      cols,
      rows,
      mask,
      start,
      end,
      n,
      gaps,
      top: { rows: top, cells: topCells, gaps: topGaps },
      lx: Int32Array.from(xs),
      ly: Int32Array.from(ys),
      x: Int32Array.from(xs),
      y: Int32Array.from(ys),
      born: Int32Array.from(born),
      occ,
      taken,
      blockOf,
      capOf,
      age: first && !still ? 0 : SCATTER,
    }
  }
  let first = true
  const free = (f, x, y) =>
    x >= 0 &&
    y >= f.start &&
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
  function drift(f) {
    const k = Math.min(12, 3 + Math.floor(f.n / 400))
    for (let t = 0; t < k && f.n; t++) {
      const i = rnd(f.n)
      const d = rnd(8)
      const dx = [-1, 0, 1, -1, 1, -1, 0, 1][d]
      const dy = [-1, -1, -1, 0, 0, 1, 1, 1][d]
      const x = f.x[i] + dx
      const y = f.y[i] + dy
      if (Math.abs(x - f.lx[i]) > WANDER || Math.abs(y - f.ly[i]) > WANDER) continue
      if (!free(f, x, y) || near(f, i, x, y) < 1) continue
      const from = f.blockOf(f.x[i], f.y[i])
      const to = f.blockOf(x, y)
      if (to !== from && (f.taken.get(to) || 0) >= f.capOf(to)) continue
      const [ox, oy] = [f.x[i], f.y[i]]
      const was = groupAt(f.occ, f.cols, f.rows, ox, oy).filter(
        (o) => o !== i + 1 && Math.abs(f.x[o - 1] - ox) <= 1 && Math.abs(f.y[o - 1] - oy) <= 1
      )
      go(f, i, x, y)
      const now = new Set(groupAt(f.occ, f.cols, f.rows, x, y))
      if (!was.every((o) => now.has(o))) {
        go(f, i, ox, oy)
        continue
      }
      if (to !== from) {
        f.taken.set(to, (f.taken.get(to) || 0) + 1)
        f.taken.set(from, f.taken.get(from) - 1)
      }
    }
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
    if (dead) return
    const s = Math.round(cell * dpr)
    const { TW, TH, nx, ny } = plan
    const by = new Map()
    const unseen = new Set([...states.keys()].map((k) => names.get(k)))
    const f = ground
    const extra = []
    if (f)
      for (let i = 0; i < f.n; i++)
        if (f.born[i] <= f.age) extra.push({ x: f.x[i], y: f.y[i], tone: DIM })
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
    try {
      acc += Math.min(0.25, (now - last) / 1000)
      last = now
      let away = 1
      let n = 0
      for (; acc >= 1 / TPS; acc -= 1 / TPS) ((away = step()), n++)
      if (n) (settled(), draw())
      raf = away || busy() ? requestAnimationFrame(frame) : 0
    } catch (err) {
      recover('a frame threw', err)
    }
  }
  function recover(why, err) {
    if (dead) return
    dead = true
    if (raf) cancelAnimationFrame(raf)
    raf = 0
    const away = world
      ? [...new Set(world.p.filter((q) => !q.home).map((q) => keys[groupOf(q)]))]
      : []
    for (const el of live) el.classList.remove('cells-on')
    live = []
    for (const [, t] of tiles) (t.ctx.clearRect(0, 0, t.dw, t.dh), (t.lit = false))
    ground = null
    for (const [, done] of waits) done()
    waits = []
    const e = err instanceof Error ? err : new Error(`cells: ${why}`)
    console.error(
      `cells: ${why}, the text is the page again (away: ${away.join(', ') || 'none'})`,
      e
    )
    window.reportError?.(e)
  }
  function watchdog(done, spread) {
    const span = spread + TPS
    const wall = ((3 * span) / TPS) * 1000
    let from = ticks
    let over = false
    done.then(() => (over = true))
    const look = () => {
      if (over || dead) return
      if (document.hidden) return ((from = ticks), setTimeout(look, wall))
      if (ticks === from) return recover('no tick ran in three times an arrival')
      if (ticks - from >= 3 * span) return recover('an arrival not home in three times its ticks')
      setTimeout(look, (span / TPS) * 1000)
    }
    setTimeout(look, wall)
  }
  function step() {
    ticks++
    if (frayed !== null && !states.has(frayed)) fray1(frayed)
    const away = tick(world)
    const f = ground
    if (f) {
      f.age++
      drift(f)
    }
    return away
  }
  const busy = () =>
    !!ground &&
    !document.hidden &&
    (window.scrollY < origin[1] + ground.end * cell || frayed !== null)
  function wake() {
    if (raf || still || dead) return
    last = performance.now()
    acc = 0
    raf = requestAnimationFrame(frame)
  }
  document.addEventListener('visibilitychange', wake)
  window.addEventListener('scroll', wake, { passive: true })
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
    if (!world || dead) return
    if (prev && e.timeStamp > prev.t) {
      const f = (e.timeStamp - prev.t) / 1000 / (1 / TPS) / cell
      kick(touched(), ...at(e), (e.clientX - prev.x) / f, (e.clientY - prev.y) / f)
      wake()
    }
    prev = { x: e.clientX, y: e.clientY, t: e.timeStamp }
  })
  window.addEventListener('pointerdown', (e) => {
    if (!world || dead) return
    burst(touched(), ...at(e))
    prev = { x: e.clientX, y: e.clientY, t: e.timeStamp }
    wake()
  })
  window.addEventListener('pointerup', (e) => e.pointerType !== 'mouse' && (prev = null))

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
    let n = 0
    try {
      n = layout()
    } catch (err) {
      recover('a lay threw', err)
    }
    for (const el of placed) watch.unobserve(el)
    placed = live.filter((el) => !(quiet && el.closest(quiet)))
    for (const el of placed) watch.observe(el)
    seen = now()
    laid = mine
    return n
  }
  let laid = 0
  const current = () => laid === pending && now() === seen

  let seen = ''
  let placed = []
  const now = () => {
    const a = article.getBoundingClientRect()
    const d = root.getBoundingClientRect()
    let s = `${root.clientWidth} ${window.devicePixelRatio} ${a.left - d.left},${a.top - d.top} ${a.width}x${a.height}`
    for (const el of placed) {
      const r = el.getBoundingClientRect()
      s += ` ${r.left - a.left},${r.top - a.top}`
    }
    return s
  }
  let queued = 0
  const refresh = () =>
    (queued ||= requestAnimationFrame(() => {
      queued = 0
      if (now() !== seen) apply()
    }))
  const watch = new ResizeObserver(() => {
    if (now() === seen) return
    if (queued) (cancelAnimationFrame(queued), (queued = 0))
    apply()
  })
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
  for (const e of ['resize', 'orientationchange', 'pageshow', 'load'])
    window.addEventListener(e, refresh)
  window.visualViewport?.addEventListener('resize', refresh)
  document.fonts.ready.then(refresh)
  setInterval(() => document.hidden || dead || refresh(), 500)
  let scrolled = 0
  article.addEventListener(
    'scroll',
    (e) =>
      (quiet && e.target.closest?.(quiet)) ||
      (scrolled ||= requestAnimationFrame(() => ((scrolled = 0), apply()))),
    { capture: true, passive: true }
  )

  function set(name, state) {
    for (const n of [name].flat()) {
      idOf(n)
      if (state === 'shown') states.delete(n)
      else states.set(n, state)
    }
    if (world) draw()
  }
  function arrive(names, { rise = 40, spread = 36 } = {}) {
    set(names, 'shown')
    if (still || !world || dead) return Promise.resolve()
    const ids = new Set(names.map(idOf))
    const up = Math.max(1, Math.round(rise / cell))
    for (const q of pixelsOf(ids))
      setDown(world, q, q.x + rnd(3) - 1, q.y - 1 - rnd(up), rnd(spread))
    const done = new Promise((r) => waits.push([ids, r]))
    watchdog(done, spread)
    draw()
    wake()
    return done
  }
  function fray(name) {
    frayed = still || name === undefined ? null : name
    if (frayed !== null) (idOf(frayed), wake())
  }
  const toPage = (x, y) => [origin[0] + x * cell, origin[1] + y * cell]
  return {
    layout: apply,
    current,
    draw,
    set,
    arrive,
    fray,
    state: (name) => states.get(name) || 'shown',
    field: () => {
      const f = ground
      if (!f) return null
      const list = []
      for (let i = 0; i < f.n; i++) if (f.born[i] <= f.age) list.push(toPage(f.x[i], f.y[i]))
      return {
        cells: f.n,
        gaps: f.gaps,
        top: { ...f.top, y: toPage(0, f.top.rows)[1] },
        end: toPage(0, f.end)[1],
        list,
        cell,
        ticks,
      }
    },
    pixels: (name) =>
      world && names.has(name)
        ? byGroup(names.get(name)).map((q) => [
            ...toPage(q.x, q.y),
            q.home,
            ...(q.spot ? toPage(q.spot.x, q.spot.y) : [null, null]),
          ])
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
