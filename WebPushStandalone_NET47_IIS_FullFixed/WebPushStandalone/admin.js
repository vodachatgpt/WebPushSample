(function () {
  const button = document.getElementById('send');
  const result = document.getElementById('result');
  button.addEventListener('click', async () => {
    const key = document.getElementById('key').value;
    const title = document.getElementById('title').value.trim();
    const message = document.getElementById('message').value.trim();
    if (!key || !title || !message) { result.textContent = 'Enter the admin key, title, and message.'; return; }
    button.disabled = true;
    result.textContent = 'Sending to registered devices…';
    try {
      const res = await fetch('api/pushdemo/send', { method:'POST', headers:{'Content-Type':'application/json','X-Demo-Admin-Key':key}, body:JSON.stringify({ title, message }), cache:'no-store' });
      if (!res.ok) throw new Error('API HTTP ' + res.status + ': ' + await res.text());
      const data = await res.json();
      result.textContent = `Registered: ${data.subscriptions} | Accepted: ${data.accepted} | Failed: ${data.failed} | Removed expired: ${data.removedExpired}. ${data.note}` + (data.errors.length ? ' Errors: ' + data.errors.join(' ; ') : '');
    } catch (e) { result.textContent = e.message || String(e); }
    finally { button.disabled = false; }
  });
})();
