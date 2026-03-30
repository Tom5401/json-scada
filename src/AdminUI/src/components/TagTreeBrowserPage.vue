<template>
  <v-container fluid>
    <h2 class="mb-4">Tag Tree Browser — {{ dbName }}</h2>

    <div class="d-flex justify-end mb-1">
      <div class="column-header-row">
        <span>Type</span>
        <span>Value</span>
        <span>Timestamp</span>
        <span>Address</span>
        <span>Actions</span>
      </div>
    </div>

    <v-treeview
      :items="treeItems"
      item-value="id"
      item-title="name"
      item-children="children"
      :load-children="onLoadChildren"
      v-model:opened="openedNodes"
      open-strategy="multiple"
      density="compact"
      class="elevation-1"
    >
      <template #prepend="{ item }">
        <v-icon v-if="item.isLeaf">mdi-tag</v-icon>
        <v-icon v-else>mdi-folder</v-icon>
      </template>
      <template #append="{ item }">
        <div v-if="item.isLeaf" class="leaf-columns">
          <span class="leaf-col-type"><v-chip size="x-small">{{ item.type }}</v-chip></span>
          <span class="leaf-col-value"><code class="text-caption">{{ formatLeafValue(item) }}</code></span>
          <span class="leaf-col-timestamp text-caption text-medium-emphasis" style="font-family: monospace;">{{ formatSourceTime(item.timeTagAtSource) }}</span>
          <span class="leaf-col-address text-caption text-medium-emphasis font-weight-light" style="font-family: monospace;">{{ item.address }}</span>
          <span class="leaf-col-actions">
            <v-btn v-if="item.commandOfSupervised !== 0" variant="outlined" size="x-small" @click.stop="openWriteDialog(item)">Modify</v-btn>
          </span>
        </div>
      </template>
    </v-treeview>
    <PushValueDialog v-model="writeDialogOpen" :item="writeDialogItem" />
  </v-container>
</template>

<script setup>
import { ref, watch, onMounted, onUnmounted } from 'vue'
import PushValueDialog from './PushValueDialog.vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const dbName = ref('')
const connectionNumber = ref(null)
const treeItems = ref([])
const openedNodes = ref([])
let refreshTimer = null

const writeDialogOpen = ref(false)
const writeDialogItem = ref(null)
function openWriteDialog(item) {
  writeDialogItem.value = item
  writeDialogOpen.value = true
}

function mapDocToNode(doc) {
  const segments = doc.ungroupedDescription.split('.')
  const name = segments[segments.length - 1]
  if (doc.hasChildren) {
    return {
      id: doc.ungroupedDescription,
      name,
      isLeaf: false,
      children: [],
    }
  }
  return {
    id: doc.ungroupedDescription,
    name,
    isLeaf: true,
    children: undefined,
    type: doc.type || '',
    value: doc.value,
    valueString: doc.valueString,
    address: doc.protocolSourceObjectAddress || '',
    commandOfSupervised: doc.commandOfSupervised || 0,
    timeTagAtSource: doc.timeTagAtSource || null,
  }
}

function formatLeafValue(item) {
  if (item.type === 'digital') {
    return item.value ? 'TRUE' : 'FALSE'
  }
  return item.value
}

function formatSourceTime(isoString) {
  if (!isoString) return ''
  const d = new Date(isoString)
  if (isNaN(d.getTime())) return ''
  return d.toLocaleString(undefined, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    second: '2-digit',
    fractionalSecondDigits: 3,
  })
}

function buildNodeMap(nodes, map = new Map()) {
  for (const node of nodes) {
    map.set(node.id, node)
    if (Array.isArray(node.children)) buildNodeMap(node.children, map)
  }
  return map
}

function getExpandedParentPaths(treeNodes, openSet) {
  const result = []
  function walk(node) {
    if (!node.isLeaf && Array.isArray(node.children) && node.children.length > 0 && openSet.has(node.id)) {
      result.push(node.id)
      for (const child of node.children) walk(child)
    }
  }
  for (const node of treeNodes) walk(node)
  return result
}

