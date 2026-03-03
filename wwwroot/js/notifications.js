// ==========================================
// Shared Notification Functions
// Used by all role layouts (Admin, Client, Lawyer, Staff, Auditor)
// ==========================================

document.addEventListener('DOMContentLoaded', function () {
    loadNotificationBadge();
    loadNotifications();

    // Also load when dropdown is opened
    document.getElementById('notificationDropdown')?.addEventListener('show.bs.dropdown', loadNotifications);

    // Refresh badge every 30 seconds
    setInterval(loadNotificationBadge, 30000);
});

async function loadNotificationBadge() {
    try {
        const response = await fetch('/api/NotificationApi/unread-count');
        if (response.ok) {
            const data = await response.json();
            const badge = document.getElementById('notificationBadge');
            if (badge) {
                if (data.count > 0) {
                    badge.textContent = data.count > 99 ? '99+' : data.count;
                    badge.classList.remove('d-none');
                } else {
                    badge.classList.add('d-none');
                }
            }
        }
    } catch (error) {
        console.error('Error loading notification badge:', error);
    }
}

async function loadNotifications() {
    const listContainer = document.getElementById('notificationList');
    if (!listContainer) return;

    try {
        const response = await fetch('/api/NotificationApi/recent');
        if (response.ok) {
            const data = await response.json();

            if (data.notifications && data.notifications.length > 0) {
                listContainer.innerHTML = data.notifications.map(function (n) {
                    const url = n.actionUrl || '/Notification';
                    const readClass = n.isRead ? '' : 'bg-light';
                    const newBadge = !n.isRead ? '<span class="badge bg-primary rounded-pill">New</span>' : '';

                    return `
                        <a href="#" class="dropdown-item notification-item py-2 ${readClass}" 
                           onclick="notificationClicked(${n.id}, '${escapeAttr(url)}'); return false;">
                            <div class="d-flex align-items-start">
                                <div class="notification-icon me-2 ${getNotificationColor(n.notificationType)}">
                                    <i class="bi ${getNotificationIcon(n.notificationType)}"></i>
                                </div>
                                <div class="flex-grow-1">
                                    <div class="fw-semibold small">${escapeHtml(n.title)}</div>
                                    <div class="text-muted small text-truncate" style="max-width: 250px;">${escapeHtml(n.message)}</div>
                                    <div class="text-muted small">${formatTimeAgo(n.createdAt)}</div>
                                </div>
                                ${newBadge}
                            </div>
                        </a>
                    `;
                }).join('');
            } else {
                listContainer.innerHTML = `
                    <div class="text-center text-muted p-3">
                        <i class="bi bi-bell-slash fs-4"></i>
                        <p class="mb-0 small">No notifications</p>
                    </div>
                `;
            }
        }
    } catch (error) {
        console.error('Error loading notifications:', error);
        if (listContainer) {
            listContainer.innerHTML = '<div class="text-center py-3 text-muted">Could not load notifications</div>';
        }
    }
}

// Click notification: mark as read, then navigate to actionUrl or Notification page
async function notificationClicked(notificationId, actionUrl) {
    try {
        await fetch(`/api/NotificationApi/${notificationId}/read`, { method: 'POST' });
    } catch (error) {
        console.error('Error marking notification as read:', error);
    }
    // Navigate to the notification's action URL or the Notifications page
    window.location.href = actionUrl || '/Notification';
}

async function markAsRead(notificationId) {
    try {
        await fetch(`/api/NotificationApi/${notificationId}/read`, { method: 'POST' });
        loadNotificationBadge();
        loadNotifications();
    } catch (error) {
        console.error('Error marking notification as read:', error);
    }
}

async function markAllAsRead() {
    try {
        await fetch('/api/NotificationApi/read-all', { method: 'POST' });
        loadNotificationBadge();
        loadNotifications();
    } catch (error) {
        console.error('Error marking all as read:', error);
    }
}

// Notification icon map — matches NotificationService type constants
function getNotificationIcon(type) {
    const icons = {
        'DocumentUploaded': 'bi-file-earmark-plus',
        'PendingReview': 'bi-hourglass-split',
        'StaffApproved': 'bi-check-circle',
        'StaffRejected': 'bi-x-circle',
        'AdminApproved': 'bi-shield-check',
        'AdminRejected': 'bi-shield-x',
        'DocumentVersioned': 'bi-arrow-repeat',
        'DocumentArchived': 'bi-archive',
        'FolderCreated': 'bi-folder-plus',
        'General': 'bi-info-circle'
    };
    return icons[type] || 'bi-bell';
}

// Notification color map — matches NotificationService type constants
function getNotificationColor(type) {
    const colors = {
        'DocumentUploaded': 'text-primary',
        'PendingReview': 'text-warning',
        'StaffApproved': 'text-success',
        'StaffRejected': 'text-danger',
        'AdminApproved': 'text-success',
        'AdminRejected': 'text-danger',
        'DocumentVersioned': 'text-info',
        'DocumentArchived': 'text-secondary',
        'FolderCreated': 'text-primary',
        'General': 'text-info'
    };
    return colors[type] || 'text-secondary';
}

function formatTimeAgo(dateString) {
    const date = new Date(dateString);
    const now = new Date();
    const seconds = Math.floor((now - date) / 1000);

    if (seconds < 60) return 'Just now';
    if (seconds < 3600) return Math.floor(seconds / 60) + 'm ago';
    if (seconds < 86400) return Math.floor(seconds / 3600) + 'h ago';
    if (seconds < 604800) return Math.floor(seconds / 86400) + 'd ago';
    return date.toLocaleDateString();
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function escapeAttr(text) {
    if (!text) return '';
    return text.replace(/'/g, "\\'").replace(/"/g, '&quot;');
}
