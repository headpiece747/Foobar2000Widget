// BeefwebApiModels.cs
using System.Collections.Generic;
using System.Text.Json.Serialization; // Required for JSON attributes

namespace Foobar2000Widget
{
    // This file contains C# classes that mirror the relevant JSON schemas
    // from the Beefweb Player API documentation (player-api.txt).
    // These are used for deserializing API responses.

    // Represents the overall response from the /player endpoint
    public class GetPlayerStateResponse
    {
        [JsonPropertyName("player")]
        public PlayerState Player { get; set; }
    }

    // Represents the overall response from the /query endpoint
    public class QueryResponse
    {
        [JsonPropertyName("player")]
        public PlayerState Player { get; set; }

        [JsonPropertyName("playlists")]
        public List<PlaylistInfo> Playlists { get; set; }

        [JsonPropertyName("playlistItems")]
        public PlaylistItemsResult PlaylistItems { get; set; } // This property is of type PlaylistItemsResult

        [JsonPropertyName("playQueue")]
        public List<PlayQueueItemInfo> PlayQueue { get; set; }

        [JsonPropertyName("outputs")]
        public OutputsInfo Outputs { get; set; }
    }


    // Represents the player's current state
    public class PlayerState
    {
        [JsonPropertyName("info")]
        public PlayerInfo Info { get; set; }

        [JsonPropertyName("activeItem")]
        public ActiveItemInfo ActiveItem { get; set; }

        [JsonPropertyName("playbackState")]
        public PlaybackState PlaybackState { get; set; }

        [JsonPropertyName("volume")]
        public VolumeInfo Volume { get; set; }

        [JsonPropertyName("permissions")]
        public ApiPermissions Permissions { get; set; }

        // Deprecated properties from API spec, included for completeness but not used
        [JsonPropertyName("playbackMode")]
        public int? PlaybackMode { get; set; }
        [JsonPropertyName("playbackModes")]
        public List<string> PlaybackModes { get; set; }
    }

    // Represents basic player information
    public class PlayerInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("pluginVersion")]
        public string PluginVersion { get; set; }
    }

    // Represents information about the currently playing item
    public class ActiveItemInfo
    {
        [JsonPropertyName("playlistId")]
        public string PlaylistId { get; set; }

        [JsonPropertyName("playlistIndex")]
        public int PlaylistIndex { get; set; }

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("position")]
        public double Position { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("columns")]
        public List<string> Columns { get; set; } // Contains metadata like title, artist, album
    }

    // Represents the playback state (stopped, playing, paused)
    [JsonConverter(typeof(JsonStringEnumConverter))] // Automatically convert string enum values
    public enum PlaybackState
    {
        stopped,
        playing,
        paused
    }

    // Represents volume information
    public class VolumeInfo
    {
        [JsonPropertyName("type")]
        public VolumeType Type { get; set; }

        [JsonPropertyName("min")]
        public double Min { get; set; }

        [JsonPropertyName("max")]
        public double Max { get; set; }

        [JsonPropertyName("value")]
        public double Value { get; set; }

        [JsonPropertyName("isMuted")]
        public bool IsMuted { get; set; }
    }

    // Represents the type of volume control
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VolumeType
    {
        db,
        linear,
        upDown
    }

    // Represents API permissions
    public class ApiPermissions
    {
        [JsonPropertyName("changePlaylists")]
        public bool ChangePlaylists { get; set; }

        [JsonPropertyName("changeOutput")]
        public bool ChangeOutput { get; set; }

        [JsonPropertyName("changeClientConfig")]
        public bool ChangeClientConfig { get; set; }
    }

    // Represents a request to set player state (e.g., volume, position)
    public class SetPlayerStateRequest
    {
        [JsonPropertyName("volume")]
        public double? Volume { get; set; } // New absolute volume value

        [JsonPropertyName("relativeVolume")]
        public double? RelativeVolume { get; set; } // New relative volume value

        [JsonPropertyName("isMuted")]
        public bool? IsMuted { get; set; } // New mute state

        [JsonPropertyName("position")]
        public double? Position { get; set; } // New absolute playback position (seconds)

        [JsonPropertyName("relativePosition")]
        public double? RelativePosition { get; set; } // New relative playback position (seconds)

        [JsonPropertyName("options")]
        public List<SetOptionRequest> Options { get; set; } // Options to modify

        // Deprecated property
        [JsonPropertyName("playbackMode")]
        public int? PlaybackMode { get; set; }
    }

    // Represents a request to set a player option
    public class SetOptionRequest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } // Option identifier
        [JsonPropertyName("value")]
        public object Value { get; set; } // New option value (can be integer or boolean)
    }

    // --- Models for other API endpoints (partially included based on player-api.txt) ---

    public class PlaylistInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("index")]
        public int Index { get; set; }
        [JsonPropertyName("title")]
        public string Title { get; set; }
        [JsonPropertyName("isCurrent")]
        public bool IsCurrent { get; set; }
        [JsonPropertyName("itemCount")]
        public int ItemCount { get; set; }
        [JsonPropertyName("totalTime")]
        public double TotalTime { get; set; }
    }

    // Represents the response from the /playlists/{playlistId}/items/{range} endpoint
    public class GetPlaylistItemsResponse
    {
        [JsonPropertyName("offset")]
        public int Offset { get; set; }
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }
        // This property name matches the API spec for the /playlists/{playlistId}/items/{range} response
        [JsonPropertyName("playlistItems")]
        public PlaylistItemsResult PlaylistItems { get; set; }
    }

    // Represents the structure of the 'playlistItems' property within QueryResponse AND GetPlaylistItemsResponse
    public class PlaylistItemsResult // This class is needed to match the structure
    {
        [JsonPropertyName("offset")]
        public int Offset { get; set; }
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }
        [JsonPropertyName("items")]
        public List<PlaylistItemInfo> Items { get; set; }
    }


    // Represents a single item within a playlist
    public class PlaylistItemInfo
    {
        [JsonPropertyName("columns")]
        public List<string> Columns { get; set; } // Contains metadata like title, artist, album for this item
    }


    public class PlayQueueItemInfo
    {
        [JsonPropertyName("playlistId")]
        public string PlaylistId { get; set; }
        [JsonPropertyName("playlistIndex")]
        public int PlaylistIndex { get; set; }
        [JsonPropertyName("itemIndex")]
        public int ItemIndex { get; set; }
    }

    public class OutputsInfo
    {
        [JsonPropertyName("active")]
        public ActiveOutputInfo Active { get; set; }
        [JsonPropertyName("types")]
        public List<OutputTypeInfo> Types { get; set; }
    }

    public class ActiveOutputInfo
    {
        [JsonPropertyName("typeId")]
        public string TypeId { get; set; }
        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; }
    }

    public class OutputTypeInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("devices")]
        public List<OutputDeviceInfo> Devices { get; set; }
    }

    public class OutputDeviceInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}