namespace LTS.Domain.Enums;

/// <summary>
/// The kind of person behind an account. Determines the default permission template,
/// whether rows are filtered to a single partner, and which milestone fields are editable.
/// </summary>
public enum UserType
{
    /// <summary>Full access to every country and every admin page.</summary>
    Admin = 1,

    /// <summary>Company logistics department: sees all shipments in the countries granted to them.</summary>
    LogisticsDepartment = 2,

    /// <summary>Customs broker: sees only shipments where they are the assigned broker.</summary>
    Broker = 3,

    /// <summary>Carrier: sees only shipments where they are the assigned logistics company.</summary>
    LogisticsCompany = 4
}
