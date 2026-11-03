<script setup lang="ts">
// Business area 1: log a delivery request (A1 Task 1). Customers log their own; office staff log
// for phone customers (variant 1a). Nothing is saved until "Submit request", so abandoning the
// request part-way leaves no partial record in the office queue (variant 6a).
import { computed, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { requestApi } from '@/api/dms'
import {
  Handling,
  type AddressInput,
  type AddressPair,
  type ContactInput,
  type CustomerSummary,
  type SizeBand,
} from '@/api/types'
import { hasRole, session } from '@/session/session'
import { flash } from '@/session/flash'
import * as rules from '@/validation/rules'
import { handlingText, localInputToServer, nextHour, toLocalInput, when } from '@/utils/format'
import { errorText } from '@/utils/useAsync'
import AddressFields from '@/components/AddressFields.vue'
import ConfirmBox from '@/components/ConfirmBox.vue'
import ContactFields from '@/components/ContactFields.vue'
import CustomerSearch from '@/components/CustomerSearch.vue'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import StepList from '@/components/StepList.vue'

interface ParcelForm {
  key: number
  size: SizeBand | ''
  fragile: boolean
  signature: boolean
  upright: boolean
  description: string
}

type AddressErrors = Partial<Record<'street' | 'suburb' | 'postcode', string>>
type ContactErrors = Partial<Record<'name' | 'phone', string>>

const MaxParcels = 10
const MaxDaysAhead = 14
const steps = ['Customer', 'Pickup', 'Destination', 'Parcels', 'Notes', 'Review']
const sizes: { value: SizeBand; label: string; hint: string }[] = [
  { value: 'Small', label: 'Small', hint: 'Satchel, up to 5 kg' },
  { value: 'Medium', label: 'Medium', hint: 'Box, up to 15 kg' },
  { value: 'Large', label: 'Large', hint: 'Large box, up to 30 kg' },
]

const router = useRouter()
const isCustomer = hasRole('Customer')

const step = ref(0)
const editingFromReview = ref(false)
const busy = ref(false)
const problem = ref<string | null>(null)
const notice = ref<string | null>(null)
const confirmingDiscard = ref(false)
const submittedReference = ref<string | null>(null)
const submittedMessage = ref('')

const customer = ref<CustomerSummary | null>(null)
const recentPairs = ref<AddressPair[]>([])
const chosenPair = ref<number | null>(null)

const pickup = reactive<AddressInput>({ street: '', suburb: '', postcode: '' })
const pickupContact = reactive<ContactInput>({ name: '', phone: '' })
const readyTime = ref(toLocalInput(nextHour()))
const destination = reactive<AddressInput>({ street: '', suburb: '', postcode: '' })
const recipient = reactive<ContactInput>({ name: '', phone: '' })
let parcelKey = 1
const parcels = ref<ParcelForm[]>([newParcel()])
const notes = ref('')

const pickupErrors = ref<AddressErrors>({})
const pickupContactErrors = ref<ContactErrors>({})
const readyError = ref<string | null>(null)
const destinationErrors = ref<AddressErrors>({})
const recipientErrors = ref<ContactErrors>({})
const parcelErrors = ref<Record<number, { size?: string; description?: string }>>({})
const notesError = ref<string | null>(null)

const nowInput = computed(() => toLocalInput(new Date()))
const nextLabel = computed(() => (editingFromReview.value ? 'Save and return to review' : 'Continue'))

function newParcel(): ParcelForm {
  return { key: parcelKey++, size: '', fragile: false, signature: false, upright: false, description: '' }
}

function flagsOf(p: ParcelForm): number {
  return (
    (p.fragile ? Handling.Fragile : 0) |
    (p.signature ? Handling.SignatureRequired : 0) |
    (p.upright ? Handling.KeepUpright : 0)
  )
}

function go(to: number): void {
  problem.value = null
  notice.value = null
  step.value = to
}

function next(): void {
  go(editingFromReview.value ? 5 : step.value + 1)
  if (step.value === 5) editingFromReview.value = false
}

function editFromReview(to: number): void {
  editingFromReview.value = true
  go(to)
}

function hasErrors(...groups: object[]): boolean {
  const count = groups.reduce<number>((n, g) => n + Object.keys(g).length, 0)
  if (count > 0) {
    problem.value = `Please correct the ${count === 1 ? 'field' : `${count} fields`} marked in red below.`
  }
  return count > 0
}

onMounted(async () => {
  if (isCustomer && session.user?.customerId) {
    try {
      await chooseCustomer(await requestApi.getCustomer(session.user.customerId))
    } catch (e) {
      problem.value = errorText(e)
    }
  }
})

async function chooseCustomer(c: CustomerSummary): Promise<void> {
  customer.value = c
  pickupContact.name = c.name
  pickupContact.phone = c.phone
  recentPairs.value = await requestApi.recentAddresses(c.customerId)
  if (!isCustomer) go(1)
}

function useRecentPair(): void {
  if (chosenPair.value === null) {
    problem.value = 'Choose one of the recent addresses first, or type new ones below.'
    return
  }
  const pair = recentPairs.value[chosenPair.value]
  Object.assign(pickup, pair.pickup)
  Object.assign(destination, pair.destination)
  Object.assign(recipient, pair.recipient)
  notice.value = 'Recent pickup and destination filled in. Check them, then continue.'
}

async function savePickup(): Promise<void> {
  problem.value = null
  pickupErrors.value = rules.check({
    street: [pickup.street, rules.street],
    suburb: [pickup.suburb, rules.suburb],
    postcode: [pickup.postcode, rules.postcode],
  })
  pickupContactErrors.value = rules.check({
    name: [pickupContact.name, rules.name],
    phone: [pickupContact.phone, rules.phone],
  })
  readyError.value = rules.dateTime((d) => {
    if (d < new Date()) return `The ready time must be in the future (it is now ${when(new Date())}).`
    return d.getTime() > Date.now() + MaxDaysAhead * 86_400_000
      ? `Requests can be logged up to ${MaxDaysAhead} days ahead.`
      : null
  })(readyTime.value)
  if (hasErrors(pickupErrors.value, pickupContactErrors.value, readyError.value ? { ready: 1 } : {})) return

  busy.value = true
  try {
    const result = await requestApi.checkPickup({ ...pickup })
    if (!result.succeeded) {
      pickupErrors.value = { postcode: result.message }
      problem.value = 'The pickup address cannot be used. See the message under Postcode.'
      return
    }
    pickupContact.phone = rules.normalisePhone(pickupContact.phone) ?? pickupContact.phone
    next()
  } catch (e) {
    problem.value = errorText(e)
  } finally {
    busy.value = false
  }
}

function saveDestination(): void {
  problem.value = null
  destinationErrors.value = rules.check({
    street: [destination.street, rules.street],
    suburb: [destination.suburb, rules.suburb],
    postcode: [destination.postcode, rules.postcode],
  })
  recipientErrors.value = rules.check({
    name: [recipient.name, rules.name],
    phone: [recipient.phone, rules.phone],
  })
  if (hasErrors(destinationErrors.value, recipientErrors.value)) return
  recipient.phone = rules.normalisePhone(recipient.phone) ?? recipient.phone
  next()
}

function addParcel(): void {
  if (parcels.value.length >= MaxParcels) {
    problem.value = `A request can have at most ${MaxParcels} parcels.`
    return
  }
  parcels.value.push(newParcel())
  notice.value = `Parcel ${parcels.value.length} added. Choose its size below.`
}

function removeParcel(index: number): void {
  if (parcels.value.length === 1) {
    problem.value = 'A request needs at least one parcel. Add the correct parcel first, then remove this one.'
    return
  }
  parcels.value.splice(index, 1)
  problem.value = null
  notice.value = `Parcel ${index + 1} removed. The remaining parcels have been renumbered.`
}

function saveParcels(): void {
  problem.value = null
  const errors: Record<number, { size?: string; description?: string }> = {}
  for (const p of parcels.value) {
    const e: { size?: string; description?: string } = {}
    if (p.size === '') e.size = 'Choose Small, Medium or Large.'
    const d = rules.optionalText(40)(p.description)
    if (d) e.description = d
    if (Object.keys(e).length > 0) errors[p.key] = e
  }
  parcelErrors.value = errors
  if (hasErrors(...Object.values(errors))) return
  next()
}

function saveNotes(): void {
  notesError.value = rules.optionalText(200)(notes.value)
  if (notesError.value) {
    problem.value = 'Please shorten the notes.'
    return
  }
  next()
}

async function submit(): Promise<void> {
  if (!customer.value) return
  busy.value = true
  problem.value = null
  try {
    const result = await requestApi.submit({
      customerId: customer.value.customerId,
      pickup: { ...pickup },
      pickupContact: { ...pickupContact },
      readyTime: localInputToServer(readyTime.value),
      destination: { ...destination },
      recipient: { ...recipient },
      parcels: parcels.value.map((p) => ({
        size: p.size as SizeBand,
        flags: flagsOf(p),
        description: p.description.trim() === '' ? null : p.description.trim(),
      })),
      notes: notes.value.trim() === '' ? null : notes.value.trim(),
    })
    if (result.succeeded && result.value) {
      submittedReference.value = result.value
      submittedMessage.value = result.message
    } else {
      problem.value = result.message
    }
  } catch (e) {
    problem.value = errorText(e)
  } finally {
    busy.value = false
  }
}

async function discard(): Promise<void> {
  flash('notice', 'Request discarded. Nothing was saved, and nothing was sent to the office queue.')
  await router.push('/dashboard')
}
</script>

<template>
  <!-- Successful completion -->
  <section v-if="submittedReference" class="card done" aria-live="polite">
    <MessageBanner kind="success" :text="submittedMessage" />
    <h1>Request submitted</h1>
    <dl class="facts">
      <dt>Reference</dt>
      <dd>
        <strong class="reference" data-testid="reference">{{ submittedReference }}</strong>
      </dd>
      <dt>Status</dt>
      <dd>Requested</dd>
      <dt>Customer</dt>
      <dd>{{ customer?.name }}</dd>
    </dl>
    <h2>What happens next</h2>
    <p>
      Office staff will prepare a quote with a price and pickup and delivery windows. The customer is told by
      their preferred contact method. Give the reference to the recipient so they can track the delivery.
    </p>
    <div class="row">
      <RouterLink :to="`/requests/${submittedReference}`" class="btn">View this request</RouterLink>
      <RouterLink to="/dashboard" class="btn btn-secondary">Main menu</RouterLink>
    </div>
  </section>

  <template v-else>
    <PageHeader
      title="Log a delivery request"
      :intro="
        isCustomer
          ? 'Tell us what to collect, where it is going, and what the parcels are.'
          : 'Logging a request for a customer who phoned in.'
      "
    />
    <StepList :steps="steps" :current="step" />
    <MessageBanner v-if="problem" kind="error" :text="problem" />
    <MessageBanner v-if="notice" kind="notice" :text="notice" dismissible @dismiss="notice = null" />

    <!-- Step 1: customer -->
    <section v-if="step === 0" class="card" aria-labelledby="s-customer">
      <h2 id="s-customer">Step 1 of 6: Customer</h2>
      <template v-if="isCustomer">
        <p v-if="customer">
          Logging a request for <strong>{{ customer.name }}</strong> ({{ customer.phone }}).
        </p>
        <button type="button" class="btn" :disabled="!customer" @click="go(1)">Continue</button>
      </template>
      <CustomerSearch v-else allow-register @chosen="chooseCustomer" />
    </section>

    <!-- Step 2: pickup -->
    <form v-if="step === 1" class="card" novalidate aria-labelledby="s-pickup" @submit.prevent="savePickup">
      <h2 id="s-pickup">Step 2 of 6: Pickup</h2>
      <p class="muted">
        For {{ customer?.name }}. Where should we collect the parcels? Pickups must be in our service area.
      </p>

      <fieldset v-if="recentPairs.length > 0 && !editingFromReview" class="recent">
        <legend>Reuse a recent pickup and destination</legend>
        <label v-for="(pair, i) in recentPairs" :key="i" class="choice">
          <input v-model="chosenPair" type="radio" name="recent" :value="i" />
          {{ pair.pickup.street }}, {{ pair.pickup.suburb }} &rarr; {{ pair.destination.street }},
          {{ pair.destination.suburb }} (for {{ pair.recipient.name }})
        </label>
        <button type="button" class="btn btn-secondary" @click="useRecentPair">Use these addresses</button>
      </fieldset>

      <AddressFields v-model="pickup" id-prefix="pickup" legend="Pickup address" :errors="pickupErrors" />
      <ContactFields
        v-model="pickupContact"
        id-prefix="pickup-contact"
        name-label="Pickup contact name"
        name-hint="Who hands over the parcels"
        phone-label="Pickup contact phone"
        :errors="pickupContactErrors"
      />
      <FormField
        id="ready"
        label="Parcels ready from"
        hint="Date and time, up to 14 days ahead"
        :error="readyError"
        required
      >
        <template #default="f">
          <input
            :id="f.id"
            v-model="readyTime"
            type="datetime-local"
            :min="nowInput"
            :aria-describedby="f.describedBy"
            :aria-invalid="f.invalid"
          />
        </template>
      </FormField>
      <div class="actions">
        <button type="submit" class="btn" :disabled="busy">{{ nextLabel }}</button>
        <button
          v-if="!editingFromReview && !isCustomer"
          type="button"
          class="btn btn-secondary"
          @click="go(0)"
        >
          Back
        </button>
        <button type="button" class="btn-link" @click="confirmingDiscard = true">
          Cancel and discard this request
        </button>
      </div>
    </form>

    <!-- Step 3: destination -->
    <form
      v-if="step === 2"
      class="card"
      novalidate
      aria-labelledby="s-dest"
      @submit.prevent="saveDestination"
    >
      <h2 id="s-dest">Step 3 of 6: Destination and recipient</h2>
      <p class="muted">
        Where are the parcels going, and who will receive them? A destination outside our service area can
        still be quoted, but an owner must approve the price.
      </p>
      <AddressFields
        v-model="destination"
        id-prefix="dest"
        legend="Destination address"
        :errors="destinationErrors"
      />
      <ContactFields
        v-model="recipient"
        id-prefix="recipient"
        name-label="Recipient name"
        name-hint="The person receiving the parcels"
        phone-label="Recipient phone"
        :errors="recipientErrors"
      />
      <div class="actions">
        <button type="submit" class="btn">{{ nextLabel }}</button>
        <button v-if="!editingFromReview" type="button" class="btn btn-secondary" @click="go(1)">Back</button>
        <button type="button" class="btn-link" @click="confirmingDiscard = true">
          Cancel and discard this request
        </button>
      </div>
    </form>

    <!-- Step 4: parcels -->
    <form v-if="step === 3" class="card" novalidate aria-labelledby="s-parcels" @submit.prevent="saveParcels">
      <h2 id="s-parcels">Step 4 of 6: Parcels</h2>
      <p class="muted">Describe each parcel. Exact weights are not needed; choose the closest size.</p>
      <fieldset v-for="(p, i) in parcels" :key="p.key" class="parcel" data-testid="parcel">
        <legend>Parcel {{ i + 1 }}</legend>
        <div class="field">
          <span :id="`size-label-${p.key}`" class="label-text"
            >Size <span class="required" aria-hidden="true">*</span></span
          >
          <div
            class="row"
            role="radiogroup"
            :aria-labelledby="`size-label-${p.key}`"
            :aria-invalid="parcelErrors[p.key]?.size ? 'true' : undefined"
          >
            <label v-for="s in sizes" :key="s.value" class="choice size-choice">
              <input v-model="p.size" type="radio" :name="`size-${p.key}`" :value="s.value" />
              <span
                ><strong>{{ s.label }}</strong> <span class="muted small">{{ s.hint }}</span></span
              >
            </label>
          </div>
          <span v-if="parcelErrors[p.key]?.size" class="error-text" data-testid="field-error">{{
            parcelErrors[p.key]?.size
          }}</span>
        </div>
        <fieldset class="field">
          <legend>Handling <span class="muted small">(tick any that apply)</span></legend>
          <div class="row">
            <label class="choice"><input v-model="p.fragile" type="checkbox" /> Fragile</label>
            <label class="choice"><input v-model="p.signature" type="checkbox" /> Signature required</label>
            <label class="choice"><input v-model="p.upright" type="checkbox" /> Keep upright</label>
          </div>
        </fieldset>
        <FormField
          :id="`desc-${p.key}`"
          label="Description"
          hint="Up to 40 characters, e.g. 'documents'"
          :error="parcelErrors[p.key]?.description"
        >
          <template #default="f">
            <input
              :id="f.id"
              v-model="p.description"
              type="text"
              :aria-describedby="f.describedBy"
              :aria-invalid="f.invalid"
            />
          </template>
        </FormField>
        <button
          type="button"
          class="btn btn-secondary"
          :aria-label="`Remove parcel ${i + 1}`"
          @click="removeParcel(i)"
        >
          Remove parcel {{ i + 1 }}
        </button>
      </fieldset>
      <button type="button" class="btn btn-secondary" @click="addParcel">+ Add another parcel</button>
      <div class="actions">
        <button type="submit" class="btn">{{ nextLabel }}</button>
        <button v-if="!editingFromReview" type="button" class="btn btn-secondary" @click="go(2)">Back</button>
        <button type="button" class="btn-link" @click="confirmingDiscard = true">
          Cancel and discard this request
        </button>
      </div>
    </form>

    <!-- Step 5: notes -->
    <form v-if="step === 4" class="card" novalidate aria-labelledby="s-notes" @submit.prevent="saveNotes">
      <h2 id="s-notes">Step 5 of 6: Notes</h2>
      <FormField
        id="notes"
        label="Notes for the office"
        hint="Up to 200 characters, e.g. 'call on arrival'"
        :error="notesError"
      >
        <template #default="f">
          <textarea :id="f.id" v-model="notes" :aria-describedby="f.describedBy" :aria-invalid="f.invalid" />
        </template>
      </FormField>
      <div class="actions">
        <button type="submit" class="btn">{{ editingFromReview ? nextLabel : 'Review request' }}</button>
        <button v-if="!editingFromReview" type="button" class="btn btn-secondary" @click="go(3)">Back</button>
        <button type="button" class="btn-link" @click="confirmingDiscard = true">
          Cancel and discard this request
        </button>
      </div>
    </form>

    <!-- Step 6: review -->
    <section v-if="step === 5" aria-labelledby="s-review">
      <div class="card">
        <h2 id="s-review">Step 6 of 6: Review and submit</h2>
        <p class="muted">Check every detail. Use "Change" to correct anything before you submit.</p>
        <div class="grid-2">
          <div>
            <div class="spread">
              <h3>Pickup</h3>
              <button type="button" class="btn-link" @click="editFromReview(1)">Change pickup</button>
            </div>
            <dl class="facts">
              <dt>Customer</dt>
              <dd>{{ customer?.name }} ({{ customer?.phone }})</dd>
              <dt>Address</dt>
              <dd>{{ pickup.street }}, {{ pickup.suburb }} VIC {{ pickup.postcode }}</dd>
              <dt>Contact</dt>
              <dd>{{ pickupContact.name }} ({{ pickupContact.phone }})</dd>
              <dt>Ready from</dt>
              <dd>{{ when(new Date(readyTime)) }}</dd>
            </dl>
          </div>
          <div>
            <div class="spread">
              <h3>Destination</h3>
              <button type="button" class="btn-link" @click="editFromReview(2)">Change destination</button>
            </div>
            <dl class="facts">
              <dt>Address</dt>
              <dd>{{ destination.street }}, {{ destination.suburb }} VIC {{ destination.postcode }}</dd>
              <dt>Recipient</dt>
              <dd>{{ recipient.name }} ({{ recipient.phone }})</dd>
            </dl>
          </div>
        </div>
        <div class="spread">
          <h3>Parcels ({{ parcels.length }})</h3>
          <button type="button" class="btn-link" @click="editFromReview(3)">Change or add parcels</button>
        </div>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">#</th>
                <th scope="col">Size</th>
                <th scope="col">Handling</th>
                <th scope="col">Description</th>
                <th scope="col"><span class="visually-hidden">Remove</span></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(p, i) in parcels" :key="p.key" data-testid="review-parcel">
                <td>{{ i + 1 }}</td>
                <td>{{ p.size }}</td>
                <td>{{ handlingText(flagsOf(p)) }}</td>
                <td>{{ p.description || '-' }}</td>
                <td>
                  <button type="button" class="btn-link" @click="removeParcel(i)">
                    Remove parcel {{ i + 1 }}
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="spread">
          <h3>Notes</h3>
          <button type="button" class="btn-link" @click="editFromReview(4)">Change notes</button>
        </div>
        <p>{{ notes.trim() || 'No notes.' }}</p>
        <div class="actions">
          <button type="button" class="btn" :disabled="busy" @click="submit">Submit request</button>
          <button type="button" class="btn btn-secondary" @click="confirmingDiscard = true">
            Discard this request
          </button>
        </div>
      </div>
    </section>

    <ConfirmBox
      v-if="confirmingDiscard"
      question="Discard this request? Nothing you have entered will be saved."
      confirm-label="Yes, discard it"
      danger
      @confirm="discard"
      @cancel="confirmingDiscard = false"
    />
  </template>
</template>

<style scoped>
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: center;
  margin-top: 1.25rem;
}

.recent {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 0.75rem 1rem;
  margin-bottom: 1.25rem;
  background: #f8fbfb;
}

.recent legend {
  font-weight: 700;
  padding: 0 0.25rem;
}

.recent .choice {
  margin-bottom: 0.4rem;
}

.parcel {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 0.75rem 1rem 1rem;
  margin-bottom: 1rem;
}

.parcel > legend {
  font-weight: 700;
  padding: 0 0.25rem;
}

.label-text {
  font-weight: 600;
}

.required {
  color: var(--error);
}

.size-choice {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 0.4rem 0.7rem;
}

.done {
  max-width: 720px;
  border-top: 6px solid var(--success);
}

.reference {
  font-size: 1.4rem;
  letter-spacing: 0.04em;
}
</style>
