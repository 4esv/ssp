export const DISH = 86
const SHAPE_N = 2

export const PARAMS = Object.freeze({
  agents: 500,
  sensorAngle: 0.5,
  sensorDist: 5,
  turn: 0.5,
  jitter: 0.9,
  deposit: 1.4,
  decay: 0.85,
  clump: 12,
  pellet: 1.5,
  pelletRadius: 1,
  foodSmell: 1.6,
  smellDecay: 0.998,
  bite: 0.005,
})
export const TONES = Object.freeze([0.45, 1.1, 2.4])

const R = DISH / 2
export function inside(cx, cy) {
  const x = Math.abs(cx + 0.5 - R) / R
  const y = Math.abs(cy + 0.5 - R) / R
  return x ** SHAPE_N + y ** SHAPE_N <= 1
}
export function rimGrid() {
  const g = Array.from({ length: DISH }, () => Array(DISH).fill(false))
  for (let y = 0; y < DISH; y++)
    for (let x = 0; x < DISH; x++)
      g[y][x] =
        inside(x, y) &&
        !(inside(x - 1, y) && inside(x + 1, y) && inside(x, y - 1) && inside(x, y + 1))
  return g
}
export const MASK = (() => {
  const m = new Uint8Array(DISH * DISH)
  for (let y = 0; y < DISH; y++)
    for (let x = 0; x < DISH; x++) m[y * DISH + x] = inside(x, y) ? 1 : 0
  return m
})()
export const CELLS = (() => {
  const c = []
  for (let i = 0; i < MASK.length; i++) if (MASK[i]) c.push(i)
  return Int32Array.from(c)
})()
export const FRAME_BYTES = Math.ceil(CELLS.length / 4)

