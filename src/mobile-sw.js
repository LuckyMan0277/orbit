const CACHE='orbit-remote-v13';
const shell=['/mobile.html','/mobile.js','/mobile.css','/orbit.svg','/orbit-180.png','/orbit-192.png','/orbit-512.png'];
const allowed=path=>shell.includes(path)||(/^\/chunks\/[A-Za-z0-9_-]+\.(js|css)$/).test(path);
// A cellular hiccup (e.g. right after the Camera app hands off to Safari for a
// scanned QR) can leave this fetch pending forever with no timeout of its own,
// stranding the page on its static pre-JS markup. Bound it and fall back to cache.
function fetchWithTimeout(request,ms){const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),ms);return fetch(request,{signal:controller.signal}).finally(()=>clearTimeout(timer));}
self.addEventListener('install',event=>event.waitUntil(caches.open(CACHE).then(cache=>cache.addAll(shell)).then(()=>self.skipWaiting())));
self.addEventListener('activate',event=>event.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(key=>key!==CACHE).map(key=>caches.delete(key)))).then(()=>self.clients.claim())));
self.addEventListener('fetch',event=>{const url=new URL(event.request.url);if(url.origin!==location.origin||url.pathname.startsWith('/api/')||event.request.method!=='GET')return;if(!allowed(url.pathname)){if(event.request.mode==='navigate')event.respondWith(caches.match('/mobile.html'));return;}event.respondWith(fetchWithTimeout(event.request,8000).then(response=>{if(response.ok)caches.open(CACHE).then(cache=>cache.put(url.pathname,response.clone()));return response;}).catch(()=>caches.match(url.pathname).then(hit=>{if(hit)return hit;throw new Error('offline asset unavailable');})));});
