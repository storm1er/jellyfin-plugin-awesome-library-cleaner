# Awesome Library Cleaner (ALC)

A Jellyfin plugin to automatically manage and clean your media libraries based on configurable rules.

- [Requirements](#requirements)
- [Features](#features)
- [Configuration](#configuration)
  - [Library Settings](#library-settings)
  - [Accessing Configuration](#accessing-configuration)
- [How It Works](#how-it-works)
  - [Favorites Protection](#favorites-protection)
    - [Protection Levels](#protection-levels)
  - [Leaving Soon Collections](#leaving-soon-collections)
  - [Deletion Process](#deletion-process)
  - [Scheduled Task](#scheduled-task)
- [Library Cleanup Page](#library-cleanup-page)
- [Installation](#installation)
- [Best Practices](#best-practices)
- [License](#license)
- [Contributing](#contributing)
- [Disclaimer](#disclaimer)

## Requirements

- **Jellyfin Version**: 10.11.2 or later

## Features

- **Per-Library Configuration**: Configure cleanup rules individually for each movie or TV show library
- **Leaving Soon Collections**: Create "leaving soon" collections to notify users before media will be removed
- **Flexible Time References**: Choose between file added date, file update date, or latest watch date for cleanup calculations
- **Automatic or Manual Deletion**: Enable automatic deletion or review items manually before removal
- **Favorites Protection**: Optionally exclude user favorites from cleanup
- **TV Show Granularity**: For TV shows, choose to clean per episode, season, or whole series
- **Admin Cleanup Interface**: Web interface to review and manually delete pending items

## Configuration

### Library Settings

For each library, you can configure:

1. **Enable/Disable**: Toggle Awesome Library Cleaner for this library
2. **Time Reference**: Choose which date to use for age calculations:
   - File Added Date
   - Latest File Update Date
   - Latest Watch Date
3. **Leaving Soon After (Days)**: Number of days before media appears in "leaving soon" collection (0 to disable)
4. **Leaving Soon Collection Name**: Display name for the leaving soon collection
5. **Delete After (Days)**: Number of days before media is deleted (must be >= leaving soon days)
6. **Delete Automation**: If enabled, media is automatically deleted; if disabled, items are added to a "To Delete" collection for manual review
7. **Exclude Favorites**: Prevent user favorites from being cleaned or added to leaving soon collection
8. **Element Reference** (TV Shows only): Choose cleanup granularity:
   - Per Episode
   - Per Season
   - Whole Series

### Accessing Configuration

1. Go to **Dashboard** → **Plugins** → **Awesome Library Cleaner** → **Settings**
2. Configure settings for each library
3. Click **Save**

## How It Works

### Favorites Protection

When "Exclude Favorites" is enabled, the plugin protects favorited content from deletion. For Movies, it's easy, not so for TV Show: the protection behavior depends on the **Element Reference** setting as follows.

#### Protection Levels

| Element Reference | User Favorites Episode 5 - What Gets Protected |
|-------------------|----------------------------------------------|
| **Whole Series** | **Entire series** - All seasons and all episodes are kept |
| **Per Season** | **Only that season** - Only the season containing Episode 5 is kept, other seasons are deleted |
| **Per Episode** | **Only that episode** - Only Episode 5 is kept, other episodes are deleted |

**Important Notes:**
- If ANY user favorites content, it's protected from deletion
- For TV shows with "Whole Series" mode: Even if only one episode is favorited, the entire series is safe
- For TV shows with "Per Season" mode: A favorited episode protects its entire season, but not other seasons
- For TV shows with "Per Episode" mode: Only the specifically favorited episodes are protected

**Examples:**

1. **User favorites "S05E14"**
   - Whole Series mode: Entire series is protected ✅
   - Per Season mode: Only Season 5 is protected, Seasons 1-4 may be deleted ⚠️
   - Per Episode mode: Only S05E14 is protected, all other episodes may be deleted ⚠️

2. **User favorites the series itself**
   - All modes: Entire series is protected ✅

### Leaving Soon Collections

When a media item reaches the "leaving soon" threshold:
1. The item is added to a collection named `[Library Name] - [Settings Leaving Soon Collection Name]`
2. This collection appears in Jellyfin's Collections section
3. Users can see what content is about to be removed
4. If a user favorites an item from the leaving soon collection, it will be protected from deletion (if "Exclude Favorites" is enabled)
5. **Note**: Favorites are checked BEFORE items go into leaving soon, so favorited content won't appear there if "Exclude Favorites" is enabled

### Deletion Process

When delete automation is **enabled**:
- Media files are automatically deleted when they reach the deletion threshold

**Warning**: Deletion is permanent and will remove the original media files!

When delete automation is **disabled**:
- Items are added to a collection named `[Library Name] - To Delete`
- Users can see the collection, it behaves like any leaving soon collection
- Admins can review these items in the **Library Cleanup** page
- Admins can selectively delete items or let them remain

### Scheduled Task

The plugin runs as a scheduled task (default: daily at 3 AM). You can:
- Manually triggger or update schedule from **Dashboard** → **Scheduled Tasks** → **Awesome Library Cleaner**
- Manually trigger from **Dashboard** → **Plugins** → **Awesome Library Cleaner** → **Settings** → **Run Cleanup Task**

## Library Cleanup Page

Access the cleanup interface at **Dashboard** → **Plugins** → **Awesome Library Cleaner** → **Settings** → **Library Cleanup** to:
- View all media pending deletion (when delete automation is disabled)
- Review items grouped by library
- Select and delete multiple items at once
- Delete individual items

**Warning**: Deletion is permanent and will remove the original media files!

## Installation

- **Dashboard** → **Plugins** → **Manage Repositories** → **New Repository**
  - Repository Name: ALC
  - Repository URL: `https://raw.githubusercontent.com/storm1er/jellyfin-plugin-awesome-library-cleaner/refs/heads/main/manifest.json`
- **Dashboard** → **Plugins** → **All** → **Awesome Library Cleaner** → **Install**
- **Dashboard** → **Restart**
- **Dashboard** → **Plugins** → **Awesome Library Cleaner** → **Settings**

## Best Practices

1. **Start Conservative**: Begin with longer time periods and manual deletion mode
2. **Test First**: Enable on a single test library before rolling out to all libraries
3. **Choose Time Reference Carefully**:
   - Use **"File Added Date"** if you want to check from when the media was added
   - Use **"Latest File Update Date"** if you want to check from when the media was updated
   - Use **"Latest Watch Date"** if you want to check from when the media was watched last
4. **Choose Element Reference Carefully**:
   - Use **"Whole Series"** if you want maximum protection for favorited shows (any favorited episode keeps the entire series)
   - Use **"Per Season"** for balanced cleanup (favorited episode keeps its season)
   - Use **"Per Episode"** only if you want aggressive cleanup (only favorited episodes are kept)
5. **Enable "Exclude Favorites"**: Always recommended to allow users to keep what they love
6. **Monitor Logs**: Check Jellyfin logs for any issues during cleanup operations
7. **Backup Important Media**: Always maintain backups of irreplaceable content
8. **User Communication**:
   - Inform users about leaving soon collections so they can see what's being cleaned
   - Explain the favorites protection behavior based on your Element Reference setting
   - Encourage users to favorite at the Series level for TV shows if you're using "Per Season" or "Per Episode" modes

## License

This project is licensed under the GPL-3.0 License - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## Disclaimer

**Use at your own risk!** This plugin deletes files from your system. Always maintain backups of important media.
The authors are not responsible for any data loss.
