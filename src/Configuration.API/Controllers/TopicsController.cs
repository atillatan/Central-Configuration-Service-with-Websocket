using Configuration.API.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace Configuration.API.Controllers;

[ApiController]
[Route("/[controller]/[action]")]
public class TopicsController : ControllerBase
{
    //private readonly Serilog.ILogger _logger = Log.ForContext<TopicsController>();
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
            Console.WriteLine("WebSocket connection established");
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
}
