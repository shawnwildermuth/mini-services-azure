<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, type Schemas } from '@/api/client'
import { problemMessage } from '@/api/errors'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import StatusPill from '@/components/StatusPill.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useToastStore } from '@/stores/toast'
import { formatDateTime, timeAgo } from '@/utils/format'

usePageTitle('Failed messages')

type Message = Schemas['DeadLetterDto']

const toast = useToastStore()

// The API shows at most this many messages per queue (oldest first).
const PEEK_LIMIT = 50
const messages = ref<Message[]>()
const unavailable = ref(false)
const error = ref('')

async function load() {
  error.value = ''
  unavailable.value = false
  try {
    const { data, response } = await api.GET('/api/admin/operations/dead-letters')
    if (data) messages.value = data
    else if (response.status === 503) unavailable.value = true
    else error.value = 'We could not load failed messages.'
  } catch {
    error.value = 'We could not reach the API.'
  }
}
onMounted(load)

const pending = ref<{ kind: 'replay' | 'discard'; message: Message }>()
const busy = ref(false)

async function confirm() {
  if (!pending.value) return
  const { kind, message } = pending.value
  busy.value = true

  const path = { params: { path: { queue: message.queue, sequenceNumber: message.sequenceNumber } } }
  const { response, error: problem } =
    kind === 'replay'
      ? await api.POST('/api/admin/operations/dead-letters/{queue}/{sequenceNumber}/replay', path)
      : await api.DELETE('/api/admin/operations/dead-letters/{queue}/{sequenceNumber}', path)
  busy.value = false
  pending.value = undefined

  if (response.status === 404) toast.info('That message is already gone.')
  else if (!response.ok) toast.error(problemMessage(problem, 'The action failed.'))
  else toast.success(kind === 'replay' ? 'Message sent back for processing.' : 'Message discarded.')
  await load()
}

const truncated = computed(() => (messages.value ?? []).filter((m) => m.queue === 'order-placed').length >= PEEK_LIMIT || (messages.value ?? []).filter((m) => m.queue === 'payment-captured').length >= PEEK_LIMIT)

const prettyBody = (body: string) => {
  try {
    return JSON.stringify(JSON.parse(body), null, 2)
  } catch {
    return body
  }
}

const explanation = computed(() =>
  pending.value?.kind === 'replay'
    ? 'The message goes back on its queue and is processed again. Billing and fulfillment are safe to repeat: an order is never charged or shipped twice.'
    : 'The message is deleted without being processed. The related order will stay stuck unless you cancel it.',
)
</script>

<template>
  <div class="space-y-4">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div>
        <h1 class="text-2xl font-bold">Failed messages</h1>
        <p class="text-sm opacity-70">Messages that exhausted their retries (or could never be processed) wait here for a decision.</p>
      </div>
      <button class="btn btn-sm" @click="load">Refresh</button>
    </div>

    <div v-if="unavailable" role="alert" class="alert alert-info alert-soft">Message queues are not connected in this environment.</div>
    <ErrorAlert v-else-if="error" :message="error" retry @retry="load" />
    <div v-else-if="!messages" class="skeleton h-40" />

    <div v-else-if="!messages.length" class="rounded-box bg-base-200 p-10 text-center">
      <p class="text-lg font-semibold">No failed messages 🎉</p>
      <p class="text-sm opacity-70">Every order is flowing through billing and fulfillment.</p>
    </div>

    <template v-else>
      <div v-if="truncated" role="status" class="alert alert-warning alert-soft mb-3">
        Showing the oldest {{ PEEK_LIMIT }} messages of at least one queue. Replay or discard some to see the rest.
      </div>

      <ul class="space-y-3">
        <li v-for="m in messages" :key="`${m.queue}-${m.sequenceNumber}`" class="card card-border bg-base-100">
          <div class="card-body gap-2 p-4">
            <div class="flex flex-wrap items-start justify-between gap-2">
              <div class="space-y-1">
                <p>
                  <StatusPill tone="error">{{ m.reason ?? 'Dead-lettered' }}</StatusPill>
                  <span class="badge badge-ghost ml-2">{{ m.queue }}</span>
                </p>
                <p v-if="m.description" class="text-sm">{{ m.description }}</p>
                <p class="text-xs opacity-70" :title="formatDateTime(m.enqueuedAt)">
                  {{ m.subject ?? 'unknown type' }} · {{ m.deliveryCount }} {{ m.deliveryCount === 1 ? 'delivery' : 'deliveries' }} · {{ timeAgo(m.enqueuedAt) }}
                </p>
                <RouterLink v-if="m.orderId" :to="`/orders/${m.orderId}`" class="link text-sm">View order</RouterLink>
              </div>
              <div class="flex gap-2">
                <button class="btn btn-sm btn-primary" @click="pending = { kind: 'replay', message: m }">Replay</button>
                <button class="btn btn-sm btn-ghost text-error" @click="pending = { kind: 'discard', message: m }">Discard</button>
              </div>
            </div>
            <details class="text-sm">
              <summary class="cursor-pointer opacity-70">Message body</summary>
              <pre class="mt-2 overflow-x-auto rounded-box bg-base-200 p-3 text-xs">{{ prettyBody(m.body) }}</pre>
            </details>
          </div>
        </li>
      </ul>
    </template>

    <ConfirmDialog
      :open="!!pending"
      :title="pending?.kind === 'replay' ? 'Replay this message?' : 'Discard this message?'"
      :confirm-label="pending?.kind === 'replay' ? 'Replay' : 'Discard'"
      :tone="pending?.kind === 'replay' ? 'btn-primary' : 'btn-error'"
      :busy="busy"
      @confirm="confirm"
      @cancel="pending = undefined"
    >
      {{ explanation }}
    </ConfirmDialog>
  </div>
</template>