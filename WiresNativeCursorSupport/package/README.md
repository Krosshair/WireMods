# Wire's Native Cursor

Uses the operating system's actual cursor instead of Valheim's custom pointer image. The native operating system draws the cursor. Cursor states change on UI events, not through per-frame updates.

Native backends:
- Windows and Proton/Wine: Windows system cursor handles using the game's window messages.
- macOS: AppKit NSCursor objects.
- Native Linux/X11: Xcursor theme handles assigned to Valheim's X11 window.
- Native Wayland without XWayland is not supported by this backend.

Cursor states include arrow, clickable hand, text, dragging, working and waiting. The accessibility settings toggle returns to Valheim's default cursor when disabled.

Custom system theme, size and animation are delegated to the OS or the Wine/Proton cursor layer, rather than copying and resizing images. Windows cursor scaling has not yet been tested. macOS, native X11 and Proton/Wayland behavior need confirmation from users.

This mod is AI-assisted.
