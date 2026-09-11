import { ref, type Ref } from 'vue'

/**
 * 管理通道选择弹窗。
 * - `openChannelDialog`：阅图中从工具栏打开；取消只关闭，不触发重读。
 * - `openInitialChannelDialog`：导入后立即打开；确认或取消都结束首次选择，
 *   触发 `afterResolve`（由调用方停止回放并按当前通道从文件 0 秒重读）。
 */
export function useChannelSelection(
  selectedChannels: Ref<string[]>,
  afterResolve: () => Promise<void>,
) {
  const isChannelDialogOpen = ref(false)
  let isInitialChoice = false

  function openChannelDialog() {
    isInitialChoice = false
    isChannelDialogOpen.value = true
  }

  function openInitialChannelDialog() {
    isInitialChoice = true
    isChannelDialogOpen.value = true
  }

  function closeChannelDialog() {
    if (!isChannelDialogOpen.value) return
    isChannelDialogOpen.value = false
    if (!isInitialChoice) return
    isInitialChoice = false
    void afterResolve()
  }

  async function applyChannels(channels: string[]) {
    if (!channels.length) return
    selectedChannels.value = channels
    isInitialChoice = false
    isChannelDialogOpen.value = false
    await afterResolve()
  }

  return { isChannelDialogOpen, openChannelDialog, openInitialChannelDialog, closeChannelDialog, applyChannels }
}
