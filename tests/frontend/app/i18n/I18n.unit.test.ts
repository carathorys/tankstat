import { expect, it } from 'vitest'
import en from '../../../../src/frontend/i18n/locales/en.json'
import hu from '../../../../src/frontend/i18n/locales/hu.json'
import { LANGUAGES } from '../../../../src/frontend/i18n/index.ts'

type Tree = { [key: string]: string | Tree }

/** "a.b_one" and "a.b_other" are plural variants of one message; languages may have different plural categories. */
const flatten = (tree: Tree, prefix = ''): Record<string, string> =>
  Object.entries(tree).reduce<Record<string, string>>((acc, [key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key
    return typeof value === 'string' ? { ...acc, [path]: value } : { ...acc, ...flatten(value, path) }
  }, {})

const baseKey = (key: string) => key.replace(/_(zero|one|two|few|many|other)$/, '')
const placeholders = (text: string) => [...text.matchAll(/{{\s*(\w+)\s*}}/g)].map((m) => m[1]).sort()

const catalogs: Record<string, Record<string, string>> = { en: flatten(en as Tree), hu: flatten(hu as Tree) }

it('every language in the switcher has a catalog', () => {
  expect(LANGUAGES.map((l) => l.code).sort()).toEqual(Object.keys(catalogs).sort())
})

for (const code of Object.keys(catalogs).filter((c) => c !== 'en')) {
  it(`${code} has exactly the messages English has`, () => {
    const english = new Set(Object.keys(catalogs.en).map(baseKey))
    const translated = new Set(Object.keys(catalogs[code]).map(baseKey))

    expect([...english].filter((k) => !translated.has(k))).toEqual([]) // untranslated
    expect([...translated].filter((k) => !english.has(k))).toEqual([]) // stale or misspelled
  })

  it(`${code} keeps the same {{placeholders}} as English`, () => {
    for (const [key, text] of Object.entries(catalogs[code])) {
      const english = catalogs.en[key] ?? catalogs.en[`${baseKey(key)}_other`]
      expect(placeholders(text), key).toEqual(placeholders(english))
    }
  })

  it(`${code} has no empty messages`, () => {
    for (const [key, text] of Object.entries(catalogs[code])) expect(text.trim(), key).not.toBe('')
  })
}
