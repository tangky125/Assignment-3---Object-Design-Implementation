// A message carried from one screen to the next, e.g. "Quote v1 issued" shown on the queue
// after the prepare-quote screen closes.
import { ref } from 'vue'

export type MessageKind = 'success' | 'error' | 'notice'

export interface Message {
  kind: MessageKind
  text: string
}

const pending = ref<Message | null>(null)

/** Keeps a message for the next screen. */
export function flash(kind: MessageKind, text: string): void {
  pending.value = { kind, text }
}

/** Returns the kept message once, then forgets it. */
export function takeFlash(): Message | null {
  const message = pending.value
  pending.value = null
  return message
}
