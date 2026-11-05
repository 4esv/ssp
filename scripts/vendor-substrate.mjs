#!/usr/bin/env node
// Vendor the aesv.io V3 substrate (#416): run the source repo's own export
// (scripts/substrate/export.mjs in 4esv/aesv.io), take the files the app
// uses, byte for byte, and write a manifest of them with the export's hashes.
//
//   node scripts/vendor-substrate.mjs [aesv.io checkout]
//
// The checkout needs `npm ci` (the export parses JS with acorn). Taken: the
// pixel, the two inks and the faces (css/substrate.css), and the Handjet
// faces with their licence. Left out: the JS kits and the other sheets (the
// app loads no cells layer, and their controls are scoped to the site's own
// markup), and the Pixelta Cells files (their licence for a public repository
// is not decided; app.css sets titles in Handjet Cells, so no page asks for them).
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const OUT = fileURLToPath(new URL('../src/Ssp.Web/wwwroot/substrate/', import.meta.url))
const TAKE = [
  'css/substrate.css',
  'fonts/LICENSE-Handjet.txt',
  ...['handjet-cells', 'handjet-cells-mono'].flatMap((f) => ['woff2', 'woff', 'ttf'].map((x) => `fonts/${f}.${x}`)),
]

const repo = path.resolve(process.argv[2] ?? path.join(os.homedir(), 'Code', 'projects', 'aesv.io'))
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'substrate-'))
try {
  execFileSync('node', ['scripts/substrate/export.mjs', tmp], { cwd: repo, stdio: ['ignore', 'ignore', 'inherit'] })
  const exported = JSON.parse(fs.readFileSync(path.join(tmp, 'manifest.json'), 'utf8'))
  if (exported.dirty) throw new Error(`${repo} has uncommitted substrate changes; export from a clean commit`)
  const byPath = new Map(exported.files.map((f) => [f.path, f]))
  fs.rmSync(OUT, { recursive: true, force: true })
  let bytes = 0
  for (const p of TAKE) {
    if (!byPath.has(p)) throw new Error(`the export has no ${p}`)
    fs.mkdirSync(path.join(OUT, path.dirname(p)), { recursive: true })
    fs.copyFileSync(path.join(tmp, p), path.join(OUT, p))
    bytes += byPath.get(p).bytes
  }
  const manifest = {
    source: 'https://github.com/4esv/aesv.io',
    commit: exported.commit,
    date: exported.date,
    files: TAKE.sort().map((p) => byPath.get(p)),
  }
  fs.writeFileSync(path.join(OUT, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n')
  console.log(`${TAKE.length} files, ${bytes} bytes from ${exported.commit.slice(0, 7)}`)
} finally {
  fs.rmSync(tmp, { recursive: true, force: true })
}
