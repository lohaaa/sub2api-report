using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sub2ApiReport.Api.Middleware;
using Sub2ApiReport.Api.Models;
using Sub2ApiReport.Application.Audit;
using Sub2ApiReport.Application.People;
using Sub2ApiReport.Application.Sub2Api;

namespace Sub2ApiReport.Api.Endpoints;

internal static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/people")
            .RequireAuthorization()
            .WithTags("People");

        group.MapGet("", ListAsync)
            .WithName("ListPeople")
            .WithSummary("列出人员")
            .Produces<IReadOnlyList<PersonResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{personId:guid}", GetAsync)
            .WithName("GetPerson")
            .WithSummary("获取人员")
            .Produces<PersonResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("CreatePerson")
            .WithSummary("创建人员")
            .Produces<PersonResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{personId:guid}", UpdateAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("UpdatePerson")
            .WithSummary("更新人员")
            .Produces<PersonResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{personId:guid}", DeactivateAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("DeactivatePerson")
            .WithSummary("停用人员")
            .WithDescription("保留历史归属和报告引用，只把人员标记为停用。")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/assignments/{assignmentId:guid}", GetAssignmentAsync)
            .WithName("GetApiKeyAssignment")
            .WithSummary("获取 API Key 归属")
            .Produces<ApiKeyAssignmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{personId:guid}/assignments", CreateAssignmentAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("CreateApiKeyAssignment")
            .WithSummary("创建 API Key 归属")
            .Produces<ApiKeyAssignmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/assignments/{assignmentId:guid}", UpdateAssignmentAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("UpdateApiKeyAssignment")
            .WithSummary("更新 API Key 归属有效期")
            .Produces<ApiKeyAssignmentResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/assignments/{assignmentId:guid}", DeleteAssignmentAsync)
            .RequireAntiforgery()
            .RequireRateLimiting("configuration")
            .WithName("DeleteApiKeyAssignment")
            .WithSummary("删除 API Key 归属")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<PersonResponse>>> ListAsync(
        IPeopleService peopleService,
        CancellationToken cancellationToken)
    {
        var people = await peopleService.ListAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<PersonResponse>>(people.Select(Map).ToArray());
    }

    private static async Task<IResult> GetAsync(
        Guid personId,
        IPeopleService peopleService,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(Map(await peopleService.GetAsync(personId, cancellationToken)));
        }
        catch (PeopleResourceNotFoundException)
        {
            return NotFound("人员不存在。");
        }
    }

    private static async Task<IResult> CreateAsync(
        CreatePersonRequest request,
        ClaimsPrincipal principal,
        IPeopleService peopleService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var person = await peopleService.CreateAsync(
                new CreatePersonCommand(request.Code, request.DisplayName),
                cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "people.create",
                person.Id,
                httpContext,
                cancellationToken);
            return TypedResults.Created($"/api/v1/people/{person.Id}", Map(person));
        }
        catch (PeopleConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid personId,
        UpdatePersonRequest request,
        ClaimsPrincipal principal,
        IPeopleService peopleService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var person = await peopleService.UpdateAsync(
                new UpdatePersonCommand(
                    personId,
                    request.Code,
                    request.DisplayName,
                    request.IsActive,
                    request.Revision),
                cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "people.update",
                person.Id,
                httpContext,
                cancellationToken);
            return TypedResults.Ok(Map(person));
        }
        catch (PeopleResourceNotFoundException)
        {
            return NotFound("人员不存在。");
        }
        catch (PeopleConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> DeactivateAsync(
        Guid personId,
        ClaimsPrincipal principal,
        IPeopleService peopleService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await peopleService.DeactivateAsync(personId, cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "people.deactivate",
                personId,
                httpContext,
                cancellationToken);
            return TypedResults.NoContent();
        }
        catch (PeopleResourceNotFoundException)
        {
            return NotFound("人员不存在。");
        }
        catch (PeopleConflictException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static async Task<IResult> GetAssignmentAsync(
        Guid assignmentId,
        IPeopleService peopleService,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(Map(await peopleService.GetAssignmentAsync(
                assignmentId,
                cancellationToken)));
        }
        catch (PeopleResourceNotFoundException)
        {
            return NotFound("归属记录不存在。");
        }
    }

    private static async Task<IResult> CreateAssignmentAsync(
        Guid personId,
        CreateApiKeyAssignmentRequest request,
        ClaimsPrincipal principal,
        IPeopleService peopleService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var assignment = await peopleService.CreateAssignmentAsync(
                new CreateApiKeyAssignmentCommand(
                    personId,
                    request.ExternalApiKeyId,
                    request.ValidFrom,
                    request.ValidTo),
                cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "people.assignment.create",
                assignment.Id,
                httpContext,
                cancellationToken);
            return TypedResults.Created(
                $"/api/v1/people/assignments/{assignment.Id}",
                Map(assignment));
        }
        catch (PeopleResourceNotFoundException)
        {
            return NotFound("人员或 API Key 不存在。");
        }
        catch (PeopleConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> UpdateAssignmentAsync(
        Guid assignmentId,
        UpdateApiKeyAssignmentRequest request,
        ClaimsPrincipal principal,
        IPeopleService peopleService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var assignment = await peopleService.UpdateAssignmentAsync(
                new UpdateApiKeyAssignmentCommand(
                    assignmentId,
                    request.ValidFrom,
                    request.ValidTo,
                    request.Revision),
                cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "people.assignment.update",
                assignment.Id,
                httpContext,
                cancellationToken);
            return TypedResults.Ok(Map(assignment));
        }
        catch (PeopleResourceNotFoundException)
        {
            return NotFound("归属记录不存在。");
        }
        catch (PeopleConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> DeleteAssignmentAsync(
        Guid assignmentId,
        ClaimsPrincipal principal,
        IPeopleService peopleService,
        IAuditWriter auditWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await peopleService.DeleteAssignmentAsync(assignmentId, cancellationToken);
            await WriteAuditAsync(
                auditWriter,
                principal,
                "people.assignment.delete",
                assignmentId,
                httpContext,
                cancellationToken);
            return TypedResults.NoContent();
        }
        catch (PeopleResourceNotFoundException)
        {
            return NotFound("归属记录不存在。");
        }
        catch (PeopleConflictException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private static PersonResponse Map(PersonSnapshot person) => new(
        person.Id,
        person.Code,
        person.DisplayName,
        person.IsActive,
        person.CurrentApiKeyCount,
        person.AssignmentCount,
        person.Revision,
        person.UpdatedAt);

    internal static ApiKeyAssignmentResponse Map(ApiKeyAssignmentSnapshot assignment) => new(
        assignment.Id,
        assignment.PersonId,
        assignment.PersonCode,
        assignment.PersonDisplayName,
        assignment.ValidFrom,
        assignment.ValidTo,
        assignment.Revision);

    private static ProblemHttpResult NotFound(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Not Found",
        detail: detail);

    private static ProblemHttpResult Conflict(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Conflict",
        detail: detail);

    private static ValidationProblem Validation(ArgumentException exception) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "request"] = [exception.Message],
        });

    private static Task WriteAuditAsync(
        IAuditWriter auditWriter,
        ClaimsPrincipal principal,
        string action,
        Guid targetId,
        HttpContext httpContext,
        CancellationToken cancellationToken) => auditWriter.WriteAsync(
            principal.Identity?.Name,
            action,
            targetId.ToString(),
            "succeeded",
            httpContext.TraceIdentifier,
            null,
            cancellationToken);
}
