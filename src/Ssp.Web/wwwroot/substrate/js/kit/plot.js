import { figure, width, mono, INK, DIM, LINE, lit } from './lattice.js'
import { pick } from './touch.js'
import { ticks, wrap, running, cuts as cutsOf, turn as turnOf } from '../shared/charts.js'

const RAMP = [16, 10, 6, 3, 1]
const GAP = 6

export function plot(host, { kind, labels, values, say }) {
  const cols = () =>
    Math.floor(
      host.parentElement.clientWidth /
        +getComputedStyle(document.documentElement).getPropertyValue('--px')
    )
  let f
  let sel = -1
  let layout = null
  const readout = (i) => {
    const s = i < 0 ? say(-1) : say(i)
    const old = f.words.querySelector('.readout:not(.dither-swap-out)')
    const n = f.word(s, 0, 15, { face: mono, cls: 'readout' })
    if (old) {
      old.classList.add('dither-swap-out')
      n.classList.add('dither-swap-in')
      old.addEventListener('animationend', () => old.remove(), { once: true })
    }
  }

  function build() {
    const W = Math.min(cols(), 600)
    if (kind === 'bars') {
      const t = ticks(Math.max(...values))
      const gutter = Math.max(...t.list.map((v) => width(String(v), mono))) + 8
      const slot = Math.floor((W - gutter) / values.length)
      const fits = labels.every((l) => l.split(' ').every((w) => width(w) <= slot - 2))
      if (fits) {
        const plotTop = LINE + 10
        const plotH = 6 * LINE
        const base = plotTop + plotH
        const lines = Math.max(...labels.map((l) => wrap(l, slot - 2).length))
        f = figure(host, W, base + 4 + lines * LINE)
        f.reset()
        const y = (v) => base - Math.round((v / t.top) * plotH)
        for (const v of t.list)
          f.word(String(v), gutter - 6, y(v) + 5, { face: mono, align: 'right', cls: 'dim' })
        labels.forEach((l, i) =>
          wrap(l, slot - 2).forEach((ln, j) =>
            f.word(ln, gutter + i * slot + slot / 2, base + 4 + j * LINE + 16, {
              align: 'center',
              cls: 'dim',
            })
          )
        )
        layout = {
          hit: (x) => Math.floor((x - gutter) / slot),
          draw(s) {
            for (const v of t.list.slice(1)) for (let x = gutter; x < W; x += 2) f.put(x, y(v), DIM)
            for (let x = gutter; x < W; x++) f.put(x, base, DIM)
            values.forEach((v, i) =>
              f.rect(
                gutter + i * slot + GAP / 2,
                y(v),
                slot - GAP,
                base - y(v),
                s < 0 || s === i ? INK : DIM
              )
            )
          },
        }
      } else {
        const lw = Math.max(...labels.map((l) => width(l))) + 8
        const vw = Math.max(...values.map((v) => width(String(v), mono))) + 8
        const stacked = lw + vw + 40 > W
        const per = stacked ? 2 : 1
        f = figure(host, W, LINE + values.length * per * LINE)
        f.reset()
        const room = stacked ? W - vw : W - lw - vw
        const x0 = stacked ? 0 : lw
        const top = (i) => LINE + i * per * LINE + (per - 1) * LINE
        labels.forEach((l, i) => f.word(l, 0, LINE + i * per * LINE + 16, { cls: 'dim' }))
        values.forEach((v, i) =>
          f.word(String(v), x0 + Math.round((v / Math.max(...values)) * room) + 6, top(i) + 16, {
            face: mono,
            cls: 'dim',
          })
        )
        layout = {
          hit: (x, yy) => Math.floor((yy - LINE) / (per * LINE)),
          draw(s) {
            values.forEach((v, i) =>
              f.rect(
                x0,
                top(i) + 4,
                Math.max(1, Math.round((v / Math.max(...values)) * room)),
                15,
                s < 0 || s === i ? INK : DIM
              )
            )
          },
        }
      }
    }
    if (kind === 'line') {
      const run = running(values)
      const t = ticks(run.at(-1))
      const gutter = Math.max(...t.list.map((v) => width(String(v), mono))) + 8
      const plotTop = LINE + 10
      const plotH = 6 * LINE
      const base = plotTop + plotH
      const step = Math.floor((W - gutter - 8) / (run.length - 1 || 1))
      f = figure(host, W, base + 4 + LINE)
      f.reset()
      const y = (v) => base - Math.round((v / t.top) * plotH)
      const pts = run.map((v, i) => [gutter + 4 + i * step, y(v)])
      for (const v of t.list)
        f.word(String(v), gutter - 6, y(v) + 5, { face: mono, align: 'right', cls: 'dim' })
      pts.forEach(([x], i) =>
        f.word(String(i + 1), x, base + 4 + 16, { face: mono, align: 'center', cls: 'dim' })
      )
      layout = {
        hit: (x) => Math.round((x - gutter - 4) / step),
        draw(s) {
          const upto = s < 0 ? pts.length - 1 : s
          for (const v of t.list.slice(1)) for (let x = gutter; x < W; x += 2) f.put(x, y(v), DIM)
          for (let x = gutter; x < W; x++) f.put(x, base, DIM)
          for (let i = 1; i < pts.length; i++) f.line(pts[i - 1], pts[i], i <= upto ? INK : DIM)
          pts.forEach(([x, yy], i) => f.rect(x - 1, yy - 1, 3, 3, i <= upto ? INK : DIM))
          if (s >= 0) for (let yy = pts[s][1] + 3; yy < base; yy += 2) f.put(pts[s][0], yy, INK)
        },
      }
    }
    if (kind === 'pie') {
      const sum = values.reduce((a, b) => a + b, 0)
      const R = Math.min(3 * LINE + 9, Math.floor((W - 2) / 2))
      const side = W < 2 * R + 160
      const cx = R + 1
      const cy = LINE + 10 + R
      const legendX = side ? 0 : 2 * R + 24
      const legendY = side ? cy + R + 14 : LINE + 10
      const need = Math.max(...labels.map((l) => 18 + width(l) + 8 + width('100%')))
      f = figure(
        host,
        side ? Math.max(W, need) : W,
        side ? legendY + values.length * LINE : cy + R + 10
      )
      f.reset()
      const cuts = cutsOf(values)
      labels.forEach((l, i) => {
        const yy = legendY + i * LINE
        f.word(l, legendX + 18, yy + 16, { cls: 'dim' })
        f.word(`${Math.round((values[i] / sum) * 100)}%`, legendX + 18 + width(l) + 8, yy + 16, {
          face: mono,
          cls: 'dim',
        })
      })
      layout = {
        hit: (x, yy) => {
          if (x >= legendX && yy >= legendY) return Math.floor((yy - legendY) / LINE)
          const turn = turnOf(x, yy, cx, cy)
          return Math.hypot(x - cx, yy - cy) <= R ? cuts.findIndex((c) => turn <= c) : -1
        },
        draw(s) {
          for (let yy = cy - R; yy <= cy + R; yy++)
            for (let x = cx - R; x <= cx + R; x++) {
              const d = Math.hypot(x - cx, yy - cy)
              if (d > R + 0.5) continue
              const turn = turnOf(x, yy, cx, cy)
              const i = cuts.findIndex((c) => turn <= c)
              const t = s < 0 || s === i ? INK : DIM
              if (Math.abs(d - R) < 0.5) f.put(x, yy, t === INK || s < 0 ? INK : DIM)
              else if (lit(x, yy, RAMP[i % RAMP.length])) f.put(x, yy, t)
            }
          for (const c of [0, ...cuts.slice(0, -1)]) {
            const a = c * 2 * Math.PI
            for (let r = 0; r < R; r += 0.5) {
              const x = Math.round(cx + Math.sin(a) * r)
              const yy = Math.round(cy - Math.cos(a) * r)
              f.put(x, yy, 0)
            }
          }
          values.forEach((_, i) =>
            f.rect(
              legendX,
              legendY + i * LINE + 5,
              11,
              11,
              s < 0 || s === i ? INK : DIM,
              RAMP[i % RAMP.length]
            )
          )
        },
      }
    }
    readout(sel)
    layout.draw(sel)
    f.show(true)
  }

  const choose = (i) => {
    i = i >= values.length ? values.length - 1 : i
    if (i === sel) return
    sel = i
    f.clear()
    layout.draw(sel)
    f.show()
    readout(sel)
  }
  const at = (e) => {
    const r = host.getBoundingClientRect()
    const k = +getComputedStyle(document.documentElement).getPropertyValue('--px')
    return [(e.clientX - r.left) / k, (e.clientY - r.top) / k]
  }
  pick(host, (e) => Math.max(-1, layout.hit(...at(e))), choose, -1)
  host.addEventListener('keydown', (e) => {
    const d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key]
    if (d)
      (e.preventDefault(),
        choose(
          Math.max(
            0,
            Math.min(values.length - 1, (sel < 0 ? (d > 0 ? -1 : values.length) : sel) + d)
          )
        ))
    if (e.key === 'Escape') choose(-1)
  })
  host.addEventListener('blur', () => choose(-1))
  host.tabIndex = 0
  build()
  return { build, redraw: () => f.redraw() }
}
