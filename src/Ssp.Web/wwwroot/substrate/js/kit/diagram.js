import { register, render } from '../pele/esm/core.js'
import flowchart from '../pele/esm/diagrams/flowchart.js'
import sequence from '../pele/esm/diagrams/sequence.js'
import pie from '../pele/esm/diagrams/pie.js'
import xychart from '../pele/esm/diagrams/xychart.js'
import quadrant from '../pele/esm/diagrams/quadrantChart.js'
import sankey from '../pele/esm/diagrams/sankey.js'
import radar from '../pele/esm/diagrams/radar.js'
import treemap from '../pele/esm/diagrams/treemap.js'
import gantt from '../pele/esm/diagrams/gantt.js'
import timeline from '../pele/esm/diagrams/timeline.js'
import journey from '../pele/esm/diagrams/journey.js'
import venn from '../pele/esm/diagrams/venn.js'
import { figure, width, prose, mono, INK, DIM, NONE, LINE, lit, unit } from './lattice.js'
import { pick } from './touch.js'
import { paint, CHARTS, sourceOf, said as sayOf, ends } from '../shared/charts.js'

register(
  flowchart,
  sequence,
  pie,
  xychart,
  quadrant,
  sankey,
  radar,
  treemap,
  gantt,
  timeline,
  journey,
  venn
)
const measurer = { width: (text, size, style) => width(text, style & 4 ? mono : prose) }

