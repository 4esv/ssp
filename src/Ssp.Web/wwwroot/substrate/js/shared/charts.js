import { NONE, DIM, INK, LINE, width, whole, prose, mono } from './lattice.js'

export const LAYOUT = { fontSize: 17, padding: 1, autoDirection: true, fontFamily: 'Handjet Cells' }
export const measurer = { width: (text, size, style) => width(text, style & 4 ? mono : prose) }
export const MOST = 600
export const HINT = 'point at a part, or Tab here and use the arrows'
export const READ = 16
export const sizeOf = (vw, vh, W, chart, says = []) => ({
  rows: whole((chart ? LINE : 0) + Math.ceil(vh) + 2),
  cols: Math.max(
    W,
    Math.ceil(vw) + 2,
    ...(chart ? [HINT, ...says].map((s) => READ + width(s, mono)) : [])
  ),
})

const clean = (v) => Number(v.toPrecision(12))
export function ticks(max, n = 4) {
  if (!(max > 0)) return { step: 1, top: 1, list: [0, 1] }
  const raw = max / n
  const p = 10 ** Math.floor(Math.log10(raw))
  const step = clean([1, 2, 2.5, 5, 10].map((m) => m * p).find((s) => s >= raw))
  const top = clean(Math.ceil(clean(max / step)) * step)
  return {
    step,
    top,
    list: Array.from({ length: Math.round(top / step) + 1 }, (_, i) => clean(i * step)),
  }
}

export function wrap(text, w) {
  const out = []
  for (const word of text.split(' ')) {
    const tail = out.length && `${out[out.length - 1]} ${word}`
    if (tail && width(tail) <= w) out[out.length - 1] = tail
    else out.push(word)
  }
  return out
}

export const running = (values) => values.reduce((a, v) => [...a, (a.at(-1) || 0) + v], [])
export const cuts = (values) => {
  const sum = values.reduce((a, b) => a + b, 0)
  return values.reduce((a, v) => [...a, (a.at(-1) || 0) + v / sum], [])
}
export const turn = (x, y, cx, cy) => (Math.atan2(x - cx, cy - y) / (2 * Math.PI) + 1) % 1

const RAMP = [16, 7, 12, 4, 14, 9, 2, 11]
export function paint(v, o = 1, line = false) {
  if (!v || v === 'none' || v === 'transparent') return null
  const level = (l) => Math.max(1, Math.round(l * o))
  const s = v.match(/--pele-series-(\d+)/)
  if (s) return [INK, line ? 16 : level(RAMP[(s[1] - 1) % RAMP.length])]
  if (/--_bg\b/.test(v)) return [NONE, 16]
  if (/--_(s|a)\b/.test(v)) return line ? [DIM, 8] : null
  if (/--_(m|b)\b/.test(v)) return [DIM, level(16)]
  return [INK, level(16)]
}

export const CHARTS =
  /pele-(pie|xychart|quadrantChart|sankey|radar|treemap|gantt|timeline|journey|venn)\b/

export function sourceOf(source) {
  const values = new Map(
    [...source.matchAll(/^\s*"([^"]+)"\s*:\s*([\d.]+)\s*$/gm)].map((m) => [m[1], m[2]])
  )
  const xs = (source.match(/x-axis\s*\[([^\]]*)\]/) || ['', ''])[1]
    .split(',')
    .map((s) => s.trim().replace(/^"|"$/g, ''))
  const ys = (source.match(/^\s*bar\s*\[([^\]]*)\]/m) || ['', ''])[1]
    .split(',')
    .map((s) => s.trim())
  for (const [i, x] of xs.entries()) if (ys[i]) values.set(x, ys[i])
  return { values, xs }
}

export function said(words, id, v) {
  const say = [...new Set(words.length ? words : [id])].join(' ')
  return v && !say.includes(v) ? `${say}  ${v}` : say
}

export const ends = (id, nodes) =>
  nodes.filter((n) => id.startsWith(`L_${n}_`) || id.includes(`_${n}_`))
