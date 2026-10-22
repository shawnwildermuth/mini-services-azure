const FALLBACK = 'Something went wrong. Please try again.'

/**
 * Turns an API error body into one readable message. Handles ASP.NET validation problems
 * (`errors`: field -> messages, which is also how Identity reports weak passwords and duplicate
 * emails) and plain problem details (`detail` / `title`).
 */
export function problemMessage(error: unknown, fallback = FALLBACK): string {
  if (!error || typeof error !== 'object') return fallback
  const problem = error as { errors?: Record<string, unknown>; detail?: unknown; title?: unknown }

  if (problem.errors && typeof problem.errors === 'object') {
    const messages = Object.values(problem.errors)
      .flatMap((value) => (Array.isArray(value) ? value : [value]))
      .filter((value): value is string => typeof value === 'string' && value.length > 0)
    if (messages.length) return [...new Set(messages)].join(' ')
  }

  if (typeof problem.detail === 'string' && problem.detail) return problem.detail
  if (typeof problem.title === 'string' && problem.title) return problem.title
  return fallback
}

export type Result = { ok: true } | { ok: false; message: string }

export const ok = (): Result => ({ ok: true })
export const fail = (message: string): Result => ({ ok: false, message })
