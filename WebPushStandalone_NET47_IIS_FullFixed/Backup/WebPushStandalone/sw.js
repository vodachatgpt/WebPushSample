self.addEventListener('push', event => {
  let data = {};
  try { if (event.data) data = event.data.json(); } catch (e) { /* fallback */ }
  const target = new URL('index.html', self.registration.scope).href;
  event.waitUntil(self.registration.showNotification(data.title || 'New notification', {
    body: data.body || 'You have a new message.',
    tag: String(data.id || Date.now()),
    data: { url: target }
  }));
});
self.addEventListener('notificationclick', event => {
  event.notification.close();
  const url = new URL('index.html', self.registration.scope).href;
  event.waitUntil((async () => {
    const windows = await clients.matchAll({ type: 'window', includeUncontrolled:true });
    for (const client of windows) { if (client.url === url && 'focus' in client) return client.focus(); }
    return clients.openWindow(url);
  })());
});
