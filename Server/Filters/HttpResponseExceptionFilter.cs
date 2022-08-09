namespace ThriveDevCenter.Server.Filters
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using Microsoft.AspNetCore.Mvc.Formatters;

    public class HttpResponseExceptionFilter : IActionFilter, IOrderedFilter
    {
        public int Order => int.MaxValue - 10;

        public void OnActionExecuting(ActionExecutingContext context) { }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Exception is not HttpResponseException exception)
                return;

            var result = new ObjectResult(exception.Value)
            {
                StatusCode = exception.Status,
            };

            if (!string.IsNullOrEmpty(exception.ContentType))
            {
                result.ContentTypes = new MediaTypeCollection()
                {
                    exception.ContentType,
                };
            }

            context.Result = result;

            context.ExceptionHandled = true;
        }
    }
}
