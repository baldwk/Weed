# Weed Specification Index

Weed is an Alfred-style launcher and workflow tool for Windows. These documents describe the MVP product scope,
system design, plugin model, and development roadmap.

## Documents

- [00-overview.md](00-overview.md): product positioning, MVP scope, design principles, and terminology.
- [01-system-architecture.md](01-system-architecture.md): Host, Core, PluginHost, platform layer, and first-party plugin responsibilities.
- [02-plugin-system.md](02-plugin-system.md): managed DLL plugin contract, manifest, lifecycle, permissions, packaging, and compatibility.
- [03-query-routing-hotkeys.md](03-query-routing-hotkeys.md): Keyword, Hotkey, ImplicitQuery, ranking, usage history, and shortcut settings.
- [04-ui-ux.md](04-ui-ux.md): launcher UI, plugin panels, settings, keyboard interaction, and theme rules.
- [05-first-party-plugins.md](05-first-party-plugins.md): first-party plugin specs and extension plugin specs.
- [06-data-storage.md](06-data-storage.md): settings, history, indexes, clipboard objects, and plugin data storage.
- [07-roadmap.md](07-roadmap.md): implementation phases, acceptance checks, and release preparation.
- [08-external-plugins.md](08-external-plugins.md): external plugin development, packaging, import design, and OCR plugin example.

## Status

These documents are the MVP-stage product and technical specs. When APIs, data structures, or interactions change,
update the corresponding spec in the same change.
