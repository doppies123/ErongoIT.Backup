# ErongoIT Backup - What's new

## 1.4.2 - 9 October 2026
- "Check for updates" now tells you clearly when you already have the latest version.
- The update installer now opens in front of your other windows instead of only on the taskbar.
- Fixed strange characters in this "What's new" list.

## 1.4.1 - 9 October 2026
- Test release for automatic updates.

## 1.4.0 - 9 October 2026
- Automatic updates: ErongoIT Backup checks for new versions every 6 hours and asks "Update to version x.y.z?". One click downloads the update and starts the installer; your PC stays registered and its backups continue.
- Settings > Updates shows the installed version, with a "Check for updates" button.
- ErongoIT Backup now starts in the system tray when you sign in to Windows.

## 1.3.1 - 7 October 2026
- Restore screen: the folder path (This PC > C: > ...) now sits on its own line and no longer squeezes the search and date controls.
- Search results show "Search results for ..." with a link back to the folder you were in.

## 1.3.0 - 7 October 2026
- New Restore screen, like CrashPlan:
  - Browse the PC's backed-up folders, tick any files or folders.
  - Name, Size and Date Modified columns.
  - Restore files as they were at any date and time ("As of"), or the most current version.
  - "Include deleted files" and search by file name.
  - Restore to the original location, Desktop, Downloads or any folder; choose to rename, overwrite or skip existing files. Restored files keep their original date modified.
- Much faster backups: only new and changed files are checked and sent. An unchanged backup takes seconds instead of minutes.
- The server keeps a history of every file version for the plan's retention period.
- "Back up now" asks the background service to run the backup, so it no longer blocks scheduled backups.

## 1.2.2 - 7 October 2026
- A backup interrupted by a restart or upgrade no longer blocks backups for 12 hours.
- History and Overview refresh automatically every 30 seconds while the window is open.

## 1.2.1 - 7 October 2026
- Restore and History lists are shown in pages; choose the lines per page under Settings > Display.

## 1.2.0 - 7 October 2026
- Low-impact backups: the backup service uses very low disk and CPU priority, limits its disk reads and slows down further while you are working.
- Scheduled backups wait when a laptop runs on battery below 30%.
- Folders to back up can be removed as well as added (Backup page); changes apply without restarting the service.

## 1.1.x - September 2026
- Installer wizard: sign in, choose company and backup plan, choose folders; installs the background service.
- Runs in the system tray; closing the window keeps backups running.
- Each PC signs in with its own device key.
- Incremental uploads, compression and de-duplication.
