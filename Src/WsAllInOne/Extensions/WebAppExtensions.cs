using WsTuneCommon.Models;

namespace WsAllInOne.Extensions;

public static class WebAppExtensions
{
    public static void AddConfigurationEndpoints(this WebApplication webApplication, int i)
    {
        // POST /api/Configuration/add - Add a new config (enforce max 10)
        webApplication.MapPost("/api/Configuration/add", (TunnelConfigDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return Results.BadRequest("Config name is required.");

            // Ensure uniqueness
            if (Storage.Configs.ContainsKey(dto.Name))
                return Results.Conflict($"Config with name '{dto.Name}' already exists.");

            // If we have reached max limit, remove the first entry
            if (Storage.Configs.Count >= i)
            {
                return Results.Conflict($"Config Reached max '{Storage.Configs.Count}' configs.");
            }

            dto.TargetHost = "localhost";
            
            Storage.Configs.TryAdd(dto.Name, dto);
            return Results.Ok($"Config '{dto.Name}' added successfully.");
        });

// POST /api/Configuration/update - Update an existing config
        webApplication.MapPost("/api/Configuration/update", (TunnelConfigDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return Results.BadRequest("Config name is required.");

            if (!Storage.Configs.ContainsKey(dto.Name))
                return Results.NotFound($"Config with name '{dto.Name}' not found.");

            dto.TargetHost = "localhost";
            
            Storage.Configs[dto.Name] = dto; // ConcurrentDictionary supports safe updates
            return Results.Ok($"Config '{dto.Name}' updated successfully.");
        });

// DELETE /api/Configuration/delete - Delete a config by name
        webApplication.MapDelete("/api/Configuration/delete", (string name) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest("Config name is required.");

            if (!Storage.Configs.TryRemove(name, out _))
                return Results.NotFound($"Config with name '{name}' not found.");

            return Results.Ok($"Config '{name}' deleted successfully.");
        });

// POST /api/Configuration/list - List all configs
        webApplication.MapPost("/api/Configuration/list", () =>
        {
            var list = Storage.Configs.Values.ToList();
            return Results.Ok(list);
        });

// POST /api/Configuration/get - Get a specific config by name
        webApplication.MapPost("/api/Configuration/get", (string name) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest("Config name is required.");

            if (!Storage.Configs.TryGetValue(name, out var config))
                return Results.NotFound($"Config with name '{name}' not found.");

            return Results.Ok(config);
        });
    }
}