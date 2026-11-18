import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterEach } from 'vitest'

// findBy*/waitFor wait up to 5 s (default 1 s), so a heavily loaded machine makes tests slower, not flaky.
configure({ asyncUtilTimeout: 5000 })

// Unmount rendered components between tests so they cannot affect each other.
afterEach(() => {
  cleanup()
})