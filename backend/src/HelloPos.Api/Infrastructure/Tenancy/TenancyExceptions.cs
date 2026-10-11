namespace HelloPos.Api.Infrastructure.Tenancy;

/// <summary>Business-owned data was touched without a current business (spec FR-008); answered as 401.</summary>
public sealed class NoCurrentBusinessException : Exception
{
    public NoCurrentBusinessException()
        : base("There is no current business; business-owned data cannot be read or written.")
    {
    }

    public NoCurrentBusinessException(string message)
        : base(message)
    {
    }

    public NoCurrentBusinessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Code tried to move a record to another business; never allowed (spec FR-010).</summary>
public sealed class BusinessReassignmentException : Exception
{
    public BusinessReassignmentException()
        : base("A business-owned record cannot change its business.")
    {
    }

    public BusinessReassignmentException(string message)
        : base(message)
    {
    }

    public BusinessReassignmentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
