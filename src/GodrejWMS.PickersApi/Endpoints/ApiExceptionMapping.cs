using GodrejWMS.Application.Common.Exceptions;

namespace GodrejWMS.PickersApi.Endpoints;

/// <summary>Maps the Application layer's own exception types (thrown by the same MediatR handlers
/// the Blazor pages call) to JSON API responses, mirroring how the Blazor pages surface them as
/// inline field errors / toasts today - see InwardNoPkm/Index.razor's "Lines[n].Property" parsing
/// for the shape ValidationException.Errors is expected to come back in.</summary>
public static class ApiExceptionMapping
{
    public static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(ex.Errors, detail: ex.Message);
        }
        catch (NotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Thrown directly by a handful of handlers for business-rule failures that aren't
            // FluentValidation (e.g. AssignInwardPutawayPkmHandler's permission/state checks) -
            // the Blazor pages surface these as a plain toast message, so a 400 with the message
            // is the closest JSON equivalent.
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
