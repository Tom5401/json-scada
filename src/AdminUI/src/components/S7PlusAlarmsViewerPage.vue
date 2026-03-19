<template>
  <v-container fluid>
    <h2 class="mb-4">S7Plus Alarms Viewer</h2>

    <v-row class="mb-2">
      <v-col cols="12" sm="4" md="3">
        <v-select
          label="Status"
          :items="['All', 'Incoming', 'Outgoing']"
          v-model="statusFilter"
          density="compact"
        />
      </v-col>
      <v-col cols="12" sm="4" md="3">
        <v-select
          label="Alarm Class"
          :items="alarmClassOptions"
          v-model="alarmClassFilter"
          density="compact"
        />
      </v-col>
    </v-row>

    <v-data-table
      :headers="headers"
      :items="filteredAlarms"
      density="compact"
      class="elevation-1"
      :items-per-page="50"
      :items-per-page-options="[25, 50, 100, 200]"
    >
      <template #[`item.alarmState`]="{ item }">
        <v-chip :color="item.alarmState === 'Coming' ? 'red' : 'green'" size="small">
          {{ item.alarmState }}
        </v-chip>
      </template>

      <template #[`item.ackState`]="{ item }">
        <v-icon v-if="item.ackState" color="green">mdi-check</v-icon>
        <template v-else>
          <v-progress-circular
            v-if="pendingAcks.has(item.cpuAlarmId)"
            indeterminate
            size="16"
            width="2"
          />
          <v-btn
            v-else
            size="x-small"
            variant="tonal"
            @click="ackAlarm(item.cpuAlarmId, item.connectionId)"
          >
            Ack
          </v-btn>
        </template>
      </template>

      <template #[`item.date`]="{ item }">
        {{ formatDate(item.timestamp) }}
      </template>

      <template #[`item.time`]="{ item }">
        {{ formatTime(item.timestamp) }}
      </template>

      <template #[`item.additionalText1`]="{ item }">
        {{ item.additionalTexts && item.additionalTexts[0] }}
      </template>

      <template #[`item.additionalText2`]="{ item }">
        {{ item.additionalTexts && item.additionalTexts[1] }}
      </template>

      <template #[`item.additionalText3`]="{ item }">
        {{ item.additionalTexts && item.additionalTexts[2] }}
      </template>
    </v-data-table>
  </v-container>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'

const alarms = ref([])
const statusFilter = ref('All')
const alarmClassFilter = ref('All')
const pendingAcks = ref(new Set())
let refreshTimer = null

const ackAlarm = async (cpuAlarmId, connectionNumber) => {
  pendingAcks.value = new Set([...pendingAcks.value, cpuAlarmId])
  try {
    const response = await fetch('/Invoke/auth/ackS7PlusAlarm', {
      method: 'post',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ cpuAlarmId, connectionNumber }),
    })
    const json = await response.json()
    if (json.error) {
      console.warn('Ack failed:', json.error)
      pendingAcks.value = new Set([...pendingAcks.value].filter(id => id !== cpuAlarmId))
    }
    // On success: pending state stays until next poll confirms ackState: true
  } catch (err) {
    console.warn('Ack failed:', err)
    pendingAcks.value = new Set([...pendingAcks.value].filter(id => id !== cpuAlarmId))
  }
}

const headers = [
  { title: 'Source', key: 'connectionId', sortable: true },
  { title: 'Date', key: 'date', sortable: false },
  { title: 'Time', key: 'time', sortable: false },
  { title: 'Status', key: 'alarmState', sortable: true },
  { title: 'Acknowledge', key: 'ackState', sortable: true },
  { title: 'Alarm class name', key: 'alarmClassName', sortable: true },
  { title: 'Event text', key: 'alarmText', sortable: true },
  { title: 'ID', key: 'cpuAlarmId', sortable: true },
  { title: 'Additional text 1', key: 'additionalText1', sortable: false },
  { title: 'Additional text 2', key: 'additionalText2', sortable: false },
  { title: 'Additional text 3', key: 'additionalText3', sortable: false },
]

const formatDate = (isoStr) => {
  if (!isoStr) return ''
  return new Date(isoStr).toLocaleDateString()
}

const formatTime = (isoStr) => {
  if (!isoStr) return ''
  return new Date(isoStr).toLocaleTimeString()
}

const alarmClassOptions = computed(() => {
  const classes = [...new Set(
    alarms.value.map(a => a.alarmClassName).filter(Boolean)
  )]
  return ['All', ...classes.sort()]
})

const filteredAlarms = computed(() => {
  return alarms.value.filter(alarm => {
    const stateMatch =
      statusFilter.value === 'All' ||
      (statusFilter.value === 'Incoming' && alarm.alarmState === 'Coming') ||
      (statusFilter.value === 'Outgoing' && alarm.alarmState === 'Going')
    const classMatch =
      alarmClassFilter.value === 'All' ||
      alarm.alarmClassName === alarmClassFilter.value
    return stateMatch && classMatch
  })
})

const fetchAlarms = async () => {
  try {
    const response = await fetch('/Invoke/auth/listS7PlusAlarms')
    const json = await response.json()
    if (Array.isArray(json)) {
      alarms.value = json
      // Resolve pending ack states: remove IDs that are now acknowledged OR still false (allow retry)
      if (pendingAcks.value.size > 0) {
        const stillPending = new Set()
        for (const id of pendingAcks.value) {
          const alarm = json.find(a => a.cpuAlarmId === id)
          if (alarm && !alarm.ackState) {
            // PLC has not yet acknowledged — keep pending for one more cycle only
            // Do NOT keep pending: remove to allow retry (per CONTEXT.md decision)
          }
          // If alarm.ackState === true or alarm not found: confirmed, remove from pending
        }
        pendingAcks.value = stillPending
      }
    }
  } catch (err) {
    console.warn('Failed to fetch S7Plus alarms:', err)
  }
}

onMounted(async () => {
  document.documentElement.style.overflowY = 'scroll'
  await fetchAlarms()
  refreshTimer = setInterval(fetchAlarms, 5000)
})

onUnmounted(() => {
  document.documentElement.style.overflowY = 'hidden'
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>