function getExpandedLeafTags(treeNodes, openSet) {
  const result = []
  function walk(node) {
    if (node.isLeaf) {
      result.push({
        connectionNumber: connectionNumber.value,
        protocolSourceObjectAddress: node.address,
      })
    } else if (Array.isArray(node.children) && openSet.has(node.id)) {
      for (const child of node.children) walk(child)
    }
  }
  for (const root of treeNodes) {
    if (Array.isArray(root.children) && openSet.has(root.id)) {
      for (const child of root.children) walk(child)
    }
  }
  return result
}

const touchExpandedLeafTags = async () => {
  const openSet = new Set(openedNodes.value)
  const tags = getExpandedLeafTags(treeItems.value, openSet)
  if (tags.length === 0) return // backend rejects empty array with 400
  try {
    await fetch('/Invoke/auth/touchS7PlusActiveTagRequests', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(tags),
    })
  } catch (err) {
    console.warn('Failed to touch active tag requests:', err)
  }
}

const onLoadChildren = async (item) => {
  try {
    const res = await fetch(
      `/Invoke/auth/listS7PlusChildNodes?connectionNumber=${connectionNumber.value}&path=${encodeURIComponent(item.id)}`
    )
    const docs = await res.json()
    item.children = Array.isArray(docs) ? docs.map(mapDocToNode) : []
  } catch (err) {
    console.warn('Failed to load children for', item.id, err)
    item.children = []
  }
}

const loadRootChildren = async () => {
  try {
    const res = await fetch(
      `/Invoke/auth/listS7PlusChildNodes?connectionNumber=${connectionNumber.value}&path=${encodeURIComponent(dbName.value)}`
    )
    const docs = await res.json()
    const children = Array.isArray(docs) ? docs.map(mapDocToNode) : []
    const root = { id: dbName.value, name: dbName.value, isLeaf: false, children }
    treeItems.value = [root]
    openedNodes.value = [root.id]
  } catch (err) {
    console.warn('Failed to load root children:', err)
  }
}

const refreshValues = async () => {
  if (!dbName.value || !connectionNumber.value || !treeItems.value[0]) return
  const openSet = new Set(openedNodes.value)
  const expandedPaths = getExpandedParentPaths(treeItems.value, openSet)
  if (expandedPaths.length === 0) return

  const nodeMap = buildNodeMap(treeItems.value)
  try {
    const responses = await Promise.all(
      expandedPaths.map(path =>
        fetch(`/Invoke/auth/listS7PlusChildNodes?connectionNumber=${connectionNumber.value}&path=${encodeURIComponent(path)}`)
          .then(r => r.json())
          .catch(() => [])
      )
    )
    for (const docs of responses) {
      if (!Array.isArray(docs)) continue
      for (const doc of docs) {
        const node = nodeMap.get(doc.ungroupedDescription)
        if (node && node.isLeaf) {
          node.value = doc.value
          node.valueString = doc.valueString
          node.timeTagAtSource = doc.timeTagAtSource || null
        }
      }
    }
    await touchExpandedLeafTags()
  } catch (err) {
    console.warn('Failed to refresh tag values:', err)
  }
}

watch(openedNodes, () => {
  touchExpandedLeafTags()
}, { deep: true })

onMounted(async () => {
  document.documentElement.style.overflowY = 'scroll'
  dbName.value = route.query.db || ''
  connectionNumber.value = parseInt(route.query.connectionNumber, 10) || null
  if (dbName.value && connectionNumber.value) {
    await loadRootChildren()
  }
  refreshTimer = setInterval(() => refreshValues(), 5000)
})

onUnmounted(() => {
  document.documentElement.style.overflowY = 'hidden'
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>

<style scoped>
.leaf-columns {
  display: grid;
  grid-template-columns: 80px 120px 220px 220px 80px;
  gap: 4px;
  align-items: center;
  min-width: 720px;
}

.leaf-col-type,
.leaf-col-value,
.leaf-col-timestamp,
.leaf-col-address,
.leaf-col-actions {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.column-header-row {
  display: grid;
  grid-template-columns: 80px 120px 220px 220px 80px;
  gap: 4px;
  min-width: 720px;
  padding: 4px 0;
  font-size: 0.75rem;
  font-weight: 600;
  color: rgba(255, 255, 255, 0.7);
  border-bottom: 1px solid rgba(255, 255, 255, 0.2);
}

:deep(.v-treeview-item) {
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
}
</style>
