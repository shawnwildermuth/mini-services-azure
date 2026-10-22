// Placeholder artwork until products have real images.
const icons: Record<string, string> = {
  cpus: '🧠',
  motherboards: '🧩',
  memory: '💾',
  storage: '🗄️',
  gpus: '🎮',
  'power-supplies': '🔌',
  cases: '🖥️',
  cooling: '❄️',
}

export const categoryIcon = (slug: string) => icons[slug] ?? '📦'
