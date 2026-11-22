import { Outlet } from 'react-router'

/**
 * Keeps the pages under it at the earlier, narrower reading width inside the wider redesigned frame. Used for the
 * management screens until they are redesigned (Stage 6), so they do not stretch.
 */
export function NarrowContent() {
  return (
    <div className="mx-auto w-full max-w-4xl">
      <Outlet />
    </div>
  )
}
