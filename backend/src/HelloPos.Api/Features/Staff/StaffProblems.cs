using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HelloPos.Api.Features.Staff;

/// <summary>The specific refusals of the staff slices (feature 006, research R10).</summary>
public static class StaffProblems
{
    public const string LastOwnerType = "urn:hellopos:problem:last-owner";
    public const string StaffChangedType = "urn:hellopos:problem:staff-changed";
    public const string AlreadyMemberType = "urn:hellopos:problem:already-member";

    public static ProblemHttpResult LastOwner() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, type: LastOwnerType,
        title: "A business needs at least one active owner.");

    public static ProblemHttpResult StaffChanged() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, type: StaffChangedType,
        title: "Someone else changed this person in the meantime. Reload and try again.");

    public static ProblemHttpResult AlreadyMember() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, type: AlreadyMemberType,
        title: "This person already works at this business.");

    public static ProblemHttpResult NotAllowed() => TypedResults.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Your role in this business does not allow this.");

    public static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);

    /// <summary>The kernel rule table as an API answer: <c>null</c> when allowed.</summary>
    public static ProblemHttpResult? Check(StaffRole actor, StaffAction action, StaffRole? target = null, StaffRole? newRole = null) =>
        StaffRules.Can(actor, action, target, newRole) ? null : NotAllowed();
}
