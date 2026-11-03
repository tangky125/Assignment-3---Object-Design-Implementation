<script setup lang="ts">
// A person's name and Australian phone number.
import type { ContactInput } from '@/api/types'
import FormField from './FormField.vue'

defineProps<{
  idPrefix: string
  nameLabel: string
  phoneLabel: string
  nameHint?: string
  errors: Partial<Record<'name' | 'phone', string>>
}>()
const contact = defineModel<ContactInput>({ required: true })
</script>

<template>
  <div class="grid-2">
    <FormField
      :id="`${idPrefix}-name`"
      :label="nameLabel"
      :hint="nameHint ?? '2 to 60 characters'"
      :error="errors.name"
      required
    >
      <template #default="f">
        <input
          :id="f.id"
          v-model="contact.name"
          type="text"
          autocomplete="name"
          :aria-describedby="f.describedBy"
          :aria-invalid="f.invalid"
        />
      </template>
    </FormField>
    <FormField
      :id="`${idPrefix}-phone`"
      :label="phoneLabel"
      hint="Australian number, e.g. 0412 345 678"
      :error="errors.phone"
      required
    >
      <template #default="f">
        <input
          :id="f.id"
          v-model="contact.phone"
          type="tel"
          autocomplete="tel"
          :aria-describedby="f.describedBy"
          :aria-invalid="f.invalid"
        />
      </template>
    </FormField>
  </div>
</template>
