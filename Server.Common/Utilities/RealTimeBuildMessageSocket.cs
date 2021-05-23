namespace ThriveDevCenter.Server.Common.Utilities
{
    using System;
    using System.Net.WebSockets;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Shared;
    using Shared.Models;

    /// <summary>
    ///   Wrapper around WebSocket to provide support for the realtime build messaging protocol
    /// </summary>
    public class RealTimeBuildMessageSocket
    {
        private readonly WebSocket socket;

        private readonly byte[] messageSizeBuffer = new byte [4];

        private byte[] messageBuffer;

        public RealTimeBuildMessageSocket(WebSocket socket)
        {
            this.socket = socket;
        }

        public WebSocketCloseStatus? CloseStatus => socket.CloseStatus;

        public async Task<(RealTimeBuildMessage message, bool closed)> Read(CancellationToken cancellationToken)
        {
            WebSocketReceiveResult sizeReadResult;
            try
            {
                sizeReadResult = await
                    socket.ReceiveAsync(new ArraySegment<byte>(messageSizeBuffer), cancellationToken);
            }
            catch (WebSocketException e)
            {
                throw new WebSocketProtocolException("Error reading message size", e);
            }

            if (sizeReadResult.CloseStatus.HasValue)
                return (null, true);

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(messageSizeBuffer);

            var messageSize = BitConverter.ToInt32(messageSizeBuffer);

            if (messageSize > AppInfo.MaxSingleBuildOutputMessageLength)
            {
                throw new WebSocketBuildMessageTooLongException(
                    $"Received too long realTimeBuildMessage length: {messageSize}");
            }

            if (messageSize <= 0)
                return (null, false);

            // Read the realTimeBuildMessage
            // First allocate big enough buffer
            if (messageBuffer == null || messageBuffer.Length < messageSize)
            {
                messageBuffer =
                    new byte[Math.Min((int)(messageSize * 1.5f), AppInfo.MaxSingleBuildOutputMessageLength)];
            }

            // TODO: can be actually receive a partial amount of the data here? so should we loop until
            // messageSize has been received? (doesn't seem to be the case, at least with reasonable size messages)
            WebSocketReceiveResult readResult;
            try
            {
                readResult = await socket.ReceiveAsync(new ArraySegment<byte>(messageBuffer), cancellationToken);
            }
            catch (WebSocketException e)
            {
                throw new WebSocketProtocolException("Error reading message content", e);
            }

            if (readResult.CloseStatus.HasValue)
                return (null, true);

            if (readResult.Count != messageSize)
            {
                throw new WebSocketBuildMessageLengthMisMatchException(
                    $"Read realTimeBuildMessage length doesn't match reported length: {messageSize} " +
                    $"actual: {readResult.Count}");
            }

            try
            {
                var message = JsonSerializer.Deserialize<RealTimeBuildMessage>(Encoding.UTF8.GetString(
                    messageBuffer, 0, readResult.Count));

                if (message == null)
                    throw new NullReferenceException("parsed realTimeBuildMessage is null");

                return (message, false);
            }
            catch (Exception e)
            {
                throw new InvalidWebSocketBuildMessageFormatException("Can't parse realtime build message", e);
            }
        }

        public async Task Write(RealTimeBuildMessage message, CancellationToken cancellationToken)
        {
            var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

            var lengthBuffer = BitConverter.GetBytes(Convert.ToInt32(buffer.Length));

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(lengthBuffer);

            await socket.SendAsync(lengthBuffer, WebSocketMessageType.Binary, false, cancellationToken);
            await socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
        }
    }

    [Serializable]
    public class WebSocketProtocolException : Exception
    {
        public WebSocketProtocolException() { }
        public WebSocketProtocolException(string message) : base(message) { }
        public WebSocketProtocolException(string message, Exception inner) : base(message, inner) { }

        protected WebSocketProtocolException(SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    public class WebSocketBuildMessageTooLongException : Exception
    {
        public WebSocketBuildMessageTooLongException() { }
        public WebSocketBuildMessageTooLongException(string message) : base(message) { }
        public WebSocketBuildMessageTooLongException(string message, Exception inner) : base(message, inner) { }

        protected WebSocketBuildMessageTooLongException(SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    public class WebSocketBuildMessageLengthMisMatchException : Exception
    {
        public WebSocketBuildMessageLengthMisMatchException() { }
        public WebSocketBuildMessageLengthMisMatchException(string message) : base(message) { }
        public WebSocketBuildMessageLengthMisMatchException(string message, Exception inner) : base(message, inner) { }

        protected WebSocketBuildMessageLengthMisMatchException(SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    public class InvalidWebSocketBuildMessageFormatException : Exception
    {
        public InvalidWebSocketBuildMessageFormatException() { }
        public InvalidWebSocketBuildMessageFormatException(string message) : base(message) { }
        public InvalidWebSocketBuildMessageFormatException(string message, Exception inner) : base(message, inner) { }

        protected InvalidWebSocketBuildMessageFormatException(SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }
}
