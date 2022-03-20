
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Configuration.API.Configuration;

public interface IWebsocketHandler
{
    Task Handle(string topic, string client, WebSocket websocket);
}

public class WebsocketHandler : IWebsocketHandler
{    
    private static ConcurrentDictionary<string, WebSocket> _websocketConnections = new ConcurrentDictionary<string, WebSocket>();

    public WebsocketHandler()
    {
        SetupCleanUpTask();
    }

    public async Task Handle(string topic, string client, WebSocket webSocket)
    {
        client = client ?? Guid.NewGuid().ToString();

        if (client.Contains('_') || topic.Contains('_')) throw new ArgumentException("Client and Topic values cannot contains '_' underscore ");

        string newGuid = Guid.NewGuid().ToString();
        string sessionId = $"{client}_{topic}_{newGuid}";

        lock (_websocketConnections)
        {
            _websocketConnections[sessionId] = webSocket;
        }
        string logMessage = $"Client {client} has joined the TOPIC:{topic}, with sessionId:{sessionId}";
        await SendMessageToClients(logMessage);
        Console.WriteLine(logMessage);

        while (webSocket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessage(sessionId, webSocket);
            if (message != null)
            {
                try
                {
                    await SendMessageToClients(message);
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }
    }

    private async Task<string> ReceiveMessage(string sessionId, WebSocket webSocket)
    {
        var arraySegment = new ArraySegment<byte>(new byte[4096]);
        var receivedMessage = await webSocket.ReceiveAsync(arraySegment, CancellationToken.None);
        if (receivedMessage.MessageType == WebSocketMessageType.Text)
        {
            var message = Encoding.Default.GetString(arraySegment).TrimEnd('\0');
            if (!string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine($"Received message {sessionId}:{message}");
                return $"{sessionId}:{message}";
            }
        }
        return null;
    }

    private async Task SendMessageToClients(string message)
    {
        var tasks = _websocketConnections.Select(async item =>
        {            
            string targetTopic = message.Split('_')[1];            

            if (item.Value.State == WebSocketState.Open)
            {
                var bytes = Encoding.Default.GetBytes(message);
                var arraySegment = new ArraySegment<byte>(bytes);

                if (item.Key.Contains(targetTopic))
                    await item.Value.SendAsync(arraySegment, WebSocketMessageType.Text, true, CancellationToken.None);

                Console.WriteLine($"Sent message: {message} => {item.Key}");
            }
        });
        await Task.WhenAll(tasks);
    }

    private void SetupCleanUpTask()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                IEnumerable<KeyValuePair<string, WebSocket>> closedSockets;
                List<string> removedSockets = new List<string>();
                closedSockets = _websocketConnections.Where(x => x.Value.State != WebSocketState.Open && x.Value.State != WebSocketState.Connecting);

                lock (_websocketConnections)
                {
                    foreach (KeyValuePair<string, WebSocket> item in closedSockets)
                    {
                        if (_websocketConnections.TryRemove(item.Key, out WebSocket retrievedValue))
                        {
                            removedSockets.Add(item.Key);
                            Console.WriteLine($"the object was removed successfully:{item.Key}");
                        }
                        else{
                            Console.WriteLine($"Unable to remove {item.Key}");
                        }
                    }
                }

                foreach (var key in removedSockets)
                {
                    string cleanupMessage = $"User with id {key} has left the TOPIC";
                    await SendMessageToClients(cleanupMessage);
                    Console.WriteLine(cleanupMessage);
                }

                await Task.Delay(5000);
            }

        });
    }
}

