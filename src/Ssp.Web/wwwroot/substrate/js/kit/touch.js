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