function rng(seed) {
  let a = seed >>> 0
  return () => {
    a = (a + 0x6d2b79f5) >>> 0
    let t = a
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

export function createSlime(seed = 1, p = {}) {
  const params = { ...PARAMS, ...p }
  const rand = rng(seed)
  const n = params.agents
  const ax = new Float32Array(n)
  const ay = new Float32Array(n)
  const ah = new Float32Array(n)
  for (let i = 0; i < n; i++) {
    ax[i] = R + (rand() - 0.5) * params.clump
    ay[i] = R + (rand() - 0.5) * params.clump
    ah[i] = rand() * Math.PI * 2
  }
  return {
    w: DISH,
    trail: new Float32Array(DISH * DISH),
    scratch: new Float32Array(DISH * DISH),
    food: new Float32Array(DISH * DISH),
    smell: new Float32Array(DISH * DISH),
    scratch2: new Float32Array(DISH * DISH),
    ax,
    ay,
    ah,
    rand,
    tick: 0,
    fed: 0,
    p: params,
  }
}

const smell = (s, x, y) => {
  const cx = Math.floor(x)
  const cy = Math.floor(y)
  if (cx < 0 || cy < 0 || cx >= DISH || cy >= DISH) return -1
  const i = cy * DISH + cx
  return MASK[i] ? s.trail[i] + s.smell[i] : -1
}
function blur(src, dst, decay) {
  for (let k = 0; k < CELLS.length; k++) {
    const i = CELLS[k]
    const x = i % DISH
    const y = (i - x) / DISH
    let sum = 0
    let cnt = 0
    for (let dy = -1; dy <= 1; dy++) {
      const yy = y + dy
      if (yy < 0 || yy >= DISH) continue
      for (let dx = -1; dx <= 1; dx++) {
        const xx = x + dx
        if (xx < 0 || xx >= DISH) continue
        const j = yy * DISH + xx
        if (!MASK[j]) continue
        sum += src[j]
        cnt++
      }
    }
    dst[i] = (sum / cnt) * decay
  }
}

export function step(s) {
  const { p, trail, food, ax, ay, ah, rand } = s
  const n = ax.length
  const { sensorAngle: SA, sensorDist: SD } = p
  for (let i = 0; i < n; i++) {
    let a = ah[i]
    const x = ax[i]
    const y = ay[i]
    const l = smell(s, x + Math.cos(a - SA) * SD, y + Math.sin(a - SA) * SD)
    const c = smell(s, x + Math.cos(a) * SD, y + Math.sin(a) * SD)
    const r = smell(s, x + Math.cos(a + SA) * SD, y + Math.sin(a + SA) * SD)
    if (c >= l && c >= r) {
    } else if (l > r) a -= p.turn
    else if (r > l) a += p.turn
    else a += (rand() - 0.5) * p.jitter
    const nx = x + Math.cos(a)
    const ny = y + Math.sin(a)
    const cx = Math.floor(nx)
    const cy = Math.floor(ny)
    if (cx >= 0 && cy >= 0 && cx < DISH && cy < DISH && MASK[cy * DISH + cx]) {
      ax[i] = nx
      ay[i] = ny
      ah[i] = a
      const ci = cy * DISH + cx
      trail[ci] += p.deposit
      if (food[ci] > 0) food[ci] = Math.max(0, food[ci] - p.bite)
    } else {
      ah[i] = a + Math.PI + (rand() - 0.5) * p.jitter
    }
  }
  const sm = s.smell
  let fed = 0
  for (let k = 0; k < CELLS.length; k++) {
    const i = CELLS[k]
    if (food[i] > 0) {
      sm[i] += p.foodSmell * Math.min(1, food[i] / p.pellet)
      fed++
    }
  }
  s.fed = fed
  blur(sm, s.scratch2, p.smellDecay)
  s.smell = s.scratch2
  s.scratch2 = sm
  blur(trail, s.scratch, p.decay)
  s.trail = s.scratch
  s.scratch = trail
  s.tick++
  return s
}

export function feed(s, cx, cy) {
  if (!Number.isInteger(cx) || !Number.isInteger(cy)) return false
  if (cx < 0 || cy < 0 || cx >= DISH || cy >= DISH || !inside(cx, cy)) return false
  const r = s.p.pelletRadius
  for (let dy = -r; dy <= r; dy++)
    for (let dx = -r; dx <= r; dx++) {
      if (dx * dx + dy * dy > r * r + 1) continue
      const x = cx + dx
      const y = cy + dy
      if (x < 0 || y < 0 || x >= DISH || y >= DISH || !inside(x, y)) continue
      const i = y * DISH + x
      if (!(s.food[i] > 0)) s.fed++
      s.food[i] = s.p.pellet
    }
  return true
}

export function pelletCells(s) {
  const r = s.p.pelletRadius
  let n = 0
  for (let dy = -r; dy <= r; dy++)
    for (let dx = -r; dx <= r; dx++) if (dx * dx + dy * dy <= r * r + 1) n++
  return n
}
export function pelletCount(s) {
  return s.fed / pelletCells(s)
}
export const PELLET_CELLS = pelletCells({ p: PARAMS })

export function randomCell(rand) {
  for (;;) {
    const x = Math.floor(rand() * DISH)
    const y = Math.floor(rand() * DISH)
    if (inside(x, y)) return [x, y]
  }
}

const toneOf = (v) => (v > TONES[2] ? 3 : v > TONES[1] ? 2 : v > TONES[0] ? 1 : 0)
export function tones(s) {
  const out = new Uint8Array(CELLS.length)
  for (let k = 0; k < CELLS.length; k++) out[k] = toneOf(s.trail[CELLS[k]])
  return out
}
export function foodCells(s) {
  const out = []
  for (let k = 0; k < CELLS.length; k++) {
    const i = CELLS[k]
    if (s.food[i] > 0) out.push(i % DISH, (i - (i % DISH)) / DISH)
  }
  return out
}
function pack(tn) {
  const b = new Uint8Array(FRAME_BYTES)
  for (let k = 0; k < tn.length; k++) b[k >> 2] |= (tn[k] & 3) << (6 - 2 * (k & 3))
  return b
}
function unpack(b) {
  const tn = new Uint8Array(CELLS.length)
  for (let k = 0; k < tn.length; k++) tn[k] = (b[k >> 2] >> (6 - 2 * (k & 3))) & 3
  return tn
}
const B64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'
function toBase64(bytes) {
  let out = ''
  for (let i = 0; i < bytes.length; i += 3) {
    const a = bytes[i]
    const b = i + 1 < bytes.length ? bytes[i + 1] : 0
    const c = i + 2 < bytes.length ? bytes[i + 2] : 0
    out += B64[a >> 2] + B64[((a & 3) << 4) | (b >> 4)]
    out += i + 1 < bytes.length ? B64[((b & 15) << 2) | (c >> 6)] : '='
    out += i + 2 < bytes.length ? B64[c & 63] : '='
  }
  return out
}
const B64_INV = (() => {
  const t = new Int8Array(128).fill(-1)
  for (let i = 0; i < 64; i++) t[B64.charCodeAt(i)] = i
  return t
})()
function fromBase64(str) {
  let len = str.length
  while (len && str.charCodeAt(len - 1) === 61) len--
  const out = new Uint8Array(Math.floor((len * 3) / 4))
  let o = 0
  for (let i = 0; i < len; i += 4) {
    let n = 0
    for (let k = 0; k < 4; k++) {
      let v = 0
      if (i + k < len) {
        const c = str.charCodeAt(i + k)
        v = c < 128 ? B64_INV[c] : -1
        if (v < 0) return null
      }
      n = (n << 6) | v
    }
    if (o < out.length) out[o++] = (n >> 16) & 255
    if (o < out.length) out[o++] = (n >> 8) & 255
    if (o < out.length) out[o++] = n & 255
  }
  return out
}
export const encode = (s) => ({ g: toBase64(pack(tones(s))), f: foodCells(s) })
const G_LEN = toBase64(new Uint8Array(FRAME_BYTES)).length
export function decode(g, f = []) {
  if (typeof g !== 'string' || g.length !== G_LEN) return null
  const b = fromBase64(g)
  if (!b || b.length !== FRAME_BYTES) return null
  if (!Array.isArray(f) || f.length % 2) return null
  const food = []
  for (let k = 0; k < f.length; k += 2) {
    const x = f[k]
    const y = f[k + 1]
    if (!Number.isInteger(x) || !Number.isInteger(y) || !inside(x, y)) return null
    food.push(x, y)
  }
  return { tones: unpack(b), food }
}

export function encodeDelta(prev, cur) {
  const out = []
  let last = -1
  for (let k = 0; k < cur.length; k++) {
    if (cur[k] === prev[k]) continue
    let v = ((k - last - 1) << 2) | (cur[k] & 3)
    last = k
    for (;;) {
      if (v < 128) {
        out.push(v)
        break
      }
      out.push((v & 127) | 128)
      v >>>= 7
    }
  }
  return toBase64(Uint8Array.from(out))
}
export function applyDelta(tn, d) {
  if (typeof d !== 'string' || d.length % 4) return null
  const b = fromBase64(d)
  if (!b) return null
  const out = Uint8Array.from(tn)
  let k = -1
  let v = 0
  let shift = 0
  for (let i = 0; i < b.length; i++) {
    v |= (b[i] & 127) << shift
    if (b[i] & 128) {
      shift += 7
      if (shift > 21) return null
      continue
    }
    k += (v >>> 2) + 1
    if (k >= out.length) return null
    out[k] = v & 3
    v = 0
    shift = 0
  }
  if (shift) return null
  return out
}

const K_OF = (() => {
  const t = new Int32Array(DISH * DISH).fill(-1)
  for (let k = 0; k < CELLS.length; k++) t[CELLS[k]] = k
  return t
})()
const DIRTY = new Int32Array(CELLS.length * 2)
const sameList = (a, b) => {
  if (a.length !== b.length) return false
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false
  return true
}
export function paintDish(ctx, tn, food, ink, live, prev = null, prevFood = []) {
  if (!prev) {
    ctx.clearRect(0, 0, DISH, DISH)
    for (let t = 1; t <= 3; t++) {
      ctx.fillStyle = ink[t]
      for (let k = 0; k < CELLS.length; k++) {
        if (tn[k] !== t) continue
        const i = CELLS[k]
        const x = i % DISH
        ctx.fillRect(x, (i - x) / DISH, 1, 1)
      }
    }
  } else {
    let n = 0
    for (let k = 0; k < tn.length; k++) if (tn[k] !== prev[k]) DIRTY[n++] = k
    if (!sameList(food, prevFood))
      for (let j = 0; j < prevFood.length; j += 2) {
        const k = K_OF[prevFood[j + 1] * DISH + prevFood[j]]
        if (k >= 0 && n < DIRTY.length) DIRTY[n++] = k
      }
    for (let t = 0; t <= 3; t++) {
      let styled = false
      for (let j = 0; j < n; j++) {
        const k = DIRTY[j]
        if (tn[k] !== t) continue
        const i = CELLS[k]
        const x = i % DISH
        const y = (i - x) / DISH
        if (!t) {
          ctx.clearRect(x, y, 1, 1)
          continue
        }
        if (!styled) {
          ctx.fillStyle = ink[t]
          styled = true
        }
        ctx.fillRect(x, y, 1, 1)
      }
    }
  }
  ctx.fillStyle = live
  for (let k = 0; k < food.length; k += 2) ctx.fillRect(food[k], food[k + 1], 1, 1)
}
