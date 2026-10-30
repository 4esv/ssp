import * as prose from '../shared/faces/prose.js'
import * as mono from '../shared/faces/mono.js'
import { growth, rooms, wave, beats, edges } from './steps.js'

const FACES = { 'Handjet Cells': prose, 'Handjet Cells Mono': mono }
const TPS = 24
const face = (css) => FACES[css.fontFamily.split(',')[0].replace(/["']/g, '').trim()]
const clear = (c) => !c || c === 'transparent' || /rgba\(.*, 0\)$/.test(c)
const norm = (c) => {
  const m = c.match(/^color\(srgb ([\d.]+) ([\d.]+) ([\d.]+)/)
  return m
    ? `rgb(${m
        .slice(1)
        .map((v) => Math.round(v * 255))
        .join(', ')})`
    : c
}

export function read(roots, L, ids = new WeakMap()) {
  const cells = new Map()
  let cur = { tag: '', n: 0 }
  const ground = norm(getComputedStyle(document.documentElement).backgroundColor)
  const put = (px, py, c) => {
    c = norm(c)
    const x = Math.round((px - L.ox) / L.k)
    const y = Math.round((py - L.oy) / L.k)
    if (x < 0 || y < 0 || x >= L.cols || y >= L.rows) return
    const id = `${cur.tag}:${cur.n++}`
    if (c === ground) cells.delete(y * L.cols + x)
    else cells.set(y * L.cols + x, { c, id })
  }
  const fill = (x, y, w, h, c) => {
    for (let j = 0; j < Math.round(h / L.k); j++)
      for (let i = 0; i < Math.round(w / L.k); i++) put(x + i * L.k, y + j * L.k, c)
  }
  const glyphs = (text, css, x, y) => {
    const f = face(css)
    if (!f) return x
    const twice = css.textShadow && css.textShadow !== 'none'
    for (let i = 0; i < text.length; i++) {
      const lig = f.LIGA?.[text.slice(i, i + 2)]
      const g = lig || f.GLYPHS[text[i]]
      if (lig) i++
      if (!g) {
        x += (text[i] === ' ' ? f.SPACE : 0) * L.k
        continue
      }
      g[1].forEach((row, r) => {
        for (let c = 0; c < row.length; c++)
          if (row[c] === '#') {
            put(x + (g[2] + c) * L.k, y + (g[0] + r) * L.k, css.color)
            if (twice) put(x + (g[2] + c + 1) * L.k, y + (g[0] + r) * L.k, css.color)
          }
      })
      x += g[3] * L.k
    }
    return x
  }
  const width = (text, css) => {
    const f = face(css)
    let w = 0
    for (let i = 0; f && i < text.length; i++) {
      const lig = f.LIGA?.[text.slice(i, i + 2)]
      w += lig ? (i++, lig[3]) : text[i] === ' ' ? f.SPACE : f.GLYPHS[text[i]]?.[3] || 0
    }
    return w * L.k
  }
  const content = (el, which) => {
    const css = getComputedStyle(el, which)
    const m = css.content.match(/^"(.*)"$/)
    return m && m[1] ? [m[1].replace(/\\"/g, '"'), css] : null
  }
  const length = (v, full) => {
    let sum = 0
    for (const [, sign, n, unit] of v.matchAll(/([+-]?)\s*([\d.]+)(%|px)?/g))
      sum += (sign === '-' ? -1 : 1) * (unit === '%' ? (n * full) / 100 : +n)
    return sum
  }
  const backgrounds = (css, r) => {
    if (!clear(css.backgroundColor))
      fill(r.left + scrollX, r.top + scrollY, r.width, r.height, css.backgroundColor)
    const imgs = css.backgroundImage.match(/linear-gradient\([^()]*(\([^()]*\)[^()]*)*\)/g) || []
    const pos = css.backgroundPosition.split(', ')
    const size = css.backgroundSize.split(', ')
    const rep = css.backgroundRepeat.split(', ')
    imgs.forEach((g, i) => {
      const cs = (g.match(/(rgba?|color)\([^)]*\)/g) || []).map(norm)
      if (cs.length !== 2 || cs[0] !== cs[1] || (rep[i % rep.length] || '') !== 'no-repeat') return
      const [px, py] = pos[i % pos.length].split(/ (?![^(]*\))/)
      const [sw, sh] = size[i % size.length].split(/ (?![^(]*\))/)
      fill(
        r.left + scrollX + length(px, r.width),
        r.top + scrollY + length(py, r.height),
        length(sw, r.width),
        length(sh, r.height),
        cs[0]
      )
    })
  }
  const visit = (el) => {
    const css = getComputedStyle(el)
    if (
      el.hidden ||
      css.display === 'none' ||
      css.visibility === 'hidden' ||
      el.matches('dialog:not([open]), .strip, [inert]')
    )
      return
    const rects = [...el.getClientRects()]
    if (!rects.length) return
    if (!ids.has(el)) ids.set(el, `e${(ids.n = (ids.n || 0) + 1)}`)
    const outer = cur
    cur = { tag: ids.get(el), n: 0 }
    for (const r of rects) backgrounds(css, r)
    if (el.tagName === 'CANVAS') return void (canvas(el), (cur = outer))
    const first = rects[0]
    const last = rects[rects.length - 1]
    const before = content(el, '::before')
    if (before)
      glyphs(
        before[0],
        before[1],
        first.left + scrollX + parseFloat(css.paddingLeft),
        first.top + scrollY
      )
    const range = document.createRange()
    const over = []
    for (const n of el.childNodes) {
      if (n.nodeType === 1) {
        const mine = cur
        getComputedStyle(n).position === 'absolute' ? over.push(n) : visit(n)
        cur = mine
      }
      if (n.nodeType !== 3) continue
      for (let i = 0; i < n.data.length; i++) {
        if (n.data[i] === ' ' || n.data[i] === '\n') continue
        const two = face(css)?.LIGA?.[n.data.slice(i, i + 2)]
        range.setStart(n, i)
        range.setEnd(n, i + (two ? 2 : 1))
        const r = range.getClientRects()[0]
        if (r) glyphs(n.data.slice(i, i + (two ? 2 : 1)), css, r.left + scrollX, r.top + scrollY)
        if (two) i++
      }
    }
    const mine = cur
    for (const n of over) (visit(n), (cur = mine))
    const after = content(el, '::after')
    if (after)
      glyphs(
        after[0],
        after[1],
        last.right + scrollX - parseFloat(css.paddingRight) - width(after[0], after[1]),
        last.top + scrollY
      )
    if (css.textDecorationLine.includes('underline'))
      for (const r of rects)
        fill(r.left + scrollX, r.top + scrollY + 19 * L.k, r.width, L.k, css.textDecorationColor)
    cur = outer
  }
  const canvas = (cv) => {
    const r = cv.getBoundingClientRect()
    const d = cv.width / Math.round(r.width / L.k)
    const data = cv.getContext('2d').getImageData(0, 0, cv.width, cv.height).data
    for (let y = 0; y < Math.round(r.height / L.k); y++)
      for (let x = 0; x < Math.round(r.width / L.k); x++) {
        const o = (Math.floor(y * d + d / 2) * cv.width + Math.floor(x * d + d / 2)) * 4
        if (data[o + 3] > 127)
          put(
            r.left + scrollX + x * L.k,
            r.top + scrollY + y * L.k,
            `rgb(${data[o]}, ${data[o + 1]}, ${data[o + 2]})`
          )
      }
  }
  roots().forEach(visit)
  return cells
}

const TICK = 1000 / TPS
const beat = (fn) =>
  new Promise((done) => {
    const t = setInterval(() => fn() || (clearInterval(t), done()), TICK)
  })
const unit = () => +getComputedStyle(document.documentElement).getPropertyValue('--px')
function surface(L) {
  const cv = document.createElement('canvas')
  cv.setAttribute('aria-hidden', 'true')
  const d = Math.round(L.k * devicePixelRatio)
  cv.width = L.cols * d
  cv.height = L.rows * d
  Object.assign(cv.style, {
    position: 'absolute',
    left: `${L.ox}px`,
    top: `${L.oy}px`,
    width: `${cv.width / devicePixelRatio}px`,
    height: `${cv.height / devicePixelRatio}px`,
    pointerEvents: 'none',
    zIndex: 4,
  })
  document.body.append(cv)
  const g = cv.getContext('2d')
  return {
    cv,
    draw: (list) => {
      g.clearRect(0, 0, cv.width, cv.height)
      for (const [x, y, c] of list) ((g.fillStyle = c), g.fillRect(x * d, y * d, d, d))
    },
  }
}

export async function unfold(block, open, { from, line }) {
  const k = unit()
  const steps = (shown) => {
    const m = parseFloat(getComputedStyle(block).marginTop)
    const total = m + block.scrollHeight
    const hs = rooms(total, line, shown)
    let i = 0
    block.classList.add('opening')
    const set = (h) => (
      (block.style.marginTop = `${Math.min(m, h)}px`),
      (block.style.height = `${Math.max(0, h - m)}px`)
    )
    set(shown ? 0 : total)
    return beat(() => {
      set(hs[i++])
      if (i < hs.length) return true
      block.classList.remove('opening')
      block.style.marginTop = block.style.height = ''
    })
  }
  const lattice = () => {
    const r = block.getBoundingClientRect()
    const doc = block.closest('.doc').getBoundingClientRect()
    return {
      k,
      ox: doc.left + scrollX,
      oy: r.top + scrollY,
      cols: Math.round(doc.width / k) + 1,
      rows: Math.round(r.height / k) + 1,
    }
  }
  const cellsOf = (L) =>
    [...read(() => [block], L)].map(([key, v]) => [key % L.cols, Math.floor(key / L.cols), v.c])
  const run = async (L, cells, forward) => {
    if (!cells.length) return { cv: { remove() {} } }
    const depth = growth(cells, Math.round((from.x - L.ox) / k), Math.round((from.y - L.oy) / k))
    const w = wave(depth, forward)
    const s = surface(L)
    let t = 0
    const frame = () => {
      s.draw(cells.filter((_, i) => w.lit(depth[i], t)))
      return w.more(t++)
    }
    frame()
    block.style.visibility = 'hidden'
    await beat(frame)
    return s
  }
  if (open) {
    block.style.visibility = 'hidden'
    block.hidden = false
    await steps(true)
    const L = lattice()
    block.style.visibility = ''
    const cells = cellsOf(L)
    block.style.visibility = 'hidden'
    const s = await run(L, cells, true)
    block.style.visibility = ''
    s.cv.remove()
  } else {
    const L = lattice()
    ;(await run(L, cellsOf(L), false)).cv.remove()
    await steps(false)
    block.hidden = true
    block.style.visibility = ''
  }
}

export async function glide(grp, from, to) {
  const k = unit()
  const g = grp.getBoundingClientRect()
  const span = (b) => {
    const r = b.getBoundingClientRect()
    return [r.left - g.left + 2 * k, r.right - g.left - k]
  }
  const [a, b] = [span(from), span(to)]
  const right = b[0] > a[0]
  const n = beats(b[0] - a[0], k)
  const ghost = grp.cloneNode(true)
  ghost.classList.add('ghost')
  ghost.setAttribute('aria-hidden', 'true')
  ghost.inert = true
  for (const el of ghost.querySelectorAll('[id]')) el.removeAttribute('id')
  grp.append(ghost)
  grp.classList.add('gliding')
  const h = ghost.getBoundingClientRect().height
  const snap = (x) => Math.round(x / k) * k
  const at = (t) => {
    const [l, r] = edges(t, right)
    const x0 = snap(a[0] + (b[0] - a[0]) * l)
    const x1 = snap(a[1] + (b[1] - a[1]) * r)
    ghost.style.clipPath = `inset(${4 * k}px ${g.width - x1}px ${h - 19 * k}px ${x0}px)`
  }
  let i = 0
  at(0)
  await beat(() => (at(++i / n), i < n))
  ghost.remove()
  grp.classList.remove('gliding')
}
