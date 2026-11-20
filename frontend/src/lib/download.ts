/** Hands a downloaded file to the browser to save under this name. */
export function saveFile(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.rel = 'noopener'
  document.body.append(link)
  link.click()
  link.remove()
  // Give the browser a moment to start the download before the address is released.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/** Rows as tab-separated text (pastes into a spreadsheet as cells); tabs and line breaks inside values become spaces. */
export function toTabSeparated(rows: readonly (readonly string[])[]): string {
  return rows.map((row) => row.map((cell) => cell.replace(/[\t\r\n]+/g, ' ')).join('\t')).join('\n')
}
