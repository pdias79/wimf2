# PR #121 — idle unload and persistent local network catalog copies

## Configuration

Optional section (defaults shown):

```json
{
  "catalogMemory": {
    "idleUnloadEnabled": true,
    "idleTimeoutMinutes": 15,
    "reloadOnSearchInput": true
  }
}
```

Catalogs load at daemon start. Editing the search field (including clearing it) updates the shared idle clock and, if previously unloaded, schedules a single asynchronous reload. Other edits do not change the UI. Search requests await the reload and retain a protected snapshot until the request completes. Only when a submitted search discovers that catalog data are not ready does the web page open a modal; it closes after the response is rendered or on error.

On 15 minutes of inactivity, the daemon drops the references held by its store. The background refresh loop must not repopulate unloaded catalog objects. Files on disk remain untouched and .NET determines when memory is returned to the OS.

## Local catalog cache

The existing `CatalogCache` implementation already persists source files in `cacheDirectory` using a SHA-256-derived filename. This PR reuses that implementation for catalog entries explicitly identified as Windows UNC paths (or paths beginning with `//`). For these paths, when `cacheDirectory` is not configured, the default is `LocalApplicationData/Wimf2/CatalogCache`. Cache copies survive application exits and RAM unloading. The source stamp (length + UTC modification time) determines when copying is necessary; replacements are validated and atomic, with last known good copies preserved on network errors.

Non-UNC filesystem mounts and mapped network drive letters are not automatically identified as network sources. Configure those under `sources` and set `cacheDirectory` to ensure local caching.

## Validation before merge

- Windows and Ubuntu automated tests
- Two browser tabs typing concurrently, with one reload
- Search request held while idle expiry is pending, and cancel/error handling
- Background refresh while unloaded
- Network source modified and unavailable, old valid local copy remains
- Cold start / warm start (cached) and first search latency
- Test that memory diagnostics report reduced managed retention when unloaded

**Do not merge until real Windows/network tests and the concurrency checks have been completed.**
