namespace ThriveDevCenter.Server
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;

    public class HttpResponseExceptionFilter : IActionFilter, IOrderedFilter
    {
        public int Order { get; } = int.MaxValue - 10;

        public void OnActionExecuting(ActionExecutingContext context) { }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Exception is not HttpResponseException exception)
                return;

            context.Result = new ObjectResult(exception.Value)
            {
                StatusCode = exception.Status,
            };

            context.ExceptionHandled = true;
        }
    }
}