const attr = (el, name) => {
  for (let n = el; n && n.getAttribute; n = n.parentNode)
    if (n.hasAttribute(name)) return n.getAttribute(name)
  return null
}
const opacity = (el) => {
  let o = 1
  for (let n = el; n && n.getAttribute; n = n.parentNode)
    o *= +(n.getAttribute('fill-opacity') ?? n.getAttribute('opacity') ?? 1)
  return o
}
export function diagram(host, source) {
  let f
  let items = []
  let chosen = null
  let chart = false
  let where = new Map()

  function build() {
    const W = Math.min(Math.floor(host.parentElement.clientWidth / unit()), 600)
    const { svg } = render(source, {
      measurer,
      fontSize: 17,
      padding: 1,
      maxWidth: W - 2,
      autoDirection: true,
      fontFamily: 'Handjet Cells',
    })
    const tmp = document.createElement('div')
    tmp.style.cssText = 'position:absolute;left:0;top:0;visibility:hidden;pointer-events:none'
    tmp.innerHTML = svg
    document.body.append(tmp)
    const root = tmp.querySelector('svg')
    chart = CHARTS.test(root.getAttribute('class'))
    const { values, xs } = sourceOf(source)
    const [, , vw, vh] = root.getAttribute('viewBox').split(' ').map(Number)
    Object.assign(root.style, {
      width: `${vw}px`,
      height: `${vh}px`,
      maxWidth: 'none',
      fontFamily: 'Handjet Cells',
    })
    root.querySelectorAll('rect').forEach((r) => (r.style.rx = '0'))
    const left = Math.max(0, Math.round((W - vw) / 2))
    const top = chart ? LINE : 0
    const cols = Math.max(W, Math.ceil(vw) + 2)
    f = figure(host, cols, top + Math.ceil(vh) + 2)
    f.reset()
    const base = root.getScreenCTM().inverse()
    const toLocal = (el) => base.multiply(el.getScreenCTM())
    const snap = (p) => [left + Math.floor(p.x), top + Math.floor(p.y)]

    const stroke = (el) => {
      const m = toLocal(el)
      const len = el.getTotalLength()
      const dash = (attr(el, 'stroke-dasharray') || '').split(/[ ,]+/).map(Number).filter(Boolean)
      const out = new Map()
      const put = (x, y) => out.set(y * 1e5 + x, [x, y])
      let prev = null
      for (let s = 0; s <= len + 0.25; s += 0.5) {
        const on = !dash.length || s % (dash[0] + (dash[1] || dash[0])) < dash[0]
        const p = snap(el.getPointAtLength(Math.min(s, len)).matrixTransform(m))
        if (on && prev && Math.max(Math.abs(p[0] - prev[0]), Math.abs(p[1] - prev[1])) <= 2) {
          let [x, y] = prev
          const dx = Math.abs(p[0] - x)
          const dy = -Math.abs(p[1] - y)
          const sx = x < p[0] ? 1 : -1
          const sy = y < p[1] ? 1 : -1
          let e = dx + dy
          for (;;) {
            put(x, y)
            if (x === p[0] && y === p[1]) break
            const e2 = 2 * e
            if (e2 >= dy) ((e += dy), (x += sx))
            if (e2 <= dx) ((e += dx), (y += sy))
          }
        } else if (on) put(...p)
        prev = on ? p : null
      }
      return [...out.values()]
    }
    const fill = (el) => {
      const m = toLocal(el)
      const inv = m.inverse()
      const b = el.getBBox()
      const pts = [
        [b.x, b.y],
        [b.x + b.width, b.y],
        [b.x, b.y + b.height],
        [b.x + b.width, b.y + b.height],
      ].map(([x, y]) => new DOMPoint(x, y).matrixTransform(m))
      const xs = pts.map((p) => p.x)
      const ys = pts.map((p) => p.y)
      const out = []
      for (let y = Math.floor(Math.min(...ys)) - 1; y <= Math.ceil(Math.max(...ys)) + 1; y++)
        for (let x = Math.floor(Math.min(...xs)) - 1; x <= Math.ceil(Math.max(...xs)) + 1; x++)
          if (el.isPointInFill(new DOMPoint(x + 0.5, y + 0.5).matrixTransform(inv)))
            out.push([left + x, top + y])
      return out
    }

    const named = new Map()
    const itemOf = (el) => {
      const g = el.closest('[data-id]')
      if (!g || g === root) return null
      const id = g.dataset.id
      if (!named.has(id))
        named.set(id, { id, say: said(g, id), node: g.matches('.pele-node, .pele-actor') })
      return named.get(id)
    }
    const said = (g, id) => {
      const t = g.querySelector('title')
      if (t) return t.textContent
      const words = [...root.querySelectorAll('[data-id]')]
        .filter((e) => e.dataset.id === id)
        .flatMap((e) => [...e.querySelectorAll('text')].map((x) => x.textContent.trim()))
        .filter(Boolean)
      return sayOf(words, id, values.get(id))
    }
    root
      .querySelectorAll('.pele-bars rect')
      .forEach((r, i) => xs[i] && r.setAttribute('data-id', xs[i]))
    const ops = []
    where = new Map()
    const mark = (it, x, y) => it && where.set(y * cols + x, it)
    for (const el of root.querySelectorAll(
      'path, rect, circle, ellipse, line, polyline, polygon'
    )) {
      if (el.closest('defs, clipPath, mask')) continue
      const item = itemOf(el)
      const fp = paint(attr(el, 'fill'), opacity(el))
      const sp = paint(attr(el, 'stroke'), 1, true)
      const px = fp || (item && item.node) ? fill(el) : null
      if (px) for (const [x, y] of px) mark(item, x, y)
      if (fp) ops.push({ item, kind: 'fill', px, tone: fp[0], level: fp[1] })
      if (sp && +(attr(el, 'stroke-width') ?? 1) > 0) {
        const px = stroke(el)
        if (item)
          for (const [x, y] of px)
            for (const [dx, dy] of [
              [0, 0],
              [1, 0],
              [-1, 0],
              [0, 1],
              [0, -1],
            ])
              mark(item, x + dx, y + dy)
        ops.push({ item, kind: 'stroke', px, tone: sp[0], level: sp[1] })
      }
    }
    const nodes = [...named.values()].filter((n) => n.node)
    for (const it of named.values())
      it.ends = it.node
        ? [it.id]
        : ends(
            it.id,
            nodes.map((n) => n.id)
          )

    const plates = []
    for (const t of root.querySelectorAll('text')) {
      const m = toLocal(t)
      const a = attr(t, 'text-anchor') || 'start'
      const face = t.closest('.pele-mono') ? mono : prose
      const muted = (n) =>
        /--_(m|b)\b/.test(attr(n.nodeType === 1 ? n : n.parentNode, 'fill') || '')
      const placed = [...t.querySelectorAll('tspan')].filter((s) => s.hasAttribute('y'))
      const lines = placed.length ? placed.map((s) => [s, [s]]) : [[t, [...t.childNodes]]]
      for (const [at, runs] of lines) {
        const parts = runs
          .map((n) => [n.textContent.replace(/\s+/g, ' '), muted(n)])
          .filter(([s]) => s)
        if (!parts.length) continue
        parts[0][0] = parts[0][0].trimStart()
        parts[parts.length - 1][0] = parts[parts.length - 1][0].trimEnd()
        const w = parts.reduce((n, [s]) => n + width(s, face), 0)
        if (!w) continue
        const p = new DOMPoint(
          +at.getAttribute('x') || +t.getAttribute('x') || 0,
          +at.getAttribute('y') || +t.getAttribute('y') || 0
        ).matrixTransform(m)
        const x = left + p.x
        let x0 = Math.round(a === 'middle' ? x - w / 2 : a === 'end' ? x - w : x)
        const y = top + Math.round(p.y)
        plates.push([x0 - 1, y - 13, w + 2, 17])
        for (const [s, dim] of parts) {
          const lead = s.length - s.trimStart().length
          if (s.trim())
            f.word(s.trim(), x0 + width(s.slice(0, lead), face), y, { face, cls: dim ? 'dim' : '' })
          x0 += width(s, face)
        }
      }
    }
    tmp.remove()
    items = [...named.values()]

    const on = (it) => {
      if (!chosen || !it) return false
      if (it === chosen) return true
      if (chosen.node)
        return (
          it.ends.includes(chosen.id) ||
          (it.node &&
            items.some((e) => !e.node && e.ends.includes(chosen.id) && e.ends.includes(it.id)))
        )
      return it.node && chosen.ends.includes(it.id)
    }
    f.draw = () => {
      f.clear()
      for (const op of ops) {
        let tone = op.tone
        if (tone !== NONE && op.item)
          tone = on(op.item) ? INK : chosen || (!chart && op.kind === 'stroke') ? DIM : tone
        for (const [x, y] of op.px) if (lit(x, y, op.level)) f.put(x, y, tone)
      }
      for (const [x, y, w, h] of plates)
        for (let j = y; j < y + h; j++) for (let i = x; i < x + w; i++) f.put(i, j, NONE)
    }
    f.draw()
    f.show(true)
    if (chart) say()
  }

  const say = () => {
    const text = chosen ? chosen.say : 'point at a part, or Tab here and use the arrows'
    const old = f.words.querySelector('.readout:not(.dither-swap-out)')
    const n = f.word(text, 8, 15, { face: mono, cls: `readout${chosen ? '' : ' dim'}` })
    if (old) {
      old.classList.add('dither-swap-out')
      n.classList.add('dither-swap-in')
      old.addEventListener('animationend', () => old.remove(), { once: true })
    }
  }
  const choose = (it) => {
    if (it === chosen) return
    chosen = it
    f.draw()
    f.show()
    if (chart) say()
  }
  const hit = (e) => {
    const r = host.getBoundingClientRect()
    const k = unit()
    const x = Math.floor((e.clientX - r.left + host.scrollLeft) / k)
    const y = Math.floor((e.clientY - r.top) / k)
    return where.get(y * f.cols + x) || null
  }
  pick(host, hit, choose, null)
  host.tabIndex = 0
  host.addEventListener('keydown', (e) => {
    const d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key]
    if (!d) return e.key === 'Escape' && choose(null)
    e.preventDefault()
    const list = items.filter((it) => it.node || chart)
    if (list.length) choose(list[(list.indexOf(chosen) + d + list.length) % list.length])
  })
  host.addEventListener('blur', () => choose(null))
  build()
  return { build, redraw: () => f.redraw() }
}
