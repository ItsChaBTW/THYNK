// THYNK Service Worker for offline capabilities
const CACHE_NAME = 'thynk-cache-v1';
const RESOURCES_CACHE_NAME = 'thynk-resources-v1';

// URLs to cache immediately
const urlsToCache = [
  '/',
  '/css/site.css',
  '/css/community.css',
  '/js/site.js',
  '/lib/bootstrap/dist/css/bootstrap.min.css',
  '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
  '/lib/jquery/dist/jquery.min.js',
  'https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.1/css/all.min.css',
  '/Community/Dashboard',
  '/Community/EducationalResources'
];

// Install event - cache basic assets
self.addEventListener('install', event => {
  self.skipWaiting();
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => {
        console.log('Opened cache');
        return cache.addAll(urlsToCache);
      })
  );
});

// Activate event - cleanup old caches
self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(cacheNames => {
      return Promise.all(
        cacheNames.filter(cacheName => {
          return cacheName !== CACHE_NAME && cacheName !== RESOURCES_CACHE_NAME;
        }).map(cacheName => {
          return caches.delete(cacheName);
        })
      );
    })
  );
});

// Fetch event - serve from cache or network
self.addEventListener('fetch', event => {
  // Skip cross-origin requests
  if (!event.request.url.startsWith(self.location.origin) && 
      !event.request.url.includes('cdnjs.cloudflare.com') &&
      !event.request.url.includes('api.mapbox.com')) {
    return;
  }

  // For educational resources
  if (event.request.url.includes('/Community/ResourceDetails/') || 
      event.request.url.includes('/uploads/resources/')) {
    event.respondWith(
      caches.open(RESOURCES_CACHE_NAME).then(cache => {
        return cache.match(event.request).then(response => {
          return response || fetch(event.request).then(networkResponse => {
            cache.put(event.request, networkResponse.clone());
            return networkResponse;
          });
        });
      })
    );
    return;
  }

  // Standard cache strategy - network first, fallback to cache
  event.respondWith(
    fetch(event.request)
      .then(response => {
        // Clone the response as it can only be consumed once
        const responseToCache = response.clone();
        
        // Only cache successful responses for same-origin requests
        if (response.ok && event.request.url.startsWith(self.location.origin)) {
          caches.open(CACHE_NAME)
            .then(cache => {
              cache.put(event.request, responseToCache);
            });
        }
        
        return response;
      })
      .catch(() => {
        // Network failed, try the cache
        return caches.match(event.request);
      })
  );
});

// Listen for messages from the main thread
self.addEventListener('message', event => {
  if (event.data && event.data.action === 'cacheResource') {
    const resourceUrl = event.data.url;
    
    if (resourceUrl) {
      caches.open(RESOURCES_CACHE_NAME).then(cache => {
        fetch(resourceUrl)
          .then(response => {
            if (response.ok) {
              cache.put(resourceUrl, response);
              console.log('Resource cached for offline use:', resourceUrl);
            }
          })
          .catch(error => {
            console.error('Failed to cache resource:', error);
          });
      });
    }
  }
});

// Background sync for pending submissions when offline
self.addEventListener('sync', event => {
  if (event.tag === 'sync-reports') {
    event.waitUntil(syncReports());
  } else if (event.tag === 'sync-updates') {
    event.waitUntil(syncUpdates());
  }
});

// Function to sync pending reports
function syncReports() {
  return idbKeyval.get('pendingReports')
    .then(pendingReports => {
      if (!pendingReports || !pendingReports.length) return;
      
      return Promise.all(pendingReports.map(report => {
        return fetch('/api/reports', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json'
          },
          body: JSON.stringify(report)
        })
        .then(response => {
          if (response.ok) {
            // Remove from pending queue
            return idbKeyval.get('pendingReports')
              .then(currentReports => {
                const updatedReports = currentReports.filter(r => r.tempId !== report.tempId);
                return idbKeyval.set('pendingReports', updatedReports);
              });
          }
        });
      }));
    });
}

// Function to sync pending community updates
function syncUpdates() {
  return idbKeyval.get('pendingUpdates')
    .then(pendingUpdates => {
      if (!pendingUpdates || !pendingUpdates.length) return;
      
      return Promise.all(pendingUpdates.map(update => {
        return fetch('/api/updates', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json'
          },
          body: JSON.stringify(update)
        })
        .then(response => {
          if (response.ok) {
            // Remove from pending queue
            return idbKeyval.get('pendingUpdates')
              .then(currentUpdates => {
                const updatedQueue = currentUpdates.filter(u => u.tempId !== update.tempId);
                return idbKeyval.set('pendingUpdates', updatedQueue);
              });
          }
        });
      }));
    });
}

// Simple implementation of IndexedDB key-value store for offline data
const idbKeyval = (() => {
  const dbName = 'thynk-offline-db';
  const storeName = 'keyval-store';
  let dbPromise;

  const getDB = () => {
    if (!dbPromise) {
      dbPromise = new Promise((resolve, reject) => {
        const openReq = indexedDB.open(dbName, 1);
        openReq.onerror = () => reject(openReq.error);
        openReq.onsuccess = () => resolve(openReq.result);
        openReq.onupgradeneeded = () => {
          openReq.result.createObjectStore(storeName);
        };
      });
    }
    return dbPromise;
  };

  return {
    get(key) {
      return getDB().then(db => {
        return new Promise((resolve, reject) => {
          const transaction = db.transaction(storeName, 'readonly');
          const store = transaction.objectStore(storeName);
          const req = store.get(key);
          transaction.oncomplete = () => resolve(req.result);
          transaction.onabort = transaction.onerror = () => reject(transaction.error);
        });
      });
    },
    set(key, value) {
      return getDB().then(db => {
        return new Promise((resolve, reject) => {
          const transaction = db.transaction(storeName, 'readwrite');
          const store = transaction.objectStore(storeName);
          store.put(value, key);
          transaction.oncomplete = () => resolve();
          transaction.onabort = transaction.onerror = () => reject(transaction.error);
        });
      });
    },
    delete(key) {
      return getDB().then(db => {
        return new Promise((resolve, reject) => {
          const transaction = db.transaction(storeName, 'readwrite');
          const store = transaction.objectStore(storeName);
          store.delete(key);
          transaction.oncomplete = () => resolve();
          transaction.onabort = transaction.onerror = () => reject(transaction.error);
        });
      });
    }
  };
})(); 