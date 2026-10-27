import * as prose from './faces/prose.js'
import * as mono from './faces/mono.js'

export const NONE = 0
export const DIM = 1
export const INK = 2
export const LINE = 20
export const BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5]
export const lit = (x, y, level) => BAYER[(y & 3) * 4 + (x & 3)] < level

export function width(text, face = prose) {
  let w = 0
  for (const c of text) w += c === ' ' ? face.SPACE : (face.GLYPHS[c] || face.GLYPHS['?'])[3]
  return w
}
export { prose, mono }

export const whole = (rows) => Math.ceil(rows / LINE) * LINE

export const left = (w, x, align = 'left') =>
  Math.round(align === 'center' ? x - w / 2 : align === 'right' ? x - w : x)

export function line([x0, y0], [x1, y1]) {
  ;[x0, y0, x1, y1] = [x0, y0, x1, y1].map(Math.round)
  const out = []
  const dx = Math.abs(x1 - x0)
  const dy = -Math.abs(y1 - y0)
  const sx = x0 < x1 ? 1 : -1
  const sy = y0 < y1 ? 1 : -1
  let e = dx + dy
  for (;;) {
    out.push([x0, y0])
    if (x0 === x1 && y0 === y1) return out
    const e2 = 2 * e
    if (e2 >= dy) ((e += dy), (x0 += sx))
    if (e2 <= dx) ((e += dx), (y0 += sy))
  }
}

export function mix(from, to, cols, level) {
  const out = new Uint8Array(to.length)
  for (let i = 0; i < to.length; i++)
    out[i] = lit(i % cols, Math.floor(i / cols), level) ? to[i] : from[i]
  return out
}
