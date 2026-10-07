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
- What is already on the screen stays there. Nothing is sent: a page or action that needs the server says at once that it works again once you are back online, instead of waiting for a server that does not answer (a server that is down can leave a request hanging for a long time). Grids and the notification bell stop polling.
- The app asks the server again after 2, 5, 15 and 30 seconds, then once a minute, and at once when the device comes back online, when you return to the app, or when you press **Try again**. When it answers, everything on the screen is loaded again.

Saving, signing in and signing out need the server for now (signing out clears a cookie only the server can clear). Working offline, with changes kept on the device and sent when the server is back, is being built in steps.
