using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Facet.Core;
using Facet.Deployment;

namespace Facet.Integration;

internal static class ReminderProtocol
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    // Existing clients and running releases share this protocol identity (ADR 0056).
    internal static string PipeName(string root) => "DailyDock-reminders-" + LaunchProtocol.Hash(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)));

    internal static async Task<T> ReadAsync<T>(Stream stream, int maximum, CancellationToken token)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        int count = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (count <= 0 || count > maximum) throw new InvalidDataException("알림 메시지가 너무 크거나 올바르지 않습니다.");
        byte[] body = new byte[count]; await stream.ReadExactlyAsync(body, token);
        return JsonSerializer.Deserialize<T>(body, Json) ?? throw new InvalidDataException("빈 알림 메시지입니다.");
    }

    internal static async Task WriteAsync<T>(Stream stream, T value, int maximum, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (body.Length > maximum) throw new InvalidDataException("알림 응답이 너무 큽니다.");
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(body, token); await stream.FlushAsync(token);
    }
}
