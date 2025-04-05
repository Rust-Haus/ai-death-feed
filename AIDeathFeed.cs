using Oxide.Core.Plugins;
using Rust;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Linq;
using System.Net;
using System.Text;

namespace Oxide.Plugins
{
    [Info("AI Death Feed", "Goo_", "1.0.0")]
    [Description("AI-generated kill commentary for Rust using RustGPT")]
    public class AIDeathFeed : RustPlugin
    {
        [PluginReference]
        private Plugin RustGPT;

        [PluginReference]
        private Plugin DeathNotes;

        private Dictionary<ulong, List<KillInfo>> _recentKills = new Dictionary<ulong, List<KillInfo>>();
        private Dictionary<ulong, Timer> _pendingCommentary = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, List<string>> _deathNotesMessages = new Dictionary<ulong, List<string>>();
        private Dictionary<ulong, Timer> _deathNotesTimers = new Dictionary<ulong, Timer>();
        private PluginConfig _config;
        private Timer _cleanupTimer;
        private string _pendingDiscordKillMessage = null;

        private class KillInfo
        {
            public string AttackerName { get; set; }
            public string VictimName { get; set; }
            public string WeaponName { get; set; }
            public string HitBone { get; set; }
            public float Distance { get; set; }
            public float Time { get; set; }
            public string OriginalVictimName { get; set; }
        }

        private class KillFeedStylingConfig
        {
            [JsonProperty("Commentary Prefix")]
            public string CommentaryPrefix { get; set; }

            [JsonProperty("Commentary Prefix Color")]
            public string CommentaryPrefixColor { get; set; }

            [JsonProperty("Commentary Message Color")]
            public string CommentaryMessageColor { get; set; }

            [JsonProperty("Commentary Font Size")]
            public int CommentaryFontSize { get; set; }

            [JsonProperty("Chat Icon (SteamID)")]
            public string ChatIcon { get; set; }

            public KillFeedStylingConfig()
            {
                CommentaryPrefix = "[AI Commentary]";
                CommentaryPrefixColor = "#de2400";
                CommentaryMessageColor = "#FFFFFF";
                CommentaryFontSize = 12;
                ChatIcon = "76561197970331299";
            }
        }

        private class CommentarySettingsConfig
        {
            [JsonProperty("System Prompt")]
            public string SystemPrompt { get; set; }

            [JsonProperty("Commentary Delay (seconds)")]
            public float CommentaryDelay { get; set; }

            [JsonProperty("Multikill Window (seconds)")]
            public float MultikillWindow { get; set; }

            [JsonProperty("Long Range Distance (meters)")]
            public float LongRangeDistance { get; set; }

            [JsonProperty("Medium Range Distance (meters)")]
            public float MediumRangeDistance { get; set; }

            [JsonProperty("Enable Multikill Detection")]
            public bool EnableMultikillDetection { get; set; }

            [JsonProperty("Enable Distance Reporting")]
            public bool EnableDistanceReporting { get; set; }

            [JsonProperty("Maximum Commentary Length")]
            public int MaxCommentaryLength { get; set; }

            [JsonProperty("Enable DeathNotes Integration")]
            public bool EnableDeathNotesIntegration { get; set; }

            public CommentarySettingsConfig()
            {
                SystemPrompt = "You are a color commentator for a brutal post-apocalyptic death match TV show called RUST. Your job is to provide brief, exciting, and entertaining commentary when players kill each other. Be creative, dramatic, and crude, like a mix between WWE and The Hunger Games commentary. If a player has a weird name with special characters, make fun of their name choice.";

                CommentaryDelay = 2.5f;
                MultikillWindow = 5f;
                LongRangeDistance = 100f;
                MediumRangeDistance = 20f;
                EnableMultikillDetection = true;
                EnableDistanceReporting = true;
                MaxCommentaryLength = -1;
                EnableDeathNotesIntegration = false;
            }
        }

        private class DiscordSettingsConfig
        {
            [JsonProperty("Enable Discord Integration")]
            public bool EnableDiscordIntegration { get; set; }

            [JsonProperty("Discord Webhook URL")]
            public string WebhookUrl { get; set; }

            public DiscordSettingsConfig()
            {
                EnableDiscordIntegration = false;
                WebhookUrl = "https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

            }
        }

