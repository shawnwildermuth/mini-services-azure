import { ref } from 'vue'
import { defineStore } from 'pinia'
import { api, type Schemas } from '@/api/client'
import { fail, ok, problemMessage, type Result } from '@/api/errors'

type Address = Schemas['AddressDto']
type AddressRequest = Schemas['AddressRequest']

export const useAddressStore = defineStore('addresses', () => {
  const addresses = ref<Address[]>([])
  const loaded = ref(false)

  async function load() {
    const { data } = await api.GET('/api/addresses')
    if (data) {
      addresses.value = data
      loaded.value = true
    }
  }

  async function create(request: AddressRequest): Promise<Result> {
    const { data, error } = await api.POST('/api/addresses', { body: request })
    if (!data) return fail(problemMessage(error, 'We could not save that address.'))
    await load() // default flags on other addresses may have changed
    return ok()
  }

  async function update(id: number, request: AddressRequest): Promise<Result> {
    const { data, error } = await api.PUT('/api/addresses/{id}', { params: { path: { id } }, body: request })
    if (!data) return fail(problemMessage(error, 'We could not save that address.'))
    await load()
    return ok()
  }

  async function remove(id: number): Promise<Result> {
    const { response } = await api.DELETE('/api/addresses/{id}', { params: { path: { id } } })
    if (!response.ok) return fail('We could not delete that address.')
    await load()
    return ok()
  }

  function reset() {
    addresses.value = []
    loaded.value = false
  }

  return { addresses, loaded, load, create, update, remove, reset }
})
