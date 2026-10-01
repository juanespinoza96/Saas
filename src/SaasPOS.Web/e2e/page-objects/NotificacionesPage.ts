import { type Page, type Locator, expect } from '@playwright/test';

export class NotificacionesPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly emptyState: Locator;
  readonly notificationCards: Locator;
  readonly markAsReadButtons: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: 'Notificaciones' });
    this.emptyState = page.getByText('No tienes notificaciones.');
    this.notificationCards = page.locator('.border-l-4');
    this.markAsReadButtons = page.getByRole('button', { name: 'Marcar como leída' });
  }

  async goto() {
    await this.page.goto('/notificaciones');
  }

  async expectLoaded() {
    await expect(this.heading).toBeVisible();
  }

  /**
   * Returns the count of unread notifications (those with "Marcar como leída" button visible).
   */
  async getUnreadCount(): Promise<number> {
    // Unread notifications have font-bold title and "Marcar como leída" button
    return this.markAsReadButtons.count();
  }

  /**
   * Marks the first unread notification as read by clicking its "Marcar como leída" button.
   */
  async markAsRead(): Promise<void> {
    const firstButton = this.markAsReadButtons.first();
    await expect(firstButton).toBeVisible();
    await firstButton.click();
    // Wait for the button to disappear (notification marked as read)
    await expect(firstButton).toBeHidden({ timeout: 5000 }).catch(() => {
      // Button might still be visible if there are more unread — just wait a tick
    });
    // Small wait for UI update
    await this.page.waitForTimeout(300);
  }

  /**
   * Returns all notification titles displayed on the page.
   */
  async getNotificationTitles(): Promise<string[]> {
    const titles = this.notificationCards.locator('h3');
    return titles.allTextContents();
  }

  /**
   * Checks whether the empty state message is displayed.
   */
  async expectEmptyState() {
    await expect(this.emptyState).toBeVisible();
  }

  /**
   * Checks that unread notifications are visually distinct (border-l-action-confirm).
   */
  async expectUnreadHighlighted() {
    const unreadCards = this.page.locator('.border-l-action-confirm');
    const count = await unreadCards.count();
    expect(count).toBeGreaterThan(0);
  }
}
