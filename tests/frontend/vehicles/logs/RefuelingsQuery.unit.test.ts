import { visit, type FieldNode } from 'graphql'
import { expect, it } from 'vitest'
import { RefuelingsDocument } from '../../../../src/frontend/gql/generated.ts'

/** The fields of the grid's rows, with the variables that switch them on (none: always asked for). */
function rowFields() {
  const fields = new Map<string, string[]>()
  visit(RefuelingsDocument, {
    Field(node: FieldNode, _key, _parent, _path, ancestors) {
      const parent = ancestors.at(-2) as FieldNode | undefined
      if (parent?.kind !== 'Field' || parent.name.value !== 'refuelings') return
      fields.set(node.name.value, (node.directives ?? []).map((d) => d.name.value))
    },
  })
  return fields
}

// The msw fakes answer with every field whatever @include says, so only the document itself can pin this: a real server leaves out an
// excluded field, and the price per unit would lose its currency (a dash) whenever the cost column is hidden.
it('asks for the currency whatever columns are shown: the price per unit needs it too', () => {
  const fields = rowFields()

  expect(fields.get('currency')).toEqual([])
  expect(fields.get('pricePerUnit')).toEqual(['include'])
})
