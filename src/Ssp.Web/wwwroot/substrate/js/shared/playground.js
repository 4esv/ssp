export const WORLD = Object.freeze({ w: 1920, h: 1080 })

export const SCALE = 0.85

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v))

export function clampPan(x, y, vw, vh) {
  return [clamp(x, 0, Math.max(0, WORLD.w - vw)), clamp(y, 0, Math.max(0, WORLD.h - vh))]
}

export function toFrac(x, y) {
  if (!(x >= 0 && x < WORLD.w && y >= 0 && y < WORLD.h)) return null
  return [x / WORLD.w, y / WORLD.h]
}

export const fromFrac = (fx, fy) => [fx * WORLD.w, fy * WORLD.h]

export const EMPTY = '.........'
const LINES = [
  [0, 1, 2],
  [3, 4, 5],
  [6, 7, 8],
  [0, 3, 6],
  [1, 4, 7],
  [2, 5, 8],
  [0, 4, 8],
  [2, 4, 6],
]

export function turnOf(b) {
  let x = 0
  let o = 0
  for (const c of b) {
    if (c === 'x') x++
    else if (c === 'o') o++
  }
  return x > o ? 'o' : 'x'
}

export function winnerOf(b) {
  for (const [a, c, d] of LINES) if (b[a] !== '.' && b[a] === b[c] && b[a] === b[d]) return b[a]
  return b.includes('.') ? '' : '='
}

export function lineOf(b) {
  for (const l of LINES) if (b[l[0]] !== '.' && b[l[0]] === b[l[1]] && b[l[0]] === b[l[2]]) return l
  return null
}

export const ROCK_MS = 2400

export function tiltOf(l, r, t) {
  if (l > r) return -1
  if (r > l) return 1
  if (!l) return 0
  return Math.sin((t / ROCK_MS) * 2 * Math.PI)
}
