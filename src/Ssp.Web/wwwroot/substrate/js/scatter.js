export const DEFAULTS = {
  radius: 8,
  gain: 1,
  maxSpeed: 4,
  waitMin: 10,
  waitMax: 28,
  fallMax: 3,
  patience: 3,
}

const sign = Math.sign
const clamp = (v, m) => Math.max(-m, Math.min(m, v))
const quant = (v) => (Math.abs(v) < 0.34 ? 0 : sign(v) * Math.max(1, Math.round(Math.abs(v))))

export function createWorld({ cols, rows, homes, rand = Math.random }) {
  const spots = homes.map(([x, y, tone = 0]) => ({ x, y, tone, owner: null }))
  const p = spots.map((spot) => ({
    spot,
    tone: spot.tone,
    x: spot.x,
    y: spot.y,
    vx: 0,
    vy: 0,
    wait: 0,
    falling: 0,
    blocked: 0,
    home: true,
  }))
  const occ = new Map()
  for (const q of p) {
    q.spot.owner = q
    occ.set(q.y * cols + q.x, q)
  }
  const bw = Math.ceil(cols / B)
  const free = new Map(spots.map((s) => [s.tone, []]))
  return {
    cols,
    rows,
    rand,
    p,
    spots,
    occ,
    bw,
    bh: Math.ceil(rows / B),
    free,
    opts: { ...DEFAULTS },
  }
}

const B = 8
const bucket = (w, s) => {
  const grid = w.free.get(s.tone)
  const k = Math.floor(s.y / B) * w.bw + Math.floor(s.x / B)
  return grid[k] || (grid[k] = [])
}
function release(w, s) {
  s.owner = null
  const b = bucket(w, s)
  s.i = b.push(s) - 1
}
function take(w, s, q) {
  const b = bucket(w, s)
  const last = b.pop()
  if (last !== s) ((b[s.i] = last), (last.i = s.i))
  s.owner = q
  q.spot = s
}

const key = (w, x, y) => y * w.cols + x
const inside = (w, x, y) => x >= 0 && y >= 0 && x < w.cols && y < w.rows

function disturb(w, q, vx, vy) {
  const o = w.opts
  q.vx = clamp(quant(vx), o.maxSpeed)
  q.vy = clamp(quant(vy), o.maxSpeed)
  if (!q.vx && !q.vy) return
  if (q.spot) release(w, q.spot)
  q.spot = null
  q.home = false
  q.falling = 0
  q.blocked = 0
  q.wait = o.waitMin + Math.floor(w.rand() * (o.waitMax - o.waitMin + 1))
}

export function place(w, q, x, y, wait = 0) {
  q.home = false
  q.vx = q.vy = 0
  q.falling = 0
  q.blocked = 0
  q.wait = wait
  move(w, q, Math.max(0, Math.min(w.cols - 1, x)), Math.max(0, Math.min(w.rows - 1, y)))
}

export function kick(w, cx, cy, vx, vy) {
  const o = w.opts
  for (const q of w.p) {
    const f = Math.max(0, 1 - Math.hypot(q.x - cx, q.y - cy) / o.radius) ** 2
    if (f > 0) disturb(w, q, vx * o.gain * f + q.vx, vy * o.gain * f + q.vy)
  }
}

export function burst(w, cx, cy) {
  const o = w.opts
  for (const q of w.p) {
    const dx = q.x - cx
    const dy = q.y - cy
    const d = Math.hypot(dx, dy)
    const f = Math.max(0, 1 - d / (o.radius * 1.5))
    if (f <= 0) continue
    const a = d > 0 ? Math.atan2(dy, dx) : w.rand() * Math.PI * 2
    disturb(w, q, Math.cos(a) * o.maxSpeed * f, Math.sin(a) * o.maxSpeed * f)
  }
}

function move(w, q, x, y) {
  if (w.occ.get(key(w, q.x, q.y)) === q) w.occ.delete(key(w, q.x, q.y))
  q.x = x
  q.y = y
  if (!w.occ.has(key(w, x, y))) w.occ.set(key(w, x, y), q)
}

function drift(w, q) {
  const n = Math.max(Math.abs(q.vx), Math.abs(q.vy))
  let ex = 0
  let ey = 0
  for (let s = 1; s <= n; s++) {
    const sx = Math.round((q.vx * s) / n) - ex
    const sy = Math.round((q.vy * s) / n) - ey
    ex += sx
    ey += sy
    let nx = q.x + sx
    let ny = q.y + sy
    if (nx < 0 || nx >= w.cols) {
      q.vx = -q.vx
      nx = q.x
    }
    if (ny < 0 || ny >= w.rows) {
      q.vy = -q.vy
      ny = q.y
    }
    if (nx === q.x && ny === q.y) return
    if (w.occ.has(key(w, nx, ny))) {
      q.vx = -q.vx
      q.vy = -q.vy
      return
    }
    move(w, q, nx, ny)
  }
}

export function nearest(w, px, py, tone) {
  const grid = w.free.get(tone)
  const bx = Math.floor(px / B)
  const by = Math.floor(py / B)
  let best = null
  let bd = Infinity
  const scan = (x, y) => {
    if (x < 0 || y < 0 || x >= w.bw || y >= w.bh) return
    for (const s of grid[y * w.bw + x] || []) {
      const d = (s.x - px) ** 2 + (s.y - py) ** 2
      if (d < bd) ((bd = d), (best = s))
    }
  }
  scan(bx, by)
  for (let r = 1; !best || bd > ((r - 1) * B + 1) ** 2; r++) {
    for (let x = bx - r; x <= bx + r; x++) (scan(x, by - r), scan(x, by + r))
    for (let y = by - r + 1; y < by + r; y++) (scan(bx - r, y), scan(bx + r, y))
  }
  return best
}
const claim = (w, q) => take(w, nearest(w, q.x, q.y, q.tone), q)

function fall(w, q) {
  if (!q.spot) claim(w, q)
  const { x: hx, y: hy } = q.spot
  q.falling++
  const steps = Math.min(w.opts.fallMax, 1 + (q.falling >> 2))
  for (let s = 0; s < steps; s++) {
    const dx = sign(hx - q.x)
    const dy = sign(hy - q.y)
    if (!dx && !dy) break
    const tries =
      Math.abs(hx - q.x) >= Math.abs(hy - q.y)
        ? [
            [dx, dy],
            [dx, 0],
            [0, dy],
          ]
        : [
            [dx, dy],
            [0, dy],
            [dx, 0],
          ]
    const free = tries.find(
      ([ax, ay]) =>
        (ax || ay) && inside(w, q.x + ax, q.y + ay) && !w.occ.has(key(w, q.x + ax, q.y + ay))
    )
    if (free) {
      q.blocked = 0
      move(w, q, q.x + free[0], q.y + free[1])
    } else if (++q.blocked > w.opts.patience) {
      move(w, q, q.x + dx, q.y + dy)
    } else break
  }
  if (q.x === hx && q.y === hy) {
    q.vx = q.vy = 0
    q.home = true
    w.occ.set(key(w, q.x, q.y), q)
  }
}

export function tick(w) {
  const moving = w.p.filter((q) => !q.home)
  for (let i = moving.length - 1; i > 0; i--) {
    const j = Math.floor(w.rand() * (i + 1))
    ;[moving[i], moving[j]] = [moving[j], moving[i]]
  }
  let away = 0
  for (const q of moving) {
    if (q.wait > 0) {
      q.wait--
      drift(w, q)
    } else fall(w, q)
    away += !q.home
  }
  return away
}
