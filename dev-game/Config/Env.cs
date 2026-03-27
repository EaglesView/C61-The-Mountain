using System.Collections.Generic;
using Godot;

namespace Config;

/// Reads key=value pairs from res://.env at first access and caches them.
/// The .env file is gitignored and packed into the exported build.
public static class Env
{
    private static Dictionary<string, string>? _values;

    private static Dictionary<string, string> Values
    {
        get
        {
            if (_values is not null) return _values;
            _values = new Dictionary<string, string>();

            if (!FileAccess.FileExists("res://.env"))
            {
                GD.PushError("[Env] .env file not found — copy .env.example to .env and fill in the values.");
                return _values;
            }

            using var file = FileAccess.Open("res://.env", FileAccess.ModeFlags.Read);
            while (!file.EofReached())
            {
                var line = file.GetLine().Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

                var idx = line.IndexOf('=');
                if (idx < 1) continue;

                _values[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
            return _values;
        }
    }

    public static string Get(string key)
    {
        if (Values.TryGetValue(key, out var value)) return value;
        GD.PushError($"[Env] Missing required key '{key}' in .env");
        return string.Empty;
    }
}
