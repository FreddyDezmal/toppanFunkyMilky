import { Outlet } from 'react-router'

/**
 * Keeps the pages under it at the earlier, narrower reading width inside the wider redesigned frame. Used for the
 * screens that are redesigned in later stages (My orders, order details, management), so they do not stretch.
 */
export function NarrowContent() {
  return (
    <div className="mx-auto w-full max-w-4xl">
      <Outlet />
    </div>
  )
}
