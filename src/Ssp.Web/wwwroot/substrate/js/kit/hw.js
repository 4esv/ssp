import { figure, INK, DIM, NONE } from './lattice.js'
import * as K from './controls.js'
import { GLYPHS } from './icons.js'
import { cellgrid, file } from './cellgrid.js'

const { $ } = K
const NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']
const TYPING = 'awsedftgyhujk'
let octave = 4
let wave = 'sine'
let decay = 4
let pad = { x: 60, y: 30 }
const played = []

let ac = null
let filter = null
let analyser = null
function audio() {
  if (!ac) {
    ac = new AudioContext()
    filter = ac.createBiquadFilter()
    filter.type = 'lowpass'
    analyser = ac.createAnalyser()
    analyser.fftSize = 512
    const master = ac.createGain()
    master.gain.value = 0.18
    filter.connect(analyser).connect(master).connect(ac.destination)
    bend()
  }
  ac.resume?.()
  return ac
}
function bend() {
  if (!filter) return
  filter.frequency.value = 200 * 40 ** (pad.x / 100)
  filter.Q.value = 0.5 + (pad.y / 100) * 14.5
}
function note(midi) {
  const a = audio()
  const o = a.createOscillator()
  const g = a.createGain()
  const t = a.currentTime
  const len = 0.15 + decay * 0.18
  o.type = wave
  o.frequency.value = 440 * 2 ** ((midi - 69) / 12)
  g.gain.setValueAtTime(0, t)
  g.gain.linearRampToValueAtTime(0.9, t + 0.01)
  g.gain.exponentialRampToValueAtTime(0.001, t + len)
  o.connect(g).connect(filter)
  o.start(t)
  o.stop(t + len + 0.05)
  played.push(NAMES[midi % 12] + (Math.floor(midi / 12) - 1))
  if (played.length > 8) played.shift()
  live()
}

let knobF = null
function circle(f, cx, cy, r, t, gapFrom = 0.375, gapTo = 0.625) {
  for (let a = 0; a < 360; a += 2) {
    const turn = a / 360
    if (turn > gapFrom && turn < gapTo) continue
    const rad = (a * Math.PI) / 180
    f.put(cx + Math.round(Math.sin(rad) * r), cy - Math.round(Math.cos(rad) * r), t)
  }
}
function drawKnob(first = false) {
  const host = $('#knob')
  if (!knobF) ((knobF = figure(host, 41, 41)), (first = true))
  knobF.clear()
  circle(knobF, 20, 20, 17, DIM, 0.375, 0.625)
  const turn = -135 + (decay / 7) * 270
  const rad = (turn * Math.PI) / 180
  knobF.line(
    [20, 20],
    [20 + Math.round(Math.sin(rad) * 13), 20 - Math.round(Math.cos(rad) * 13)],
    INK
  )
  knobF.rect(19, 19, 3, 3, INK)
  knobF.show(first)
  host.setAttribute('aria-valuenow', decay)
  host.setAttribute('aria-valuetext', `decay ${decay + 1} of 8`)
  $('#knob-t').textContent = `decay ${decay + 1} of 8`
}
function knob() {
  const el = $('#knob')
  const set = (v) => ((decay = (v + 8) % 8), drawKnob(), live())
  let y0 = null
  let moved = false
  el.addEventListener(
    'pointerdown',
    (e) => ((y0 = e.clientY), (moved = false), el.setPointerCapture(e.pointerId))
  )
  el.addEventListener('pointermove', (e) => {
    if (y0 === null) return
    const steps = Math.trunc((y0 - e.clientY) / 12)
    if (steps) ((y0 -= steps * 12), (moved = true), set(Math.max(0, Math.min(7, decay + steps))))
  })
  el.addEventListener('pointerup', () => (!moved && set(decay + 1), (y0 = null)))
  el.addEventListener('keydown', (e) => {
    const d = { ArrowUp: 1, ArrowRight: 1, ArrowDown: -1, ArrowLeft: -1 }[e.key]
    if (d) (e.preventDefault(), set(Math.max(0, Math.min(7, decay + d))))
    if (e.key === 'Home' || e.key === 'End') (e.preventDefault(), set(e.key === 'Home' ? 0 : 7))
  })
  drawKnob(true)
}

