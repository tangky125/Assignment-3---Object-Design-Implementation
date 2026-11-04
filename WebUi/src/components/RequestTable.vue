<script setup lang="ts">
// A list of requests. Each row opens the screen given by `linkTo`.
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import type { QueueTag, RequestSummary } from '@/api/types'
import StatusBadge from './StatusBadge.vue'
import { shortWhen } from '@/utils/format'

const tagText: Record<QueueTag, string> = { New: 'New', Revision: 'Revision', Expired: 'Expired' }

defineProps<{
  rows: RequestSummary[]
  linkTo: (row: RequestSummary) => RouteLocationRaw
  actionLabel?: string
  timeHeader?: string
  emptyText: string
  caption: string
}>()
</script>

<template>
  <div class="table-wrap">
    <table class="stack-table">
      <caption class="visually-hidden">
        {{
          caption
        }}
      </caption>
      <thead>
        <tr>
          <th scope="col">Reference</th>
          <th scope="col">Customer</th>
          <th scope="col">Status</th>
          <th scope="col">Route</th>
          <th scope="col">Parcels</th>
          <th scope="col">{{ timeHeader ?? 'Logged / pickup' }}</th>
          <th scope="col">Note</th>
          <th scope="col"><span class="visually-hidden">Action</span></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.reference" data-testid="request-row">
          <td data-label="Reference">
            <strong>{{ row.reference }}</strong>
          </td>
          <td data-label="Customer">{{ row.customerName }}</td>
          <td data-label="Status"><StatusBadge :status="row.status" /></td>
          <td data-label="Route">{{ row.pickupSuburb }} &rarr; {{ row.destinationSuburb }}</td>
          <td data-label="Parcels">{{ row.parcelCount }}</td>
          <td :data-label="timeHeader ?? 'Logged / pickup'">
            {{ shortWhen(row.pickupWindow ? row.pickupWindow.start : row.loggedAt) }}
          </td>
          <td data-label="Note">
            <div class="note">
              <span
                v-if="row.tag"
                :class="['badge', `badge-tag-${row.tag.toLowerCase()}`]"
                data-testid="queue-tag"
                >{{ tagText[row.tag] }}</span
              >
              <span :class="row.tag === 'Revision' ? 'note-strong' : 'muted'">{{ row.note ?? '' }}</span>
            </div>
          </td>
          <td class="action-cell">
            <RouterLink
              class="btn btn-secondary btn-row"
              :to="linkTo(row)"
              :aria-label="`${actionLabel ?? 'Open'} ${row.reference}`"
            >
              {{ actionLabel ?? 'Open' }}
            </RouterLink>
          </td>
        </tr>
        <tr v-if="rows.length === 0">
          <td colspan="8" class="muted">{{ emptyText }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.note {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.35rem 0.5rem;
}

.note-strong {
  font-weight: 600;
}

.btn-row {
  min-height: 2.1rem;
  padding: 0.25rem 0.8rem;
  white-space: nowrap;
}
</style>
