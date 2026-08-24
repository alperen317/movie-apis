using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace Movie.Infrastructure.Realtime;

/// <summary>
/// Makes <see cref="IHttpContextAccessor"/> see the connection's HttpContext
/// for the length of a hub method call.
/// </summary>
/// <remarks>
/// SignalR does not flow the original request's HttpContext into a hub method
/// invocation the way the ASP.NET Core pipeline does for an ordinary request —
/// <see cref="IHttpContextAccessor.HttpContext"/> is null inside one otherwise,
/// even on an authenticated connection. That silently breaks every check built
/// on <c>ICurrentUser</c>, <c>IListAccess</c> included, which
/// <see cref="ListHub"/> exists specifically to reuse rather than reinvent.
/// Setting it here once, for every hub method, means those checks work exactly
/// as they do behind an HTTP endpoint — no hub method has to know this is a
/// problem, let alone work around it itself.
/// </remarks>
public sealed class HttpContextPropagationHubFilter(IHttpContextAccessor accessor) : IHubFilter
{
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        accessor.HttpContext = invocationContext.Context.GetHttpContext();

        return next(invocationContext);
    }
}