let padF = null
const PAD = 80
function drawPad() {
  const host = $('#pad')
  if (!padF) padF = figure(host, PAD, PAD)
  padF.clear()
  for (let i = 0; i < PAD; i += 2)
    (padF.put(i, 0, DIM), padF.put(i, PAD - 1, DIM), padF.put(0, i, DIM), padF.put(PAD - 1, i, DIM))
  const x = Math.round((pad.x / 100) * (PAD - 5)) + 2
  const y = Math.round((1 - pad.y / 100) * (PAD - 5)) + 2
  for (let i = 2; i < PAD - 2; i += 3) (padF.put(i, y, DIM), padF.put(x, i, DIM))
  padF.rect(x - 1, y - 1, 3, 3, INK)
  padF.show(true)
  const hz = Math.round(200 * 40 ** (pad.x / 100))
  const said = `cutoff ${hz >= 1000 ? (hz / 1000).toFixed(1) + ' kHz' : hz + ' Hz'}, resonance ${(0.5 + (pad.y / 100) * 14.5).toFixed(1)}`
  host.setAttribute('aria-valuetext', said)
  host.setAttribute('aria-valuenow', pad.x)
  $('#pad-t').textContent = said
}
function padPart() {
  const el = $('#pad')
  const set = (x, y) => (
    (pad = {
      x: Math.max(0, Math.min(100, Math.round(x))),
      y: Math.max(0, Math.min(100, Math.round(y))),
    }),
    bend(),
    drawPad(),
    live()
  )
  const at = (e) => {
    const r = $('canvas', el).getBoundingClientRect()
    set(((e.clientX - r.left) / r.width) * 100, (1 - (e.clientY - r.top) / r.height) * 100)
  }
  el.addEventListener('pointerdown', (e) => (el.setPointerCapture(e.pointerId), at(e)))
  el.addEventListener('pointermove', (e) => el.hasPointerCapture(e.pointerId) && at(e))
  el.addEventListener('keydown', (e) => {
    const s = e.shiftKey ? 10 : 1
    const d = { ArrowRight: [s, 0], ArrowLeft: [-s, 0], ArrowUp: [0, s], ArrowDown: [0, -s] }[e.key]
    if (d) (e.preventDefault(), set(pad.x + d[0], pad.y + d[1]))
  })
  drawPad(true)
}

let scopeF = null
let scopeT = 0
const SW = 96
const SH = 40
function drawScope() {
  const host = $('#scope')
  if (!scopeF) scopeF = figure(host, SW, SH)
  scopeF.clear()
  for (let x = 0; x < SW; x += 2) scopeF.put(x, SH / 2, DIM)
  let sounding = false
  if (analyser) {
    const d = new Float32Array(analyser.fftSize)
    analyser.getFloatTimeDomainData(d)
    sounding = d.some((v) => Math.abs(v) > 0.003)
    if (sounding) {
      const peak = Math.max(0.05, ...d.map(Math.abs))
      let prev = null
      for (let x = 0; x < SW; x++) {
        const y = Math.round(SH / 2 - (d[Math.floor((x / SW) * d.length)] / peak) * (SH / 2 - 2))
        if (prev) scopeF.line(prev, [x, y], INK)
        prev = [x, y]
      }
    }
  }
  if (!sounding) {
    let prev = null
    for (let x = 0; x < SW; x++) {
      const p = ((x / SW) * 2) % 1
      const v =
        wave === 'sine'
          ? Math.sin(p * 2 * Math.PI)
          : wave === 'square'
            ? p < 0.5
              ? 1
              : -1
            : 1 - 2 * p
      const y = Math.round(SH / 2 - v * (SH / 2 - 4))
      if (prev) scopeF.line(prev, [x, y], DIM)
      prev = [x, y]
    }
  }
  scopeF.show(true)
  $('#scope-t').textContent = sounding ? `${wave}, sounding` : `${wave}, at rest`
  return sounding
}
function live() {
  hwText()
  if (scopeT) return
  scopeT = setInterval(() => drawScope() || (clearInterval(scopeT), (scopeT = 0), drawScope()), 340)
  drawScope()
}

