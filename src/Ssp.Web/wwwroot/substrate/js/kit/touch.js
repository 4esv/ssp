const held = new Set()
let watching = false
const outside = (e) => {
  for (const p of held) if (!p.host.contains(e.target)) (held.delete(p), p.choose(p.none))
}

export function pick(host, hit, choose, none) {
  const part = { host, choose, none }
  const finger = (e) => e.pointerType !== 'mouse'
  host.addEventListener('pointerdown', (e) => {
    if (!finger(e)) return
    held.add(part)
    if (!watching) ((watching = true), document.addEventListener('pointerdown', outside, true))
    choose(hit(e))
  })
  host.addEventListener('pointermove', (e) => choose(hit(e)))
  host.addEventListener(
    'pointerleave',
    (e) => !finger(e) && document.activeElement !== host && choose(none)
  )
}

export function press(el, ms, done) {
  let timer = 0
  let id = null
  let fired = false
  const stop = (e) => e.pointerId === id && (clearTimeout(timer), (id = null))
  el.addEventListener('pointerdown', (e) => {
    if (!e.isPrimary || e.button !== 0) return
    clearTimeout(timer)
    fired = false
    id = e.pointerId
    timer = setTimeout(() => ((id = null), (fired = true), done()), ms)
  })
  for (const type of ['pointerup', 'pointercancel', 'pointerleave']) el.addEventListener(type, stop)
  el.addEventListener('contextmenu', (e) => id !== null && e.preventDefault())
  el.addEventListener(
    'click',
    (e) => fired && ((fired = false), e.preventDefault(), e.stopImmediatePropagation()),
    true
  )
}
