# substrate

One pixel, three faces, two layers, and the controls, charts and parts built
on them. Plain CSS and ES modules, no build step; every path inside is
relative, so the folder works wherever it is served.

## Mount

```html
<link rel="stylesheet" href="substrate/css/index.css" />
<article class="substrate">...</article>
<script type="module">
  import { mountCells, FACES } from './substrate/js/index.js'
  const article = document.querySelector('article')
  const cells = mountCells(article, {
    faces: FACES,
    // what is drawn as cells: here the titles and the inline charts
    scope: () => [...article.querySelectorAll('h1, [data-chart]')],
  })
  cells.layout()
</script>
```

`mountCells` returns `{ layout(), draw(), pixel }`: call `layout()` after the
scope or the content changes, `draw()` after a change of ink only (a theme);
`pixel` is the pixel in device px, settable. Without JS, with reduced motion,
or when a face fails to load, the text is the page.

A kit part is imported from `js/kit/<part>.js` as needed; the controls, charts
and parts are styled inside `.doc`, but some of their class names are
global (`.ctl`, `.btn`, `.cmd`, `.lnk`, `.fig`, `.codeblock`, `[hidden]`): a
page's own markup should not reuse them.

## Tokens

Set on `:root` (defaults in `css/substrate.css`):

| token | what |
| --- | --- |
| `--ink`, `--ground` | the two inks, light; dark under `prefers-color-scheme` or `[data-theme=dark]` |
| `--px` | the pixel in CSS px; set by the cells layer to whole device px, 1 without JS |
| `--u` | one pixel as a length, `calc(var(--px) * 1px)`: every length is `N * --u` |
| `--dim`, `--off`, `--half`, `--code-ground`, `--well`, `--d25`, `--d50` | derived from the two inks |

`<html data-theme="light|dark">` overrides the system's scheme.

## Files

| file | what |
| --- | --- |
| `README.md` | this file |
| `css/brackets.css` | every control: words in brackets, their states as ink on rows they already have |
| `css/charts.css` | figures on the lattice: charts and diagrams, their readouts |
| `css/index.css` | the one stylesheet to link: imports the four below in order |
| `css/parts.css` | the parts: text forms, rows, drawers, the command list; and the controls' base |
| `css/substrate.css` | the pixel (--px, --u), the two inks, the three faces, the code ground, the cells layer |
| `js/cells.js` | the cells layer: mountCells(root, opts) draws text in scope as pixels over the DOM text |
| `js/index.js` | the one module to import: mountCells and FACES |
| `js/kit/cellgrid.js` | an 11 by 11 pixel editor |
| `js/kit/controls.js` | the controls' shared moves: labels that crossfade, notices, confirm in place |
| `js/kit/diagram.js` | any Mermaid diagram or chart drawn on the lattice (laid out by Pele) |
| `js/kit/dither.js` | fades as ordered dither, a Bayer level a frame |
| `js/kit/hw.js` | hardware on the lattice: knob, pad, scope, keys |
| `js/kit/icons.js` | 11 by 11 glyphs |
| `js/kit/lattice.js` | a figure on the lattice: a canvas of tones in whole device px, words as text |
| `js/kit/machines.js` | the controls' rules as pure state |
| `js/kit/morph.js` | changes told in pixels: unfold, glide |
| `js/kit/plot.js` | charts at full size, from the same data as their inline form |
| `js/kit/steps.js` | the steps of a change, as numbers (pure) |
| `js/kit/touch.js` | a readout the pointer drives, on every kind of pointer |
| `js/kit/toys.js` | the ladybug and the slime dish, drawn from state the host supplies |
| `js/pele/LICENSE` | Pele's licence (MIT) |
| `js/scatter.js` | the cellular automaton the cells scatter and settle by (pure) |
| `js/shared/charts.js` | chart scales, paints and inline syntax (pure) |
| `js/shared/faces/index.js` | the three faces by family name |
| `js/shared/faces/mono.js` | Handjet Cells Mono, as rows |
| `js/shared/faces/prose.js` | Handjet Cells, as rows |
| `js/shared/faces/title.js` | Pixelta Cells, as rows |
| `js/shared/lattice.js` | the lattice's numbers: tones, levels, widths, lines (pure) |
| `js/shared/qr.js` | QR codes as a matrix and as text (pure) |
| `js/shared/slime.js` | the slime mould (pure, seeded) |
| `manifest.json` | every file with its size and sha256, the source commit, the rules |
| `fonts/` (10 files) | the three faces as woff2, woff and ttf, with the licences that ship with them |
| `js/pele/esm/` (45 files) | Pele, the Mermaid layout engine, as published: the modules the kit reaches |

Pixelta Cells is drawn from Pixelta, under a commercial licence that does not
ship here; Handjet Cells and Handjet Cells Mono are under the OFL
(`fonts/LICENSE-Handjet.txt`).

## Not included

- `js/kit/files.js`: imports its demo data by a bare name an import map resolves
- `js/kit/media.js`: imports its demo data by a bare name an import map resolves
- `js/kit/over.js`: imports its demo data by a bare name an import map resolves
- `js/kit/rows.js`: imports its demo data by a bare name an import map resolves
- `js/kit/shell.js`: imports its demo data by a bare name an import map resolves
