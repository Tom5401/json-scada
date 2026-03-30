<template>
  <v-container fluid>
    <h2 class="mb-4">Datablock Browser</h2>

    <v-row class="mb-2">
      <v-col cols="12" sm="4" md="3">
        <v-select
          label="Connection"
          :items="connectionOptions"
          item-title="title"
          item-value="value"
          v-model="selectedConnection"
          density="compact"
          placeholder="Select a connection..."
          clearable
        />
      </v-col>
    </v-row>

    <v-data-table
      :headers="headers"
      :items="datablocks"
      density="compact"
      class="elevation-1"
      :items-per-page="50"
    >
      <template #item="{ item, columns }">
        <tr v-if="item._isDivider" class="bg-grey-lighten-3">
          <td :colspan="columns.length" class="text-subtitle-2 font-weight-bold py-1">
            {{ item._label }}
          </td>
        </tr>
        <tr v-else>
          <td>{{ item.db_name }}</td>
          <td>{{ item.db_number }}</td>
          <td>
            <v-btn size="x-small" variant="tonal" @click="browseDatablock(item)">
              Browse Tags
            </v-btn>
          </td>
        </tr>
      </template>
    </v-data-table>
  </v-container>
</template>

<script setup>
import { ref, watch, onMounted, onUnmounted } from 'vue'

const connectionOptions = ref([])
const selectedConnection = ref(null)
const datablocks = ref([])

const headers = [
  { title: 'DB Name', key: 'db_name', sortable: true },
  { title: 'DB Number', key: 'db_number', sortable: true },
  { title: 'Actions', key: 'actions', sortable: false },
]

const AREA_NAMES = ['IArea', 'QArea', 'MArea', 'S7Timers', 'S7Counters']
const MEMORY_AREA_DIVIDER = { _isDivider: true, _label: 'Memory Areas', db_name: '', db_number: '' }
const VIRTUAL_AREA_ROWS = AREA_NAMES.map((name) => ({ db_name: name, db_number: '\u2014', _isDivider: false }))

onMounted(async () => {
  document.documentElement.style.overflowY = 'scroll'
  try {
    const res = await fetch('/Invoke/auth/listProtocolConnections')
    const json = await res.json()
    if (Array.isArray(json)) {
      connectionOptions.value = json.map(c => ({
        title: c.name,
        value: c.protocolConnectionNumber,
      }))
    }
  } catch (err) {
    console.warn('Failed to fetch connections:', err)
  }
})

onUnmounted(() => {
  document.documentElement.style.overflowY = 'hidden'
})

watch(selectedConnection, async (newVal) => {
  if (newVal !== null && newVal !== undefined) {
    try {
      const res = await fetch(
        `/Invoke/auth/listS7PlusDatablocks?connectionNumber=${newVal}`
      )
      const json = await res.json()
      const real = Array.isArray(json) ? json : []
      datablocks.value = [MEMORY_AREA_DIVIDER, ...VIRTUAL_AREA_ROWS, ...real]
    } catch (err) {
      console.warn('Failed to fetch datablocks:', err)
      datablocks.value = []
    }
  } else {
    datablocks.value = []
  }
})

const browseDatablock = (row) => {
  const url = `/#/s7plus-tag-tree?db=${encodeURIComponent(row.db_name)}&connectionNumber=${selectedConnection.value}`
  window.open(url, '_blank')
}
</script>