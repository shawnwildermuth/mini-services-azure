import { toValue, watchEffect, type MaybeRefOrGetter } from 'vue'

const SITE = 'Parts Store Admin'

export function usePageTitle(title: MaybeRefOrGetter<string | undefined>) {
  watchEffect(() => {
    const value = toValue(title)
    document.title = value ? `${value} · ${SITE}` : SITE
  })
}
