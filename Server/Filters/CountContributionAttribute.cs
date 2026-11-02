namespace RevolutionaryWebApp.Server.Filters;

using System;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;

/// <summary>
///   Charges successful (2xx) actions against the authenticated user's daily contribution budget.
///   Apply alongside an authorization attribute. Amounts must be positive.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CountContributionAttribute : Attribute, IAsyncActionFilter
{
    public CountContributionAttribute(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        Amount = amount;
    }

    public int Amount { get; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.AuthenticatedUserOrThrow();

        // Use a separate context: actions may manage their own transactions and notification-enabled saves.
        await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cancellationToken = context.HttpContext.RequestAborted;

        // Concurrent first requests may both need to create the row.
        var initialLimit = UserContributionCount.GetInitialLimit(user.AccessCachedGroupsOrThrow());

        // We need a shorter name to fit the SQL in the source code.
        var db = database.Database;
        await db.ExecuteSqlInterpolatedAsync($"""
                                              INSERT INTO user_contribution_counts
                                                  (user_id, contribution_limit, current_contribution, 
                                                   contribution_exceeded, first_contributed_at)
                                              VALUES ({user.Id}, {initialLimit}, 0, FALSE, {DateTime.UtcNow})
                                              ON CONFLICT (user_id) DO NOTHING
                                              """, cancellationToken);
        var counts = database.UserContributionCounts.Where(count => count.UserId == user.Id);
        var count = await counts.AsNoTracking().SingleAsync(cancellationToken);

        // This is an approximate budget: requests admitted concurrently can finish after the limit is reached.
        // Do not hold a transaction or row lock while the action executes.
        if (count.CurrentContribution >= count.ContributionLimit)
        {
            context.Result = new ObjectResult("Daily contribution limit reached. Please try again tomorrow.")
            {
                StatusCode = StatusCodes.Status429TooManyRequests,
            };

            return;
        }

        // Run the actual endpoint action
        var executed = await next();
        var status = (executed.Result as IStatusCodeActionResult)?.StatusCode ??
            context.HttpContext.Response.StatusCode;

        // Then consume quota if it succeeded
        if (executed.Exception == null && !executed.Canceled && status is >= 200 and < 300)
        {
            // As the user's request has succeeded, we no longer want to cancel the transaction as otherwise the user
            // could maybe with careful timing avoid eating quota for requests that still managed to run.

            // ReSharper disable MethodSupportsCancellation

            // Keep the increment, first limit-crossing flag, and log entry atomic with respect to resets.
            await using var transaction = await db.BeginTransactionAsync();
            await counts.ExecuteUpdateAsync(update => update.SetProperty(
                contribution => contribution.CurrentContribution,
                contribution => contribution.CurrentContribution + Amount));

            var reachedLimit = await counts.Where(contribution => !contribution.ContributionExceeded &&
                    contribution.CurrentContribution >= contribution.ContributionLimit)
                .ExecuteUpdateAsync(update =>
                    update.SetProperty(contribution => contribution.ContributionExceeded, true));

            if (reachedLimit > 0)
            {
                var limit = await counts.Select(contribution => contribution.ContributionLimit)
                    .SingleAsync();

                database.LogEntries.Add(new LogEntry(
                    $"User {user.Id} reached their daily contribution limit of {limit}")
                {
                    TargetUserId = user.Id,
                });

                await database.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            // ReSharper restore MethodSupportsCancellation
        }
    }
}
