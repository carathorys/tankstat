/** An error answered by the REST endpoints (pictures): the same stable key and arguments as GraphQL errors. */
export class ApiError extends Error {
  readonly key: string | undefined
  readonly args: Record<string, unknown>
  readonly status: number

  constructor(key: string | undefined, args: Record<string, unknown>, message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.key = key
    this.args = args
    this.status = status
  }
}
