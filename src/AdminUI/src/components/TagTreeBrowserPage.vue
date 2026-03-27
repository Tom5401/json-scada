<template>
  <v-container fluid>
    <h2 class="mb-4">Tag Tree Browser — {{ dbName }}</h2>

    <v-treeview
      :items="treeItems"
      item-value="id"
      item-title="name"
      item-children="children"
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
        <template v-if="item.isLeaf">
          <v-chip size="x-small" class="ml-1">{{ item.type }}</v-chip>
          <code class="ml-2 text-caption">{{ item.value }}</code>
          <span class="ml-2 text-caption text-medium-emphasis font-weight-light" style="font-family: monospace;">{{ item.address }}</span>
        </template>
      </template>
    </v-treeview>
  </v-container>
</template>

<script setup>
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const dbName = ref('')
const connectionNumber = ref(null)
const treeItems = ref([])
const openedNodes = ref([])
let refreshTimer = null

function buildTree(docs, dbName) {
  const root = { id: dbName, name: dbName, isLeaf: false, children: [] }
  for (const doc of docs) {
    // ungroupedDescription holds the full hierarchical path (e.g. "DB.Struct.Tag").
    // protocolSourceBrowsePath is the PARENT path (everything before the last dot),
    // which cannot distinguish leaves from intermediate folders.
    const fullPath = doc.ungroupedDescription
    if (!fullPath) continue
    const segments = fullPath.split('.')
    const remaining = segments.slice(1) // skip DB name (segments[0])
    if (remaining.length === 0) continue
    let current = root
    for (let i = 0; i < remaining.length; i++) {
      const seg = remaining[i]
      const fullId = segments.slice(0, i + 2).join('.')
      const isLast = i === remaining.length - 1
      let child = current.children.find(c => c.id === fullId)
      if (!child) {
        child = {
          id: fullId,
          name: seg,
          isLeaf: isLast,
          children: isLast ? undefined : [],
          ...(isLast ? {
            type: doc.type || '',
            value: doc.value,
            address: doc.protocolSourceObjectAddress || '',
          } : {})
        }
        current.children.push(child)
      }
      current = child
    }
  }
  return root
}

function patchLeafValues(treeRoot, freshDocs) {
  const docMap = new Map(freshDocs.map(d => [d.ungroupedDescription, d]))
  function walk(node) {
    if (node.isLeaf) {
      const doc = docMap.get(node.id)
      if (doc) node.value = doc.value
    } else if (node.children) {
      for (const child of node.children) walk(child)
    }
  }
  walk(treeRoot)
}

function getExpandedLeafTags(treeRoot, openedIds) {
  const openSet = new Set(openedIds)
  const result = []
  function walk(node, parentOpen) {
    const isOpen = parentOpen || openSet.has(node.id)
    if (node.isLeaf && parentOpen) {
      result.push({
        connectionNumber: connectionNumber.value,
        protocolSourceObjectAddress: node.address,
      })
    } else if (node.children) {
      for (const child of node.children) walk(child, isOpen)
    }
  }
  if (treeRoot) {
    for (const child of treeRoot.children || []) walk(child, openSet.has(treeRoot.id))
  }
  return result
}

const touchExpandedLeafTags = async () => {
  if (!treeItems.value[0]) return
  const tags = getExpandedLeafTags(treeItems.value[0], openedNodes.value)
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

const refreshValues = async () => {
  if (!dbName.value || !connectionNumber.value) return
  try {
    const res = await fetch(
      `/Invoke/auth/listS7PlusTagsForDb?connectionNumber=${connectionNumber.value}&dbName=${encodeURIComponent(dbName.value)}`
    )
    const docs = await res.json()
    if (Array.isArray(docs) && treeItems.value[0]) {
      patchLeafValues(treeItems.value[0], docs)
      await touchExpandedLeafTags()
    }
  } catch (err) {
    console.warn('Failed to refresh tag values:', err)
  }
}

const loadTree = async () => {
  try {
    const res = await fetch(
      `/Invoke/auth/listS7PlusTagsForDb?connectionNumber=${connectionNumber.value}&dbName=${encodeURIComponent(dbName.value)}`
    )
    const docs = await res.json()
    const root = buildTree(docs, dbName.value)
    treeItems.value = [root]
    // Auto-expand first level (D-01): open root node so its direct children are visible
    openedNodes.value = [root.id]
  } catch (err) {
    console.warn('Failed to load tag tree:', err)
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
    await loadTree()
  }
  refreshTimer = setInterval(() => refreshValues(), 5000)
})

onUnmounted(() => {
  document.documentElement.style.overflowY = 'hidden'
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>
