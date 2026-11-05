using System.Text.Json;
using System.Text.Json.Serialization;
using Dms.Api.Demo;

namespace Dms.Api.Controllers;

/// <summary>Atomic, versioned JSON snapshot of customer, request, quote and delivery data.</summary>
public sealed class JsonApplicationStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonApplicationStore(string path) => _path = path;

    public void Load(DemoBackend backend)
    {
        if (!File.Exists(_path)) return;
        DemoBackend.SavedState state = JsonSerializer.Deserialize<DemoBackend.SavedState>(File.ReadAllText(_path), Json)
            ?? throw new InvalidDataException("The saved application state was empty.");
        backend.ImportState(state);
    }

    public void Save(DemoBackend backend)
    {
        lock (_gate)
        {
            string? parent = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(backend.ExportState(), Json));
            File.Move(temporary, _path, overwrite: true);
        }
    }
}
