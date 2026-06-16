using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemoTracer;

internal enum TeamSide
{
    Terrorist,
    CounterTerrorist
}

internal sealed class TeamRegistry
{
    private readonly Dictionary<string, TeamDefinition> _lookup = new(StringComparer.OrdinalIgnoreCase);
    private List<TeamDefinition> _teams = new();

    public bool Loaded { get; private set; }

    public IReadOnlyList<TeamDefinition> Teams => _teams;

    public void Load(string moduleDirectory, out string message, bool force = false)
    {
        if (Loaded && !force)
        {
            message = $"team registry already loaded, teams={_teams.Count}";
            return;
        }

        var configPath = Path.Combine(moduleDirectory, "teams.json");
        try
        {
            var teams = File.Exists(configPath)
                ? ReadConfig(configPath)
                : DefaultTeams();
            Rebuild(teams);
            Loaded = true;
            message = File.Exists(configPath)
                ? $"team registry loaded from {configPath}, teams={_teams.Count}"
                : $"team registry using built-in defaults, teams={_teams.Count}; put teams.json next to the plugin DLL to customize";
        }
        catch (Exception ex)
        {
            Rebuild(DefaultTeams());
            Loaded = true;
            message = $"team registry failed to read {configPath}: {ex.Message}; using built-in defaults, teams={_teams.Count}";
        }
    }

    public bool TryResolve(string query, out TeamDefinition team, out string error)
    {
        team = new TeamDefinition();
        error = string.Empty;

        var normalized = Normalize(query);
        if (normalized.Length == 0)
        {
            error = "empty team name";
            return false;
        }

        if (_lookup.TryGetValue(normalized, out var directTeam))
        {
            team = directTeam;
            return true;
        }

        var matches = _teams
            .Where(candidate => TeamMatches(candidate, normalized))
            .DistinctBy(candidate => candidate.Key)
            .ToArray();

        if (matches.Length == 1)
        {
            team = matches[0];
            return true;
        }

        error = matches.Length == 0
            ? $"team not found: {query}"
            : $"ambiguous team {query}: {string.Join(", ", matches.Select(match => match.Key))}";
        return false;
    }

    private static List<TeamDefinition> ReadConfig(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<TeamConfig>(
                         json,
                         new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new InvalidOperationException("teams.json is empty");
        return config.Teams;
    }

    private void Rebuild(IEnumerable<TeamDefinition> teams)
    {
        _lookup.Clear();
        _teams = teams
            .Where(team => !string.IsNullOrWhiteSpace(team.Key) && team.Players.Count > 0)
            .Select(NormalizeTeam)
            .ToList();

        foreach (var team in _teams)
        {
            AddLookup(team.Key, team);
            AddLookup(team.Name, team);
            foreach (var alias in team.Aliases)
                AddLookup(alias, team);
        }
    }

    private void AddLookup(string value, TeamDefinition team)
    {
        var normalized = Normalize(value);
        if (normalized.Length > 0)
            _lookup[normalized] = team;
    }

    private static TeamDefinition NormalizeTeam(TeamDefinition team)
    {
        team.Key = team.Key.Trim();
        team.Name = string.IsNullOrWhiteSpace(team.Name) ? team.Key : team.Name.Trim();
        team.Logo = team.Logo.Trim();
        team.Aliases = team.Aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        team.Players = team.Players
            .Where(player => !string.IsNullOrWhiteSpace(player))
            .Select(player => player.Trim())
            .Take(5)
            .ToList();
        return team;
    }

    private static bool TeamMatches(TeamDefinition team, string normalizedQuery)
    {
        return Normalize(team.Key).Contains(normalizedQuery, StringComparison.Ordinal) ||
               Normalize(team.Name).Contains(normalizedQuery, StringComparison.Ordinal) ||
               team.Aliases.Any(alias => Normalize(alias).Contains(normalizedQuery, StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        return new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static List<TeamDefinition> DefaultTeams()
    {
        return
        [
            Team("vitality", "Team Vitality", "vita", ["vit"], ["apEX", "ZywOo", "ropz", "mezii", "flameZ"]),
            Team("spirit", "Spirit", "spir", ["ts"], ["sh1ro", "magixx", "tN1R", "zont1x", "donk"]),
            Team("falcons", "Falcons", "fal", ["falcon"], ["NiKo", "TeSeS", "m0NESY", "karrigan", "kyousuke"]),
            Team("mouz", "MOUZ", "mouz", ["mouse"], ["jL", "torzsi", "Spinx", "xelex", "xertioN"]),
            Team("faze", "FaZe Clan", "faze", ["fazeclan"], ["enkay J", "frozen", "Twistzz", "broky", "jcobbb"]),
            Team("mongolz", "The MongolZ", "mngz", ["themongolz"], ["bLitz", "Techno4K", "mzinho", "910", "cobrazera"]),
            Team("navi", "Natus Vincere", "navi", ["nav"], ["Aleksib", "iM", "b1t", "w0nderful", "makazze"]),
            Team("g2", "G2 Esports", "g2", [], ["huNter-", "NertZ", "SunPayus", "HeavyGod", "MATYS"]),
            Team("aurora", "Aurora", "aura", [], ["MAJ3R", "XANTARES", "woxic", "soulfly", "Wicadia"]),
            Team("furia", "FURIA Esports", "furi", [], ["yuurih", "FalleN", "KSCERATO", "YEKINDAR", "molodoy"])
        ];
    }

    private static TeamDefinition Team(
        string key,
        string name,
        string logo,
        string[] aliases,
        string[] players)
    {
        return new TeamDefinition
        {
            Key = key,
            Name = name,
            Logo = logo,
            Aliases = aliases.ToList(),
            Players = players.ToList()
        };
    }

    private sealed class TeamConfig
    {
        [JsonPropertyName("teams")]
        public List<TeamDefinition> Teams { get; set; } = new();
    }
}

internal sealed class TeamDefinition
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("logo")]
    public string Logo { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = new();

    [JsonPropertyName("players")]
    public List<string> Players { get; set; } = new();
}
