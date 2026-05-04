using System.Text.Json;
using POC.Models;

namespace POC.Services;

internal sealed class EsdToIsoEventWriter : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly StreamWriter _writer;

    public EsdToIsoEventWriter(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(path, append: false);
    }

    public void Write(EsdToIsoProgress progress)
    {
        _writer.WriteLine(JsonSerializer.Serialize(progress, _jsonOptions));
        _writer.Flush();
    }

    public void Dispose() => _writer.Dispose();
}
