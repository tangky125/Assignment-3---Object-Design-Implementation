// Screens and who may open them. The server checks every request again; these guards only
// keep people out of screens they cannot use.
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import type { UserRole } from '@/api/types'
import { hasRole, restoreSession, session } from '@/session/session'
import { flash } from '@/session/flash'

declare module 'vue-router' {
  interface RouteMeta {
    /** Screen name shown in the browser tab. */
    title: string
    /** Roles allowed in; empty or missing = anyone signed in; public = no sign-in needed. */
    roles?: UserRole[]
    public?: boolean
  }
}

const office: UserRole[] = ['OfficeStaff', 'Owner']
const staff: UserRole[] = ['OfficeStaff', 'Owner', 'DeliveryStaff']

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    name: 'home',
    component: () => import('@/views/HomeView.vue'),
    meta: { title: 'Home', public: true },
  },
  {
    path: '/sign-in',
    name: 'sign-in',
    component: () => import('@/views/SignInView.vue'),
    meta: { title: 'Sign in', public: true },
  },
  {
    path: '/signed-out',
    name: 'signed-out',
    component: () => import('@/views/SignedOutView.vue'),
    meta: { title: 'Signed out', public: true },
  },
  {
    path: '/help',
    name: 'help',
    component: () => import('@/views/HelpView.vue'),
    meta: { title: 'Help', public: true },
  },
  {
    path: '/track/:reference?',
    name: 'track',
    component: () => import('@/views/track/TrackView.vue'),
    meta: { title: 'Track a delivery', public: true },
  },
  {
    path: '/dashboard',
    name: 'dashboard',
    component: () => import('@/views/DashboardView.vue'),
    meta: { title: 'Main menu' },
  },
  {
    path: '/requests/new',
    name: 'log-request',
    component: () => import('@/views/requests/LogRequestView.vue'),
    meta: { title: 'Log a delivery request', roles: ['Customer', ...office] },
  },
  {
    path: '/my/requests',
    name: 'my-requests',
    component: () => import('@/views/requests/CustomerRequestsView.vue'),
    meta: { title: 'My requests and quotes', roles: ['Customer'] },
  },
  {
    path: '/decisions',
    name: 'find-customer',
    component: () => import('@/views/requests/FindCustomerView.vue'),
    meta: { title: "Record a customer's decision", roles: office },
  },
  {
    path: '/customers/:customerId/requests',
    name: 'customer-requests',
    component: () => import('@/views/requests/CustomerRequestsView.vue'),
    meta: { title: 'Customer requests', roles: office },
  },
  {
    path: '/requests/:reference',
    name: 'request',
    component: () => import('@/views/requests/RequestView.vue'),
    meta: { title: 'Request' },
  },
  {
    path: '/quotes',
    name: 'quote-queue',
    component: () => import('@/views/quotes/QuoteQueueView.vue'),
    meta: { title: 'Prepare quotes', roles: office },
  },
  {
    path: '/quotes/:reference',
    name: 'prepare-quote',
    component: () => import('@/views/quotes/PrepareQuoteView.vue'),
    meta: { title: 'Prepare a quote', roles: office },
  },
  {
    path: '/owner/approvals',
    name: 'owner-approvals',
    component: () => import('@/views/quotes/OwnerApprovalView.vue'),
    meta: { title: 'Out-of-area quotes', roles: ['Owner'] },
  },
  {
    path: '/allocation',
    name: 'allocation',
    component: () => import('@/views/allocation/AllocationView.vue'),
    meta: { title: 'Allocate delivery staff', roles: office },
  },
  {
    path: '/allocation/:reference',
    name: 'allocate',
    component: () => import('@/views/allocation/AllocateView.vue'),
    meta: { title: 'Choose a staff member', roles: office },
  },
  {
    path: '/jobs',
    name: 'jobs',
    component: () => import('@/views/jobs/MyJobsView.vue'),
    meta: { title: 'My jobs', roles: ['DeliveryStaff'] },
  },
  {
    path: '/jobs/:reference',
    name: 'job',
    component: () => import('@/views/jobs/JobView.vue'),
    meta: { title: 'Job', roles: ['DeliveryStaff'] },
  },
  {
    path: '/search',
    name: 'search',
    component: () => import('@/views/track/SearchView.vue'),
    meta: { title: 'Track or search deliveries', roles: staff },
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/views/NotFoundView.vue'),
    meta: { title: 'Page not found', public: true },
  },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
})

router.beforeEach(async (to) => {
  await restoreSession()
  if (to.meta.public) {
    return true
  }
  if (session.user === null) {
    return { name: 'sign-in', query: { redirect: to.fullPath } }
  }
  const roles = to.meta.roles ?? []
  if (roles.length > 0 && !hasRole(...roles)) {
    flash('error', 'Your account cannot open that screen.')
    return { name: 'dashboard' }
  }
  return true
})

router.afterEach((to) => {
  document.title = `${to.meta.title} - Your Order We Deliver`
})
