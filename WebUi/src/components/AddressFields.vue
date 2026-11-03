<script setup lang="ts">
// Street, suburb and postcode for one address, with each field's error shown under it.
import type { AddressInput } from '@/api/types'
import FormField from './FormField.vue'

defineProps<{
  idPrefix: string
  legend: string
  errors: Partial<Record<'street' | 'suburb' | 'postcode', string>>
}>()
const address = defineModel<AddressInput>({ required: true })
</script>

<template>
  <fieldset>
    <legend class="visually-hidden">{{ legend }}</legend>
    <FormField
      :id="`${idPrefix}-street`"
      label="Street address"
      hint="Number and street, e.g. 10 Swanston St"
      :error="errors.street"
      required
    >
      <template #default="f">
        <input
          :id="f.id"
          v-model="address.street"
          type="text"
          autocomplete="street-address"
          :aria-describedby="f.describedBy"
          :aria-invalid="f.invalid"
        />
      </template>
    </FormField>
    <div class="grid-2">
      <FormField
        :id="`${idPrefix}-suburb`"
        label="Suburb"
        hint="e.g. Melbourne"
        :error="errors.suburb"
        required
      >
        <template #default="f">
          <input
            :id="f.id"
            v-model="address.suburb"
            type="text"
            autocomplete="address-level2"
            :aria-describedby="f.describedBy"
            :aria-invalid="f.invalid"
          />
        </template>
      </FormField>
      <FormField
        :id="`${idPrefix}-postcode`"
        label="Postcode"
        hint="4 digits, e.g. 3000"
        :error="errors.postcode"
        required
      >
        <template #default="f">
          <input
            :id="f.id"
            v-model="address.postcode"
            type="text"
            inputmode="numeric"
            maxlength="4"
            autocomplete="postal-code"
            :aria-describedby="f.describedBy"
            :aria-invalid="f.invalid"
          />
        </template>
      </FormField>
    </div>
  </fieldset>
</template>
