using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace FooxClothStudio;

public static class FiveMPhotoCatalog
{
    public static async Task<ServerPhotoCatalog> ReadAsync(string? snapshotPath = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var targets = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:13172/json", timeout.Token));
            var target = targets.RootElement.EnumerateArray().FirstOrDefault(item =>
                item.TryGetProperty("url", out var url) && url.GetString() == "nui://game/ui/root.html");
            if (target.ValueKind == JsonValueKind.Undefined)
                throw new InvalidDataException("Ligue o FiveM ao servidor para incluir as roupas do GTA disponíveis nele.");
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(target.GetProperty("webSocketDebuggerUrl").GetString()!), timeout.Token);
            var contexts = new List<JsonElement>();
            await Command(socket, 1, "Runtime.enable", new { }, contexts, timeout.Token);
            var context = contexts.FirstOrDefault(item => item.GetProperty("origin").GetString() is string origin
                && (origin == "nui://skinshop" || origin == "https://cfx-nui-skinshop"));
            if (context.ValueKind == JsonValueKind.Undefined)
                throw new InvalidDataException("O menu de roupas não está carregado no FiveM. Ligue-se ao servidor e tente novamente.");
            var contextId = context.GetProperty("id").GetInt32();
            await Evaluate(socket, 2, contextId, "status", 4000, contexts, timeout.Token);
            var result = await Evaluate(socket, 3, contextId, "capture", 150000, contexts, timeout.Token);
            var catalog = ServerPhotoCatalog.Parse(result);
            var targetPath = Path.GetFullPath(snapshotPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParadiseClothStudio", "server-photo-catalog.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var temporary = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, result.GetRawText(), cancellationToken: timeout.Token);
                File.Move(temporary, targetPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return catalog;
        }
        catch (HttpRequestException)
        {
            throw new InvalidDataException("Não foi possível encontrar o FiveM ligado ao servidor. Abra o jogo para incluir as roupas do GTA usadas no servidor.");
        }
        catch (OperationCanceledException)
        {
            throw new InvalidDataException("O FiveM não respondeu a tempo. Confirme a ligação ao servidor e tente novamente.");
        }
    }

    private static async Task<JsonElement> Evaluate(ClientWebSocket socket, int request, int context, string action,
        int timeoutMs, List<JsonElement> contexts, CancellationToken cancellation)
    {
        var expression = $"fetch('https://skinshop/photoCatalog',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{action:'{action}'}}),signal:AbortSignal.timeout({timeoutMs})}}).then(response=>response.json())";
        var result = await Command(socket, request, "Runtime.evaluate", new
        {
            contextId = context, expression, returnByValue = true, awaitPromise = true
        }, contexts, cancellation);
        if (result.TryGetProperty("exceptionDetails", out _) || !result.GetProperty("result").TryGetProperty("value", out var value))
            throw new InvalidDataException("O servidor precisa da versão atualizada do skinshop para obter automaticamente o catálogo de fotos. Atualize e reinicie esse recurso.");
        if (value.TryGetProperty("error", out var error))
            throw new InvalidDataException(error.GetString() ?? "Não foi possível obter o catálogo do servidor.");
        return value.Clone();
    }

    private static async Task<JsonElement> Command(ClientWebSocket socket, int id, string method, object parameters,
        List<JsonElement> contexts, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellation);
        var buffer = new byte[65536];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);
                if (received.MessageType == WebSocketMessageType.Close) throw new InvalidDataException("A ligação ao FiveM foi fechada.");
                message.Write(buffer, 0, received.Count);
                if (message.Length > 32 * 1024 * 1024) throw new InvalidDataException("A resposta do FiveM é demasiado grande.");
            } while (!received.EndOfMessage);
            using var document = JsonDocument.Parse(message.ToArray());
            var root = document.RootElement;
            if (root.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
            {
                if (root.TryGetProperty("error", out _)) throw new InvalidDataException("Não foi possível consultar o catálogo no FiveM.");
                return root.GetProperty("result").Clone();
            }
            if (root.TryGetProperty("method", out var eventName) && eventName.GetString() == "Runtime.executionContextCreated")
                contexts.Add(root.GetProperty("params").GetProperty("context").Clone());
        }
    }
}
