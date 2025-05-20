// THYNK Service Worker for offline capabilities
const CACHE_NAME = 'thynk-cache-v2';
const RESOURCES_CACHE_NAME = 'thynk-resources-v2';

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
  '/Community/Dashboard'
];

// Form submission URLs that should NOT be cached
const formUrls = [
  '/LGU/CreateAlert',
  '/LGU/EditAlert',
  '/LGU/DeleteAlert',
  '/Account/Login',
  '/Account/Register',
  '/Account/Logout'
];

// Install event - cache basic assets
self.addEventListener('install', event => {
  self.skipWaiting();
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => {
        console.log('Opened cache');
        // Use individual promises instead of addAll to handle failures more gracefully
        const cachePromises = urlsToCache.map(url => {
          // Fetch each resource and add to cache, ignoring failures
          return fetch(url)
            .then(response => {
              if (response.ok) {
                return cache.put(url, response);
              }
              console.log(`Failed to cache: ${url}, status: ${response.status}`);
              return Promise.resolve(); // Continue despite failure
            })
            .catch(error => {
              console.error(`Error caching ${url}:`, error);
              return Promise.resolve(); // Continue despite error
            });
        });
        
        return Promise.all(cachePromises);
      })
  );
});

// Activate event - cleanup old caches
self.addEventListener('activate', event => {
  self.clients.claim(); // Take control of all clients
  
  event.waitUntil(
    caches.keys().then(cacheNames => {
      return Promise.all(
        cacheNames.filter(cacheName => {
          // Delete any old versions of our caches
          return (cacheName.startsWith('thynk-cache-') && cacheName !== CACHE_NAME) || 
                 (cacheName.startsWith('thynk-resources-') && cacheName !== RESOURCES_CACHE_NAME);
        }).map(cacheName => {
          console.log('Deleting old cache:', cacheName);
          return caches.delete(cacheName);
        })
      );
    })
  );
});

// Helper function to check if a URL is in the form URLs list
function isFormUrl(url) {
  return formUrls.some(formUrl => url.includes(formUrl));
}

// Fetch event - serve from cache or network
self.addEventListener('fetch', event => {
  // Extract URL string for easier checking
  const url = event.request.url;
  const method = event.request.method;
  
  // Skip cross-origin requests except for CDN resources
  if (!url.startsWith(self.location.origin) && 
      !url.includes('cdnjs.cloudflare.com') &&
      !url.includes('api.mapbox.com')) {
    return; // Let browser handle it normally
  }

  // ALWAYS bypass the service worker for form submissions and non-GET requests
  if (method !== 'GET' || isFormUrl(url)) {
    console.log(`Bypassing service worker for: ${method} ${url}`);
    return; // Let browser handle it normally
  }

  // For educational resources
  if (url.includes('/Community/ResourceDetails/') || 
      url.includes('/uploads/resources/')) {
    event.respondWith(
      caches.open(RESOURCES_CACHE_NAME).then(cache => {
        return cache.match(event.request).then(response => {
          if (response) {
            return response; // Return cached response if available
          }
          
          // Otherwise fetch from network
          return fetch(event.request).then(networkResponse => {
            // Only cache complete successful responses
            if (networkResponse.ok && networkResponse.status !== 206 && method === 'GET') {
              // Clone the response so we can both cache it and return it
              cache.put(event.request, networkResponse.clone());
            }
            return networkResponse;
          }).catch(error => {
            console.error('Fetch failed:', error);
            throw error; // Re-throw to allow error to propagate
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
        // Don't cache non-success responses
        if (!response.ok) {
          return response;
        }
        
        // Don't cache partial responses
        if (response.status === 206) {
          return response;
        }
        
        // Don't cache non-GET requests
        if (method !== 'GET') {
          return response;
        }
        
        // Clone the response as it can only be consumed once
        const responseToCache = response.clone();
        
        // Cache successful GET responses for same-origin requests
        if (url.startsWith(self.location.origin)) {
          caches.open(CACHE_NAME)
            .then(cache => {
              cache.put(event.request, responseToCache)
                .catch(error => console.error('Cache put error:', error));
            })
            .catch(error => console.error('Cache open error:', error));
        }
        
        return response;
      })
      .catch(() => {
        // Network failed, try the cache
        return caches.match(event.request)
          .then(cachedResponse => {
            if (cachedResponse) {
              return cachedResponse;
            }
            // If not in cache either, show a generic offline page for HTML requests
            if (event.request.headers.get('Accept').includes('text/html')) {
              return caches.match('/offline.html')
                .catch(() => new Response('You are offline and this page is not cached.', 
                  { headers: { 'Content-Type': 'text/html' }}));
            }
            // Otherwise, return a basic error response
            return new Response('Network error occurred', 
              { status: 503, statusText: 'Service Unavailable' });
          });
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
            if (response.ok && response.status !== 206) {
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