        private class PermissionSettingsConfig
        {
            [JsonProperty("Permission to see kill commentary")]
            public string SeeCommentaryPermission { get; set; }

            public PermissionSettingsConfig()
            {
                SeeCommentaryPermission = "aideathfeed.see";
            }
        }

        private class PerformanceSettingsConfig
        {
            [JsonProperty("Maximum Pending Commentaries")]
            public int MaxPendingCommentaries { get; set; }

            [JsonProperty("Cleanup Interval (minutes)")]
            public int CleanupInterval { get; set; }

            public PerformanceSettingsConfig()
            {
                MaxPendingCommentaries = 10;
                CleanupInterval = 30;
            }
        }

        private class PluginConfig
        {
            public CommentarySettingsConfig CommentarySettings { get; set; }
            public DiscordSettingsConfig DiscordSettings { get; set; }
            public PermissionSettingsConfig PermissionSettings { get; set; }
            public PerformanceSettingsConfig PerformanceSettings { get; set; }
            public KillFeedStylingConfig KillFeedStyling { get; set; }

            [JsonProperty("Plugin Version")]
            public string PluginVersion { get; set; }

            public PluginConfig()
            {
                CommentarySettings = new CommentarySettingsConfig();
                DiscordSettings = new DiscordSettingsConfig();
                PermissionSettings = new PermissionSettingsConfig();
                PerformanceSettings = new PerformanceSettingsConfig();
                KillFeedStyling = new KillFeedStylingConfig();
                PluginVersion = "1.0.0";
            }
        }


        private bool HasWeirdCharacters(string name)
        {
            return name.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c != '_' && c != '-' && c != '.' && c != '[' && c != ']');
        }

