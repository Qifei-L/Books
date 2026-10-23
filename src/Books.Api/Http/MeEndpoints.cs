using Books.Api.Auth;
using Books.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Books.Api.Http;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (PrincipalAccessor principals, IBooksDb db, HttpContext http, CancellationToken ct) =>
        {
            var principal = await principals.GetAsync(ct);
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == principal.UserId, ct);
            return Envelope.Ok(http, new
            {
                id = user.Id,
                display_name = user.DisplayName,
                email = principal.VerifiedEmail,
                email_verified = principal.VerifiedEmail is not null,
                status = "active",
            });
        });
    }
}

public static class Envelope
{
    public static IResult Ok(HttpContext http, object data) =>
        Results.Ok(new { data, request_id = http.TraceIdentifier });
}
