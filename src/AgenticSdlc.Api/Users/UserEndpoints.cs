using Microsoft.AspNetCore.Http.HttpResults;

namespace AgenticSdlc.Api.Users;

/// <summary><c>/api/users</c> CRUD + list (spec 2026-10-05-user-management-api §4). Handlers stay thin.</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("", List).WithName("ListUsers");
        group.MapGet("/{id:guid}", GetById).WithName("GetUser");
        group.MapPost("", Create).WithName("CreateUser");
        group.MapPut("/{id:guid}", Update).WithName("UpdateUser");
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteUser");

        return app;
    }

    private static Results<Ok<PagedResponse<UserResponse>>, ValidationProblem> List(
        [AsParameters] UserQuery query, IUserRepository users)
    {
        var errors = UserValidator.Validate(query, out var filter, out var page, out var pageSize);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var result = users.Search(filter, page, pageSize);
        var items = result.Items.Select(UserResponse.From).ToList();
        return TypedResults.Ok(new PagedResponse<UserResponse>(items, page, pageSize, result.TotalCount));
    }

    private static Results<Ok<UserResponse>, ProblemHttpResult> GetById(Guid id, IUserRepository users) =>
        users.Get(id) is { } user
            ? TypedResults.Ok(UserResponse.From(user))
            : NotFound();

    private static Results<Created<UserResponse>, ValidationProblem, ProblemHttpResult> Create(
        CreateUserRequest request, IUserRepository users)
    {
        var errors = UserValidator.Validate(request.Email, request.FirstName, request.LastName, out var fields);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var result = users.Create(fields);
        if (result.Status == WriteStatus.DuplicateEmail)
            return DuplicateEmail();

        var user = result.User!;
        return TypedResults.Created($"/api/users/{user.Id}", UserResponse.From(user));
    }

    private static Results<Ok<UserResponse>, ValidationProblem, ProblemHttpResult> Update(
        Guid id, UpdateUserRequest request, IUserRepository users)
    {
        var errors = UserValidator.Validate(request.Email, request.FirstName, request.LastName, out var fields);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var result = users.Update(id, fields);
        return result.Status switch
        {
            WriteStatus.NotFound => NotFound(),
            WriteStatus.DuplicateEmail => DuplicateEmail(),
            _ => TypedResults.Ok(UserResponse.From(result.User!)),
        };
    }

    private static Results<NoContent, ProblemHttpResult> Delete(Guid id, IUserRepository users) =>
        users.Delete(id) ? TypedResults.NoContent() : NotFound();

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found.");

    private static ProblemHttpResult DuplicateEmail() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email is already used by another user.");
}
