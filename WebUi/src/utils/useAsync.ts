// Loads data for a screen and keeps track of loading and error states.
import { ref, type Ref } from 'vue'
import { ApiRequestError } from '@/api/client'

export interface AsyncState<T> {
  data: Ref<T | null>
  loading: Ref<boolean>
  error: Ref<string | null>
  reload: () => Promise<void>
}

/** Plain-language text for any error thrown by the API client. */
export function errorText(e: unknown): string {
  return e instanceof ApiRequestError ? e.message : 'Something went wrong. Please try again.'
}

export function useAsync<T>(load: () => Promise<T>): AsyncState<T> {
  const data = ref<T | null>(null) as Ref<T | null>
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function reload(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      data.value = await load()
    } catch (e) {
      error.value = errorText(e)
    } finally {
      loading.value = false
    }
  }

  void reload()
  return { data, loading, error, reload }
}