        private void Init()
        {
            LoadConfig();
            permission.RegisterPermission(_config.PermissionSettings.SeeCommentaryPermission, this);
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private void OnServerInitialized()
        {
            if (RustGPT == null)
            {
                PrintError("RustGPT plugin not found! Kill commentary will not work.");
                return;
            }

            if (_config.CommentarySettings.EnableDeathNotesIntegration)
            {
                if (DeathNotes == null)
                {
                    PrintError("DeathNotes plugin not found! DeathNotes integration will not work.");
                }
            }

            if (_config.DiscordSettings.EnableDiscordIntegration)
            {
                if (string.IsNullOrEmpty(_config.DiscordSettings.WebhookUrl) || _config.DiscordSettings.WebhookUrl.Contains("your-webhook-url"))
                {
                    PrintError("Discord webhook URL not configured! Discord integration will not work.");
                }
            }

            _cleanupTimer = timer.Every(_config.PerformanceSettings.CleanupInterval * 60f, CleanupOldData);
        }

        private void Unload()
        {
            if (_cleanupTimer != null)
                _cleanupTimer.Destroy();

            foreach (var timer in _pendingCommentary.Values)
            {
                if (timer != null)
                    timer.Destroy();
            }

            foreach (var timer in _deathNotesTimers.Values)
            {
                if (timer != null)
                    timer.Destroy();
            }

            _pendingCommentary.Clear();
            _deathNotesTimers.Clear();
            _deathNotesMessages.Clear();
        }

        private void CleanupOldData()
        {
            var currentTime = Time.realtimeSinceStartup;

            foreach (var playerId in _recentKills.Keys.ToList())
            {
                _recentKills[playerId].RemoveAll(k => currentTime - k.Time > _config.CommentarySettings.MultikillWindow);
                if (_recentKills[playerId].Count == 0)
                    _recentKills.Remove(playerId);
            }

            foreach (var playerId in _pendingCommentary.Keys.ToList())
            {
                if (_pendingCommentary[playerId] == null)
                    continue;

                _pendingCommentary.Remove(playerId);
            }

            foreach (var playerId in _deathNotesTimers.Keys.ToList())
            {
                if (_deathNotesTimers[playerId] == null)
                {
                    _deathNotesTimers.Remove(playerId);
                    if (_deathNotesMessages.ContainsKey(playerId))
                        _deathNotesMessages.Remove(playerId);
                }
            }

            if (_pendingCommentary.Count > _config.PerformanceSettings.MaxPendingCommentaries)
            {
                var oldestKeys = _pendingCommentary.Keys.Take(_pendingCommentary.Count - _config.PerformanceSettings.MaxPendingCommentaries).ToList();

                foreach (var key in oldestKeys)
                {
                    if (_pendingCommentary.ContainsKey(key))
                    {
                        _pendingCommentary[key].Destroy();
                        _pendingCommentary.Remove(key);
                    }
                }
            }
        }

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (_config.CommentarySettings.EnableDeathNotesIntegration && DeathNotes != null)
                return;

            if (RustGPT == null || !(bool)RustGPT.Call("IsEnabled") || victim == null)
                return;

            BasePlayer attacker = info?.InitiatorPlayer;
            if (attacker == null || attacker == victim)
                return;

            try
            {
                string cleanVictimName = StripRichText(victim.displayName ?? "Unknown");
                bool hasWeirdName = HasWeirdCharacters(victim.displayName);

                var killInfo = new KillInfo
                {
                    AttackerName = StripRichText(attacker.displayName ?? "Unknown"),
                    VictimName = cleanVictimName,
                    WeaponName = GetWeaponDisplayName(attacker) ?? "unknown weapon",
                    HitBone = GetHitBone(info) ?? "body",
                    Distance = Vector3.Distance(attacker.transform.position, victim.transform.position),
                    Time = Time.realtimeSinceStartup,
                    OriginalVictimName = victim.displayName
                };

                if (!_recentKills.ContainsKey(attacker.userID))
                {
                    _recentKills[attacker.userID] = new List<KillInfo>();
                }

                _recentKills[attacker.userID].RemoveAll(k => Time.realtimeSinceStartup - k.Time > _config.CommentarySettings.MultikillWindow);
                _recentKills[attacker.userID].Add(killInfo);

                if (_pendingCommentary.ContainsKey(attacker.userID))
                {
                    _pendingCommentary[attacker.userID].Destroy();
                }

                _pendingCommentary[attacker.userID] = timer.Once(_config.CommentarySettings.CommentaryDelay, () =>
                {
                    ProcessKillStreak(attacker.userID);
                });
            }
            catch (Exception ex)
            {
                PrintError($"Error processing death event: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnDeathNotice(Dictionary<string, object> data, string message)
        {
            if (!_config.CommentarySettings.EnableDeathNotesIntegration || DeathNotes == null)
                return;

            if (RustGPT == null || !(bool)RustGPT.Call("IsEnabled"))
                return;

            try
            {
                var killerEntity = data["KillerEntity"] as BaseEntity;
                if (killerEntity == null)
                    return;

                var attacker = killerEntity as BasePlayer;
                if (attacker == null)
                    return;

                if (!_deathNotesMessages.ContainsKey(attacker.userID))
                {
                    _deathNotesMessages[attacker.userID] = new List<string>();
                }

                _deathNotesMessages[attacker.userID].Add(message);

                if (_deathNotesTimers.ContainsKey(attacker.userID))
                {
                    _deathNotesTimers[attacker.userID].Destroy();
                }

                _deathNotesTimers[attacker.userID] = timer.Once(_config.CommentarySettings.CommentaryDelay, () =>
                {
                    ProcessDeathNotesKills(attacker.userID);
                });
            }
            catch (Exception ex)
            {
                PrintError($"Error processing DeathNotes message: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void ProcessDeathNotesKills(ulong attackerId)
        {
            if (!_deathNotesMessages.ContainsKey(attackerId) || _deathNotesMessages[attackerId].Count == 0)
                return;

            var messages = _deathNotesMessages[attackerId];
            string prompt;
            string killMessage;

            if (messages.Count == 1)
            {
                prompt = messages[0];
                killMessage = messages[0];
            }
            else
            {
                var attacker = BasePlayer.FindByID(attackerId);
                string attackerName = attacker != null ? StripRichText(attacker.displayName) : "Unknown";

                prompt = $"{attackerName} just got a {messages.Count}x multikill! Here are the kills:\n";
                prompt += string.Join("\n", messages);
                prompt += "\nEpic kill streak commentary needed!";

                killMessage = $"{attackerName} got a {messages.Count}x multikill!";
            }

            if (_config.DiscordSettings.EnableDiscordIntegration)
            {
                _pendingDiscordKillMessage = killMessage;
            }

            SendDeathMessageToAI(prompt);

            _deathNotesMessages.Remove(attackerId);
            _deathNotesTimers.Remove(attackerId);
        }

        private void ProcessKillStreak(ulong attackerId)
        {
            if (!_recentKills.ContainsKey(attackerId))
                return;

            var kills = _recentKills[attackerId];
            if (kills.Count == 0)
                return;

            string prompt;
            string killMessage;

            if (kills.Count == 1)
            {
                var kill = kills[0];
                string distanceStr = "";

                if (_config.CommentarySettings.EnableDistanceReporting)
                {
                    distanceStr = kill.Distance > _config.CommentarySettings.LongRangeDistance ?
                        "long-range" :
                        kill.Distance > _config.CommentarySettings.MediumRangeDistance ?
                            "medium-range" :
                            "close-range";

                    distanceStr += $" ({Mathf.Round(kill.Distance)}m)";
                }

                prompt = $"{kill.AttackerName} killed {kill.VictimName} with a {kill.WeaponName} to the {kill.HitBone}";

                if (_config.CommentarySettings.EnableDistanceReporting)
                {
                    prompt += $" from {distanceStr}";
                }

                killMessage = $"{kill.AttackerName} killed {kill.VictimName} with a {kill.WeaponName}";
                if (_config.CommentarySettings.EnableDistanceReporting)
                {
                    killMessage += $" from {distanceStr}";
                }
            }
            else if (_config.CommentarySettings.EnableMultikillDetection)
            {
                var weapons = kills.Select(k => k.WeaponName).Distinct();
                var victims = kills.Select(k => k.VictimName);
                prompt = $"{kills[0].AttackerName} just got a {kills.Count}x multikill, taking down {string.Join(", ", victims)} using {string.Join(" and ", weapons)}!";
                prompt += " Epic kill streak commentary needed!";

                killMessage = $"{kills[0].AttackerName} got a {kills.Count}x multikill, taking down {string.Join(", ", victims)}";
            }
            else
            {
                var kill = kills[0];
                string distanceStr = "";

                if (_config.CommentarySettings.EnableDistanceReporting)
                {
                    distanceStr = kill.Distance > _config.CommentarySettings.LongRangeDistance ?
                        "long-range" :
                        kill.Distance > _config.CommentarySettings.MediumRangeDistance ?
                            "medium-range" :
                            "close-range";

                    distanceStr += $" ({Mathf.Round(kill.Distance)}m)";
                }

                prompt = $"{kill.AttackerName} killed {kill.VictimName} with a {kill.WeaponName} to the {kill.HitBone}";

                if (_config.CommentarySettings.EnableDistanceReporting)
                {
                    prompt += $" from {distanceStr}";
                }

                killMessage = $"{kill.AttackerName} killed {kill.VictimName} with a {kill.WeaponName}";
                if (_config.CommentarySettings.EnableDistanceReporting)
                {
                    killMessage += $" from {distanceStr}";
                }
            }

            if (_config.DiscordSettings.EnableDiscordIntegration)
            {
                _pendingDiscordKillMessage = killMessage;
            }

            SendDeathMessageToAI(prompt);

            _recentKills[attackerId].Clear();
            _pendingCommentary.Remove(attackerId);
        }

        private string StripRichText(string text)
        {
            return Regex.Replace(text, "<.*?>", string.Empty);
        }

        private string GetHitBone(HitInfo info)
        {
            try
            {
                if (info.HitEntity is not BaseCombatEntity combatEntity)
                    return "body";

                if (combatEntity.skeletonProperties == null)
                    return "body";

                return combatEntity.skeletonProperties.FindBone(info.HitBone)?.name?.english ?? "body";
            }
            catch (Exception ex)
            {
                PrintError($"Error getting hit bone: {ex.Message}");
                return "body";
            }
        }

        private string GetWeaponDisplayName(BasePlayer attacker)
        {
            var activeItem = attacker.GetActiveItem();
            if (activeItem == null)
            {
                return "unknown weapon";
            }
            return activeItem.info?.displayName?.translated ?? "unknown weapon";
        }

        private string StripStylingTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            text = Regex.Replace(text, "<color=#[0-9A-Fa-f]{6}>", "");
            text = text.Replace("</color>", "");

            text = Regex.Replace(text, "<size=\\d+>", "");
            text = text.Replace("</size>", "");

            return text;
        }

        private void SendDeathMessageToAI(string deathMessage)
        {
            string cleanMessage = StripStylingTags(deathMessage);
            string prompt = $"{cleanMessage}. Provide dramatic commentary!";

            RustGPT.Call("SendMessage", prompt, _config.CommentarySettings.SystemPrompt, new Action<JObject>(response =>
            {
                if (response == null)
                {
                    PrintError("Received null response from AI");
                    return;
                }

                try
                {
                    string commentary = null;

                    if (response["choices"] != null && response["choices"][0]["message"] != null)
                    {
                        commentary = response["choices"][0]["message"]["content"].ToString();
                    }
                    else if (response["content"] != null)
                    {
                        commentary = response["content"].ToString();
                    }
                    else if (response["text"] != null)
                    {
                        commentary = response["text"].ToString();
                    }

                    if (string.IsNullOrEmpty(commentary))
                    {
                        PrintError($"Could not parse AI response: {response}");
                        return;
                    }

                    commentary = commentary.Trim().Trim('"');

                    if (_config.CommentarySettings.MaxCommentaryLength > 0 && commentary.Length > _config.CommentarySettings.MaxCommentaryLength)
                    {
                        commentary = commentary.Substring(0, _config.CommentarySettings.MaxCommentaryLength) + "...";
                    }

                    BroadcastCommentary(commentary);

                    if (_config.DiscordSettings.EnableDiscordIntegration)
                    {
                        SendToDiscord(_pendingDiscordKillMessage, commentary);
                        _pendingDiscordKillMessage = null;
                    }
                }
                catch (Exception ex)
                {
                    PrintError($"Error processing kill commentary: {ex.Message}\nResponse: {response}");
                }
            }));
        }

        private void BroadcastCommentary(string commentary)
        {
            bool usePermissions = permission.PermissionExists(_config.PermissionSettings.SeeCommentaryPermission);

            if (usePermissions)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player == null) continue;

                    if (permission.UserHasPermission(player.UserIDString, _config.PermissionSettings.SeeCommentaryPermission))
                    {
                        Player.Reply(player, $"<size={_config.KillFeedStyling.CommentaryFontSize}><color={_config.KillFeedStyling.CommentaryPrefixColor}>{_config.KillFeedStyling.CommentaryPrefix}</color><color={_config.KillFeedStyling.CommentaryMessageColor}>{commentary}</color></size>", ulong.Parse(_config.KillFeedStyling.ChatIcon));
                    }
                }
            }
            else
            {
                Server.Broadcast($"<size={_config.KillFeedStyling.CommentaryFontSize}><color={_config.KillFeedStyling.CommentaryPrefixColor}>{_config.KillFeedStyling.CommentaryPrefix}</color><color={_config.KillFeedStyling.CommentaryMessageColor}>{commentary}</color></size>", ulong.Parse(_config.KillFeedStyling.ChatIcon));
            }
        }

        private void SendToDiscord(string killMessage, string commentary)
        {
            if (!_config.DiscordSettings.EnableDiscordIntegration || string.IsNullOrEmpty(_config.DiscordSettings.WebhookUrl))
                return;

            try
            {
                string message = "";

                if (!string.IsNullOrEmpty(killMessage))
                {
                    string cleanKillMessage = StripStylingTags(killMessage);
                    message += $"**Kill Feed**\n> {cleanKillMessage}";
                }

                if (!string.IsNullOrEmpty(commentary))
                {
                    if (!string.IsNullOrEmpty(message))
                        message += "\n\n";

                    string cleanCommentary = StripStylingTags(commentary);
                    message += $"**{_config.KillFeedStyling.CommentaryPrefix}**\n> {cleanCommentary}";
                }

                if (string.IsNullOrEmpty(message))
                    return;

                using (WebClient webClient = new WebClient())
                {
                    webClient.Headers[HttpRequestHeader.ContentType] = "application/json";
                    var payload = new { content = message };
                    var serializedPayload = JsonConvert.SerializeObject(payload);
                    webClient.UploadString(_config.DiscordSettings.WebhookUrl, "POST", serializedPayload);
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error sending message to Discord: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
