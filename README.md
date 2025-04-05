<!-- image here ai-death-feed\screenshots\logo.png  -->

!(ai-death-feed/screenshots/logo.png)

AI Death Feed

A plugin for the game Rust

Compatible with Oxide and Carbon modding frameworks

A Rust plugin that provides AI-generated kill commentary using RustGPT, making your server's death feed more entertaining and engaging.

FEATURES

- AI-generated kill commentary using RustGPT
- Distance-based commentary (close, medium, long range)
- Multikill detection and special commentary
- Discord integration for kill feeds
- DeathNotes integration support
- Customizable styling and formatting
- Permission-based visibility control

DEPENDENCIES

- RustGPT plugin
- DeathNotes plugin (optional)

CONFIGURATION

Commentary Settings:
{
  "System Prompt": "You are a color commentator for a brutal post-apocalyptic death match TV show called RUST...",
  "Commentary Delay (seconds)": 2.5,
  "Multikill Window (seconds)": 5.0,
  "Long Range Distance (meters)": 100.0,
  "Medium Range Distance (meters)": 20.0,
  "Enable Multikill Detection": true,
  "Enable Distance Reporting": true,
  "Maximum Commentary Length": -1,
  "Enable DeathNotes Integration": false
}

Discord Settings:
{
  "Enable Discord Integration": false,
  "Discord Webhook URL": "your-webhook-url"
}

Kill Feed Styling:
{
  "Commentary Prefix": "[AI Commentary]",
  "Commentary Prefix Color": "#de2400",
  "Commentary Message Color": "#FFFFFF",
  "Commentary Font Size": 12,
  "Chat Icon (SteamID)": "76561197970331299"
}

PERMISSIONS

- aideathfeed.see - Allows players to see kill commentary

INSTALLATION

1. Install the RustGPT plugin
2. Copy AIDeathFeed.cs to your oxide/plugins directory
3. Configure the plugin in oxide/config/AIDeathFeed.json
4. Restart your server or use oxide.reload AIDeathFeed

USAGE

The plugin automatically generates commentary for:
- Single kills with weapon and distance info
- Multikills within the configured time window
- DeathNotes events (if enabled)

PERFORMANCE

- Configurable cleanup intervals
- Maximum pending commentaries limit
- Automatic cleanup of old data

SUPPORT

https://discord.gg/EQNPBxdjRu