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
      <v-col cols="12" sm="4" md="3">
        <v-btn
          color="red"
          density="compact"
          :disabled="filteredAlarms.length === 0"
          @click="handleDeleteFiltered"
        >
          Delete Filtered ({{ filteredAlarms.length }})
        </v-btn>
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

      <template #[`item.delete`]="{ item }">
        <v-btn
          icon
          density="compact"
          variant="text"
          @click="handleDeleteRow(item)"
        >
          <v-icon color="red">mdi-delete</v-icon>
        </v-btn>
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

      <template #[`item.connectionId`]="{ item }">
        {{ connectionName(item.connectionId) }}
      </template>
    </v-data-table>

    <v-dialog v-model="confirmState.visible" max-width="400">
      <v-card>
        <v-card-title>
          <template v-if="confirmState.type === 'row-active'">
            This alarm is still active on the PLC
          </template>
          <template v-else>
            Delete Filtered Alarms
          </template>
        </v-card-title>
        <v-card-text>
          <template v-if="confirmState.type === 'row-active'">
            Deleting it will only remove the history record — the alarm remains active on the PLC.
          </template>
          <template v-else>
            Delete {{ confirmState.count }} alarm record(s)? This cannot be undone.
          </template>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="confirmState.visible = false">Cancel</v-btn>
          <v-btn
            color="red"
            @click="confirmState.type === 'row-active'
              ? executeDeleteRow(confirmState.item)
              : executeDeleteFiltered()"
          >
            <template v-if="confirmState.type === 'row-active'">Delete anyway</template>
            <template v-else>Delete</template>
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'

const alarms = ref([])
const statusFilter = ref('All')
const alarmClassFilter = ref('All')
const pendingAcks = ref(new Set())
const connectionNameMap = ref({})
let refreshTimer = null

const confirmState = ref({
  visible: false,
  type: null,
  item: null,
  count: 0
})
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
  { title: 'Delete', key: 'delete', sortable: false },
  { title: 'Alarm class name', key: 'alarmClassName', sortable: true },
  { title: 'Event text', key: 'alarmText', sortable: true },
  { title: 'ID', key: 'cpuAlarmId', sortable: true },
  { title: 'Origin DB Name', key: 'originDbName', sortable: true },
  { title: 'DB Number', key: 'dbNumber', sortable: true },
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

const fetchConnectionNames = async () => {
  try {
    const response = await fetch('/Invoke/auth/listProtocolConnections')
    const json = await response.json()
    if (Array.isArray(json)) {
      const map = {}
      json.forEach(conn => {
        map[conn.protocolConnectionNumber] = conn.name
      })
      connectionNameMap.value = map
    }
  } catch (err) {
    console.warn('Failed to fetch protocol connections:', err)
  }
}

const connectionName = (id) => connectionNameMap.value[id] || String(id)

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

const handleDeleteRow = (item) => {
  if (item.alarmState === 'Coming' && !item.ackState) {
    confirmState.value = { visible: true, type: 'row-active', item, count: 0 }
  } else {
    executeDeleteRow(item)
  }
}

const executeDeleteRow = async (item) => {
  const idx = alarms.value.findIndex(a => a._id === item._id)
  if (idx !== -1) alarms.value.splice(idx, 1)
  confirmState.value.visible = false
  await fetch('/Invoke/auth/deleteS7PlusAlarms', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ids: [item._id] })
  })
}

const handleDeleteFiltered = () => {
  confirmState.value = { visible: true, type: 'bulk', item: null, count: filteredAlarms.value.length }
}

const executeDeleteFiltered = async () => {
  const ids = filteredAlarms.value.map(a => a._id)
  const toRemove = new Set(ids)
  alarms.value = alarms.value.filter(a => !toRemove.has(a._id))
  confirmState.value.visible = false
  await fetch('/Invoke/auth/deleteS7PlusAlarms', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ids })
  })
}

onMounted(async () => {
  document.documentElement.style.overflowY = 'scroll'
  await Promise.all([fetchAlarms(), fetchConnectionNames()])
  refreshTimer = setInterval(fetchAlarms, 5000)
})

onUnmounted(() => {
  document.documentElement.style.overflowY = 'hidden'
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>
