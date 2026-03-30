<template>
  <v-dialog v-model="dialogOpen" max-width="450" persistent>
    <v-card>
      <v-card-title>Modify — {{ item?.name }}</v-card-title>
      <v-card-text>
        <div class="mb-3 text-caption text-medium-emphasis">
          Current value: <code>{{ currentDisplayValue }}</code>
        </div>
        <v-select
          v-if="item?.type === 'digital'"
          v-model="inputValue"
          :items="['TRUE', 'FALSE']"
          label="New value"
          variant="outlined"
          density="compact"
        />
        <v-text-field
          v-else
          v-model="inputValue"
          label="New value"
          variant="outlined"
          density="compact"
        />
        <v-alert
          v-if="resultMsg"
          :type="resultType"
          density="compact"
          class="mt-3"
        >{{ resultMsg }}</v-alert>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn :disabled="sending" @click="close">Cancel</v-btn>
        <v-btn color="primary" :loading="sending" @click="submitWrite">Write</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  item: { type: Object, default: null },
})
const emit = defineEmits(['update:modelValue'])

// OPC constants inlined — opc-codes.js is not an ES module
const OPC = {
  WriteRequest: 671,
  WriteResponse: 674,
  Numeric: 0,
  Value: 13,
  Double: 11,
  String: 12,
  Namespace: 2,
  Good: 0,
}

const inputValue = ref('')
const sending = ref(false)
const resultMsg = ref('')
const resultType = ref('success')
let autoCloseTimer = null

const dialogOpen = computed({
  get: () => props.modelValue,
  set: (val) => emit('update:modelValue', val),
})

const currentDisplayValue = computed(() => {
  if (!props.item) return ''
  if (props.item.type === 'digital') return props.item.value ? 'TRUE' : 'FALSE'
  return props.item.value
})

watch(
  () => props.modelValue,
  (open) => {
    if (open && props.item) {
      resultMsg.value = ''
      sending.value = false
      if (props.item.type === 'digital') {
        inputValue.value = props.item.value ? 'TRUE' : 'FALSE'
      } else {
        inputValue.value = String(props.item.value ?? '')
      }
    }
    if (!open && autoCloseTimer) {
      clearTimeout(autoCloseTimer)
      autoCloseTimer = null
    }
  }
)

function close() {
  if (autoCloseTimer) {
    clearTimeout(autoCloseTimer)
    autoCloseTimer = null
  }
  resultMsg.value = ''
  sending.value = false
  inputValue.value = ''
  emit('update:modelValue', false)
}

async function submitWrite() {
  if (!props.item) return
  sending.value = true
  resultMsg.value = ''

  let valueType, valueBody
  if (props.item.type === 'digital') {
    valueType = OPC.Double
    valueBody = inputValue.value === 'TRUE' ? 1.0 : 0.0
  } else {
    const parsed = parseFloat(inputValue.value)
    if (isNaN(parsed)) {
      valueType = OPC.String
      valueBody = inputValue.value
    } else {
      valueType = OPC.Double
      valueBody = parsed
    }
  }

  const body = {
    ServiceId: OPC.WriteRequest,
    Body: {
      RequestHeader: {
        Timestamp: new Date().toISOString(),
        RequestHandle: Math.floor(Math.random() * 100000000),
        TimeoutHint: 1500,
        ReturnDiagnostics: 2,
        AuthenticationToken: null,
      },
      NodesToWrite: [
        {
          NodeId: { IdType: OPC.Numeric, Id: props.item.commandOfSupervised, Namespace: OPC.Namespace },
          AttributeId: OPC.Value,
          Value: { Type: valueType, Body: valueBody },
        },
      ],
    },
  }

  try {
    const res = await fetch('/Invoke/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    const data = await res.json()
    const success =
      data.ServiceId === OPC.WriteResponse &&
      data.Body?.ResponseHeader?.ServiceResult === OPC.Good &&
      data.Body?.Results?.[0] === OPC.Good

    if (success) {
      resultType.value = 'success'
      resultMsg.value = 'Value written successfully'
      autoCloseTimer = setTimeout(() => close(), 2000)
    } else {
      resultType.value = 'error'
      resultMsg.value = data.Body?.ResponseHeader?.StringTable?.[2] || 'Write failed'
    }
  } catch {
    resultType.value = 'error'
    resultMsg.value = 'Network error'
  } finally {
    sending.value = false
  }
}
</script>
