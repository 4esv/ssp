const said = (th) => {
  const c = th.cloneNode(true)
  for (const x of c.querySelectorAll('.v')) x.remove()
  return c.textContent.trim()
}

export function records(t) {
  const heads = [...t.querySelectorAll('thead th')].map(said)
  for (const tr of t.querySelectorAll('tbody tr'))
    [...tr.cells].forEach((c, i) => heads[i] && (c.dataset.h = heads[i]))
  const page = t.closest('main') || t.parentElement
  const room = () => {
    const s = getComputedStyle(page)
    return page.clientWidth - parseFloat(s.paddingLeft) - parseFloat(s.paddingRight)
  }
  let was = -1
  const tell = () => {
    const w = room()
    if (w === was) return
    was = w
    const css = t.style.cssText
    t.classList.remove('rec')
    t.classList.add('whole')
    t.style.cssText += `;max-width:none;width:${w}px`
    const over = t.scrollWidth > w + 0.5
    t.style.cssText = css
    t.classList.remove('whole')
    t.classList.toggle('rec', over)
  }
  tell()
  new ResizeObserver(tell).observe(page)
}
