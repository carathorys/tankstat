import { expect, it } from 'vitest'
import { problemsByKey } from '../../../src/frontend/admin/settingsText.ts'

it("puts each problem at the setting it starts with, the longest key first, and keeps the rest", () => {
  const { byKey, rest } = problemsByKey(
    [
      'Auth:Oidc:Authority is required for Oidc mode.',
      "Auth:Mode 'X' is not supported.",
      'Smtp:Port must be a whole number.',
      'Auth:Oidc:ClientIdFoo is no setting of this group.',
      'The settings file cannot be read.',
    ],
    ['Auth:Mode', 'Auth:Oidc:Authority', 'Auth:Oidc:ClientId'],
  )

  expect([...byKey]).toEqual([
    ['Auth:Oidc:Authority', 'Auth:Oidc:Authority is required for Oidc mode.'],
    ['Auth:Mode', "Auth:Mode 'X' is not supported."],
  ])
  // Another group's setting, a longer name that merely starts like a key, and what names no setting stay together.
  expect(rest).toEqual(['Smtp:Port must be a whole number.', 'Auth:Oidc:ClientIdFoo is no setting of this group.', 'The settings file cannot be read.'])
})
