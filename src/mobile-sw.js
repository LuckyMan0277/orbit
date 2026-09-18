const CACHE='orbit-remote-v9';
const shell=['/mobile.html','/mobile.js','/mobile.css','/mobile.webmanifest','/orbit.svg','/orbit-180.png','/orbit-192.png','/orbit-512.png'];
const allowed=path=>shell.includes(path)||(/^\/chunks\/[A-Za-z0-9_-]+\.(js|css)$/).test(path);
self.addEventListener('install',event=>event.waitUntil(caches.open(CACHE).then(cache=>cache.addAll(shell)).then(()=>self.skipWaiting())));
self.addEventListener('activate',event=>event.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(key=>key!==CACHE).map(key=>caches.delete(key)))).then(()=>self.clients.claim())));
self.addEventListener('fetch',event=>{const url=new URL(event.request.url);if(url.origin!==location.origin||url.pathname.startsWith('/api/')||event.request.method!=='GET')return;if(!allowed(url.pathname)){if(event.request.mode==='navigate')event.respondWith(caches.match('/mobile.html'));return;}event.respondWith(fetch(event.request).then(response=>{if(response.ok)caches.open(CACHE).then(cache=>cache.put(event.request,response.clone()));return response;}).catch(()=>caches.match(event.request).then(hit=>{if(hit)return hit;throw new Error('offline asset unavailable');})));});
