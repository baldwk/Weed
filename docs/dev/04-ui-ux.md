# UI and Interaction

> [Back to Developer Documentation](README.md)

## Design Direction

Weed follows a restrained launcher pattern: centered, fast, keyboard-first, and focused on the query and results. The interface supports system, light, and dark themes through shared resources.

Core principles:

- Keep the launcher visually quiet and information-dense.
- Preserve stable row, icon, and toolbar dimensions.
- Use clear selection, focus, and action feedback.
- Prefer short transitions that never delay input.
- Keep result rendering consistent across plugins.

## Launcher Window

The launcher has two responsive modes:

```text
Standard width
  Search input
  Result list

Preview width
  Search input
  Result list | Image or text preview
```

Behavior:

- `Alt+Space` shows and focuses the launcher.
- A repeated process launch activates the existing window.
- `Esc` hides the launcher.
- The launcher may hide on focus loss according to settings.
- Window height follows the bounded result count without leaving an unnecessary scrollbar for short lists.

## Search Input

- The search input receives focus whenever the launcher opens.
- Typing starts a cancellable query.
- `Ctrl+L` focuses and selects the complete query.
- Double-clicking the input selects all text.
- Placeholder, caret, and selection colors come from the active theme.

## Result Rows

Each result can show:

```text
Icon
Title
Subtitle
Action hint
```

Rules:

- Row height and icon bounds remain stable as content changes.
- Long titles and subtitles are clipped or ellipsized rather than resizing the window.
- The selected row is visible through more than color alone.
- Mouse movement updates selection only after the pointer actually moves, avoiding accidental selection when the window opens under the cursor.
- Left click executes the default action; right click exposes secondary actions.

## Keyboard Interaction

| Key | Behavior |
| --- | --- |
| `Enter` | Execute the selected result's default action |
| `Esc` | Hide the launcher |
| `Up` / `Down` | Move selection by one result |
| `PageUp` / `PageDown` | Move selection by five results |
| `Ctrl+L` | Focus and select the query |
| `Ctrl+number` | Execute the corresponding visible action |

The primary launcher flow must remain fully usable without a mouse.

## Preview Panel

The preview appears only when a result supplies image or detail text and expands the launcher to its preview width.

- Images maintain aspect ratio and stay within bounded dimensions.
- Text is read-only, selectable, and scrollable when necessary.
- Preview work must not block query input.
- Changing selection replaces preview content without shifting result-row geometry.
- Invalid or missing preview assets fall back to the normal result layout.

## Screenshots

The screenshot experience includes:

- A multi-monitor selection overlay.
- Region bounds and size feedback.
- A magnifier for precise edges.
- Primary-screen capture.
- Scrolling-area selection, progress, cancellation, and stitched output.
- A shared annotation surface after capture.

Annotation controls include pen, rectangle, ellipse, color, line width, undo, redo, clear, copy, and save. Controls use stable icon buttons and tooltips rather than large text buttons where a familiar icon exists.

The capture overlay must account for virtual desktop coordinates and per-monitor DPI. Cancelling at any point should restore the previous application state without leaving overlay windows behind.

## Settings

The settings window uses a sidebar with these top-level pages:

- **General:** Appearance, tray behavior, focus behavior, and launch at startup.
- **Hotkeys:** Main launcher shortcut and plugin shortcuts.
- **Updates:** Automatic checks, manifest URL, download, and status.
- **External Plugins:** Import, replace, remove, and open plugin directory.
- **Plugin pages:** Enablement, implicit priority, plugin-owned settings, permissions, dependencies, manifest details, and log tail.

Field type should match the data:

- Toggle for booleans.
- Numeric input for bounded integers.
- Select control for enumerated options.
- Path picker or path input for filesystem locations.
- Text or secret input for service URLs and credentials.

Saving should be immediate and predictable. Settings that require restart must say so where the change is made.

## Theme

Theme resources include concepts such as:

```text
Background
Surface
SurfaceElevated
TextPrimary
TextSecondary
Accent
Border
Selection
Danger
```

System mode follows Windows appearance changes. Light and dark selections apply immediately. Shared brushes must not be mutated after freezing; replace resources atomically when themes change.

## Accessibility

- Text and controls maintain practical contrast in every theme.
- Selection and error state are not communicated by color alone.
- Icon buttons have tooltips and accessible names.
- Focus order follows visual order.
- Text remains readable at Windows display scaling values.
- Launcher, preview, settings, and capture overlays work across mixed-DPI monitors.
- Dynamic text never overlaps adjacent controls.

## Interaction Testing

Test at minimum:

- Empty, one-result, short, and long result lists.
- Long titles, subtitles, paths, and unbroken words.
- Keyboard-only query, selection, action, and close flows.
- Mouse selection immediately after opening under the pointer.
- Light, dark, and live system-theme changes.
- Image and text previews.
- Multiple monitors with different DPI and negative virtual coordinates.
- Screenshot cancellation during selection, scrolling, editing, and saving.
