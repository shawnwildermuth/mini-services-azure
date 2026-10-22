import { clearDeadLetters } from './support/cleanup'

export default async function globalTeardown() {
  await clearDeadLetters()
}
