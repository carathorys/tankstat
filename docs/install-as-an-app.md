# Install as an app

For people who want Tankstat on their home screen or dock, and operators who want to know what the service worker does.

Tankstat is a progressive web app: served over **HTTPS** (or from `localhost`), it can be installed and opened from the home screen or the dock like any app, full screen, with its own icon.

- **Android (Chrome, Edge, ...)**: the navigation menu shows **Install app** as soon as the browser allows it (Chromium decides when to make the offer; the browser's own menu has the same entry).
- **iPhone and iPad (Safari)**: Safari makes no offer from the page, so **Install app** in the menu shows the steps instead: open the page in Safari, then Share → *Add to Home Screen* → *Add* (other browsers on iOS cannot add to the Home Screen). The installed app has its own cookies: sign in once more there; it then stays signed in as long as it is used at least every 90 days (see [Sessions](authentication.md#sessions-standalone-and-oidc)).
- **Desktop (Chrome, Edge)**: the icon in the address bar or **Install app** in the menu. Firefox has no equivalent, and Safari on macOS adds the page to the Dock from its own menu (File → Add to Dock) without telling the page, so the menu entry is missing in both; that is normal.

## What the service worker does

A service worker keeps the built pages, scripts, styles and icons, so the app starts at once and still opens without the network (you then see the shell with the footer saying the server cannot be reached: data, pictures and sign-in need the server, see below). Nothing from `/graphql`, `/auth`, `/media` or `/imports` is ever cached, and those paths are never answered with `index.html` by the worker. Updates: the worker looks for a new version at every start, every hour while the app stays open, and when you come back to its tab. When there is one, a message "A new version of Tankstat is available" with a *Reload* button appears above the page; nothing reloads by itself, so you choose the moment (open dialogs and half-filled forms are not lost until you do). The server sends `/assets/*` (content-hashed names) as immutable and everything with a fixed name (`index.html`, `sw.js`, `manifest.webmanifest`, icons) as `no-cache`, so a release reaches every browser on its next start. The HTTPS it needs is the job of the reverse proxy in front of the server, see [Self-hosting](self-hosting.md).

## When the server is out of reach

The app does not rely on the browser's online flag alone: a server on your home network is out of reach from a phone on mobile data that is perfectly online. Every request reports how it went; one that gets no answer (or only a reverse proxy's 502, 503 or 504, which it sends while the server behind it is down) marks the server unreachable, and any answer marks it reachable again.

While it is unreachable:

- The top bar shows **Offline** and the footer says *You are offline* (no network at all) or *The server cannot be reached*, with a **Try again** button. A screen reader hears it once when it happens and *Back online* when it is over.
- What was downloaded or opened while online opens again, after a restart of the app too: the home page, a vehicle's page, its refuelling and expense lists in any order and page, an entry's details, the trash, the dashboard you opened, your notifications and settings. Figures (consumption, totals, a schedule's status) are the server's as of the download; nothing is worked out on the device. Something neither downloaded nor opened says it is not on this device yet, and screens that only make sense with the server (administration, the full vehicle list and the vehicles' trash, sharing, importing, signed-in devices) say they need a connection.
- Nothing is sent: a change says at once that it works again once you are back online, instead of waiting for a server that does not answer (a server that is down can leave a request hanging for a long time). Grids, the notification bell and the photo readings stop polling.
- The app asks the server again after 2, 5, 15 and 30 seconds, then once a minute, and at once when the device comes back online, when you return to the app, or when you press **Try again**. When it answers, everything on the screen is loaded again.

What the device keeps, per account, in the browser's storage for this site (IndexedDB; a database per account, so another account signing in on the same device never sees it):

- **Your vehicles and their recent logs, downloaded ahead.** Once the server says who you are, the app downloads your vehicles and, for each, the refuellings and expenses of your offline window (the last two months unless you choose otherwise; choosing comes with the next step), the trash included. Offline, a vehicle's refuelling and expense lists then page and sort like online, their entries open, and the trash shows. Who may edit an entry follows the vehicle's sharing as of the last download.
- **The answers of the screens you opened**, for everything else (the dashboard and its charts, settings, notifications); every download also keeps the home page and each vehicle's page and schedules. At most 300 such answers per account are kept, the oldest go first.

How much travels: the first download of a vehicle brings the logs of its window in pages of up to 200; every later one (at the start, when the server is back, every hour while the app is open) asks each vehicle only for what changed since the last one, usually nothing, and for what was removed for good meanwhile. A window that grows downloads that vehicle again; one that shrinks only removes the older logs from the device. A vehicle you no longer see (trashed, removed, or not shared with you any more) goes from the device with its logs. A device that has not downloaded for longer than the server remembers removals (`Sync:TombstoneRetentionDays`, 90 days) downloads its window afresh.

Signing out leaves the data on the device but closes it: the next start without the server opens nobody's, and it opens again when the same account signs in. Clearing the site's data in the browser removes it. A browser that offers no storage (some private windows) keeps nothing and works online only.

Saving, signing in and signing out need the server for now (signing out clears a cookie only the server can clear). Next steps: choosing the offline window (per vehicle too) on the Account page, and changes kept on the device and sent when the server is back.
