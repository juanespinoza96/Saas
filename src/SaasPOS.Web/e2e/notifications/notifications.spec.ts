import { test, expect } from '@playwright/test';
import { NotificacionesPage } from '../page-objects/NotificacionesPage';
import { DashboardPage } from '../page-objects/DashboardPage';

// ─── 30.2: Badge count and marking read decrements ───────────────────────────
test.describe('Notificaciones - Badge y Marcar como leída', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Badge count matches unread notifications count', async ({ page }) => {
    // First check the badge count on dashboard
    const dashboard = new DashboardPage(page);
    await page.goto('/dashboard');
    await dashboard.expectLoaded();
    const badgeCount = await dashboard.getNotificationCount();

    // Navigate to notifications page
    const notificaciones = new NotificacionesPage(page);
    await notificaciones.goto();
    await notificaciones.expectLoaded();

    const unreadCount = await notificaciones.getUnreadCount();

    // Badge count on dashboard should match unread count on notifications page
    expect(badgeCount).toBe(unreadCount);
  });

  test('Marking notification as read decrements unread count', async ({ page }) => {
    const notificaciones = new NotificacionesPage(page);
    await notificaciones.goto();
    await notificaciones.expectLoaded();

    const initialCount = await notificaciones.getUnreadCount();

    // Skip test if no unread notifications
    test.skip(initialCount === 0, 'No unread notifications to mark as read');

    // Mark first unread notification as read
    await notificaciones.markAsRead();

    // Verify the count decreased by one
    const newCount = await notificaciones.getUnreadCount();
    expect(newCount).toBe(initialCount - 1);
  });
});