let pianoF = null
const WK = 15
const KEYS = Array.from({ length: 13 }, (_, i) => ({ i, black: [1, 3, 6, 8, 10].includes(i % 12) }))
let cursor = 0
const down = new Set()
function keyBox(i) {
  const whiteBefore = KEYS.slice(0, i).filter((k) => !k.black).length
  return KEYS[i].black ? [whiteBefore * WK - 5, 0, 10, 38] : [whiteBefore * WK, 0, WK + 1, 64]
}
function drawPiano() {
  const host = $('#piano')
  const W = 8 * WK + 1
  if (!pianoF) pianoF = figure(host, W, 72)
  pianoF.clear()
  for (const k of KEYS.filter((k) => !k.black)) {
    const [x, y, w, h] = keyBox(k.i)
    if (down.has(k.i)) pianoF.rect(x, y, w, h, INK)
    else {
      pianoF.line([x, y], [x + w - 1, y], INK)
      pianoF.line([x, y + h - 1], [x + w - 1, y + h - 1], INK)
      pianoF.line([x, y], [x, y + h - 1], INK)
      pianoF.line([x + w - 1, y], [x + w - 1, y + h - 1], INK)
    }
  }
  for (const k of KEYS.filter((k) => k.black)) {
    const [x, y, w, h] = keyBox(k.i)
    pianoF.rect(x, y, w, h, INK)
    if (down.has(k.i)) pianoF.rect(x + 1, y + 1, w - 2, h - 2, NONE)
  }
  if (document.activeElement === host) {
    const [x, , w] = keyBox(cursor)
    pianoF.rect(x + 2, 68, w - 4, 2, INK)
  }
  pianoF.show(true)
  $('#piano-t').textContent =
    `octave ${octave} · ${NAMES[cursor % 12]}${octave + Math.floor(cursor / 12)} · played ${played.join(' ') || 'nothing yet'}`
}
const midiOf = (i) => 12 * (octave + 1) + i
function press(i) {
  down.add(i)
  note(midiOf(i))
  drawPiano()
}
function release(i) {
  down.delete(i)
  drawPiano()
}
function piano() {
  const el = $('#piano')
  const keyAt = (e) => {
    const r = $('canvas', el).getBoundingClientRect()
    const x = ((e.clientX - r.left) / r.width) * (8 * WK + 1)
    const y = ((e.clientY - r.top) / r.height) * pianoF.rows
    const black = KEYS.find(
      (k) => k.black && x >= keyBox(k.i)[0] && x < keyBox(k.i)[0] + 10 && y < 38
    )
    return black
      ? black.i
      : KEYS.find((k) => !k.black && x >= keyBox(k.i)[0] && x < keyBox(k.i)[0] + WK)?.i
  }
  let held = null
  el.addEventListener('pointerdown', (e) => {
    const i = keyAt(e)
    if (i === undefined) return
    el.setPointerCapture(e.pointerId)
    ;((held = i), (cursor = i), press(i))
  })
  el.addEventListener('pointermove', (e) => {
    if (held === null) return
    const i = keyAt(e)
    if (i !== undefined && i !== held) (release(held), (held = i), press(i))
  })
  el.addEventListener('pointerup', () => held !== null && (release(held), (held = null)))
  el.addEventListener('keydown', (e) => {
    if (e.metaKey || e.ctrlKey || e.altKey || e.repeat) return
    const t = TYPING.indexOf(e.key)
    if (t >= 0) return (e.preventDefault(), (cursor = t), press(t))
    if (e.key === 'z' || e.key === 'x')
      return (
        e.preventDefault(),
        (octave = Math.max(2, Math.min(5, octave + (e.key === 'x' ? 1 : -1)))),
        drawPiano(),
        hwText()
      )
    const d = { ArrowRight: 1, ArrowLeft: -1 }[e.key]
    if (d) return (e.preventDefault(), (cursor = (cursor + d + 13) % 13), drawPiano())
    if (e.key === ' ' || e.key === 'Enter') (e.preventDefault(), press(cursor))
  })
  el.addEventListener('keyup', (e) => {
    const t = TYPING.indexOf(e.key)
    if (t >= 0) release(t)
    if (e.key === ' ' || e.key === 'Enter') release(cursor)
  })
  el.addEventListener('focus', () => drawPiano())
  el.addEventListener('blur', () => (down.clear(), drawPiano()))
  drawPiano(true)
}

function hwText() {
  K.astext(
    't-hw',
    `decay   ${decay + 1} of 8\nbend    ${$('#pad-t').textContent}\nwave    ${wave}\noctave  ${octave}\nplayed  ${played.join(' ') || 'nothing yet'}\n\n(curl gets the settings; the sound needs a browser)`
  )
}

const mark = () => GLYPHS.mark.map((r) => [...r].map((c) => (c === '#' ? 1 : 0)))
let ed = null
function icon() {
  ed = cellgrid($('#cellgrid'), $('#previews'), {
    pixels: mark(),
    readout: $('#icon-t'),
    scope: $('#p-icon'),
    change: (cells) =>
      K.astext(
        't-icon',
        `${cells.map((r) => r.map((c) => (c ? '#' : '.')).join('')).join('\n')}\n\n${file(cells).replace(/></g, '>\n<')}`
      ),
  })
  document.addEventListener('click', (e) => {
    const b = e.target.closest('[data-icon]')
    if (!b) return
    const act = b.dataset.icon
    if (act === 'clear' && !K.arm(b)) return
    if (act === 'invert') ed.edit((cells) => cells.map((r) => r.map((c) => 1 - c)))
    if (act === 'clear') (ed.set([]), K.say(b, 0))
    if (act === 'base') ed.set(mark())
    if (act === 'svg') {
      const a = Object.assign(document.createElement('a'), {
        href: URL.createObjectURL(new Blob([ed.svg()], { type: 'image/svg+xml' })),
        download: 'icon.svg',
      })
      a.click()
      K.say(b, 1)
      setTimeout(() => K.say(b, 0), 1400)
    }
  })
  document.addEventListener(
    'focusout',
    (e) => e.target.matches?.('[data-icon=clear]') && K.disarm(e.target)
  )
}

export function init() {
  knob()
  padPart()
  drawScope(true)
  piano()
  icon()
  $('#wave').addEventListener(
    'change',
    (e) => ((wave = e.target.dataset.wave), drawScope(), hwText())
  )
  hwText()
}
export const state = () => ({
  decay,
  pad,
  wave,
  octave,
  played: played.slice(),
  cells: ed.cells.flat().filter(Boolean).length,
  at: ed.at,
})
export const relayout = () => ed.draw()
export function redraw() {
  for (const f of [knobF, padF, scopeF, pianoF]) f?.redraw()
  ed?.redraw()
}
