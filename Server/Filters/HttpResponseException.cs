namespace ThriveDevCenter.Server.Filters
{
    using System;
    using Microsoft.AspNetCore.Http;

    /// <summary>
    ///   Exception thrown when the client has made a really bad request and a controller doesn't want to return
    ///   normal type of data
    /// </summary>
    public class HttpResponseException : Exception
    {
        public int Status { get; set; } = StatusCodes.Status400BadRequest;

        public object Value { get; set; }

        public string ContentType { get; set; }
    }
}
