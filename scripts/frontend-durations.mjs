// Turns a Vitest JSON report (`--reporter=json --outputFile=...`) into tests/frontend/durations.json: how long each test file took, in ms,
// by its path from the repository root. vitest.config.ts balances the CI shards by it. Usage: node scripts/frontend-durations.mjs <report>
import { readFileSync, writeFileSync } from 'node:fs'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const report = JSON.parse(readFileSync(process.argv[2], 'utf8'))
const durations = Object.fromEntries(
  report.testResults
    .map((file) => [relative(root, file.name).replaceAll('\\', '/'), Math.round(file.endTime - file.startTime)])
    .sort(([a], [b]) => (a < b ? -1 : 1)),
)
writeFileSync(resolve(root, 'tests/frontend/durations.json'), `${JSON.stringify(durations, null, 2)}\n`)
console.log(`${Object.keys(durations).length} test files measured`)
