(function () {
  const status = document.getElementById('status');
  const button = document.getElementById('subscribe');
  function setStatus(message, error) { status.textContent = message; status.classList.toggle('error', !!error); }
  function decodeKey(value) {
    const base64 = (value + '='.repeat((4 - value.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
    return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
  }
  if (!window.isSecureContext || !('serviceWorker' in navigator) || !('PushManager' in window)) {
    setStatus('Push API unavailable. Use HTTPS, a supported browser, and on iOS install to Home Screen.', true);
    button.disabled = true;
    return;
  }
  button.addEventListener('click', async () => {
    button.disabled = true;
    try {
      if (Notification.permission === 'denied') throw new Error('Notifications are blocked in browser settings.');
      const permission = await Notification.requestPermission();
      if (permission !== 'granted') throw new Error('Notification permission not granted.');
      const configResponse = await fetch('api/pushdemo/config', { credentials: 'same-origin' });
      if (!configResponse.ok) throw new Error('Cannot load VAPID public key (HTTP ' + configResponse.status + '). Test api/pushdemo/ping and config directly.');
      const config = await configResponse.json();
      const registration = await navigator.serviceWorker.register('sw.js');
      let subscription = await registration.pushManager.getSubscription();
      if (subscription) {
        // If the server's VAPID key changed, old subscriptions must be removed first.
        const oldKey = subscription.options && subscription.options.applicationServerKey;
        const newKey = decodeKey(config.publicKey);
        const a = oldKey ? new Uint8Array(oldKey) : null;
        if (!a || a.length !== newKey.length || a.some((v, i) => v !== newKey[i])) {
          await subscription.unsubscribe(); subscription = null;
        }
      }
      if (!subscription) subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: decodeKey(config.publicKey) });
      const res = await fetch('api/pushdemo/subscribe', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(subscription.toJSON()), credentials: 'same-origin' });
      if (!res.ok) throw new Error('Server rejected subscription: ' + await res.text());
      const data = await res.json();
      setStatus('Subscribed successfully. Total registered devices: ' + data.deviceCount + '.');
    } catch (e) { setStatus(e.message || String(e), true); }
    finally { button.disabled = false; }
  });
})();
