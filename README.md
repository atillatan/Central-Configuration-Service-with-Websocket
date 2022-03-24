# Central-Configuration-Service-with-Websocket
The Configuration server is implemented using .NET rest API. It accepts client connections and notifies them of topic updates.


- Session 
  - Once a client connects to a server, the session will be established and a session id is assigned to the client.
  - The client sends heartbeats(ping) at a particular time interval to keep the session valid.
  - If the Server does not receive heartbeats from a client for more than the period (session timeout) specified at the starting of the service, it decides that the client died.
 
- Topics
  - Topics are a simple mechanism for the client to get notifications about the changes in the Server. 
  - Clients can subscribe a particular Topics. 
  - Server send a notification to the Topics for registered client for any of the Topics (on which client subscribes) changes.
  - Topic notifications are triggered only once. If a client wants a Topic notification again, it must be done through another read operation. 
  - When a connection session is expired, the client will be disconnected from the server and the associated topics are also removed.
  - Once a Server starts, it will wait for the clients to connect particular topics
  - Clients will connect to one of the Topic in the Server.
  - Once a client is connected, the server assigns a session ID to the particular client and sends an acknowledgement to the client
  - If the client does not get an acknowledgment, it simply tries to connect after particular time period.
  - Once connected to the Server, the client will send heartbeats(ping) to the Server in a regular interval to make sure that the connection is not lost.

---

- Add singleton WebSocketHandler as a service, configure http request pipeline for using WebSocket


```csharp
// Program.cs
...
builder.Services.AddSingleton<IWebsocketHandler, WebsocketHandler>();
...

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromMinutes(2) });
...
```

- Add responsible controler for WebSocket requests

```csharp

[ApiController]
[Route("/[controller]/[action]")]
public class TopicsController : ControllerBase
{
  //  private readonly Serilog.ILogger _logger = Log.ForContext<TopicsController>();
    public IWebsocketHandler WebsocketHandler { get; }

    public TopicsController(IWebsocketHandler websocketHandler)
    {
        WebsocketHandler = websocketHandler;
    }

    [HttpGet("{topic}")]
    public async Task Subscribe([FromRoute] string topic, [FromQuery] string client)
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var websocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            await WebsocketHandler.Handle(topic, client, websocket);
           // _logger.Information("WebSocket connection established");
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
}


```


- Add WebSocketHandler


```csharp
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

                await Task.Delay(1000*60); 
            }

        });
    }
}
```


- Test connection
execute following lines in your chrome browser console

```javascript
let websocket = new WebSocket("ws://localhost:8106/topics/subscribe/topic1?client=atilla1");
websocket.onmessage = function(event) { if(event.data)console.log(event.data); }
websocket.send('Hello');
```


```shell
Client atilla1 has joined the TOPIC:topic1, with sessionId:atilla1_topic1_101edf6f-3e6e-4d19-a7ab-8e3e2acd2be4
Received message atilla1_topic1_101edf6f-3e6e-4d19-a7ab-8e3e2acd2be4:hello
Sent message: atilla1_topic1_101edf6f-3e6e-4d19-a7ab-8e3e2acd2be4:hello => atilla1_topic1_101edf6f-3e6e-4d19-a7ab-8e3e2acd2be4
```
