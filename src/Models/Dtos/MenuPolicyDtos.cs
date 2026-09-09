using System.ComponentModel.DataAnnotations;

namespace ToBeClarify.Api.Models.Dtos;

public sealed record PricingPolicy
{
    [RegularExpression("^(information|system)$")]
    public string Mode { get; init; } = "information";
    [RegularExpression("^(minimum_meal_credit|base_nomination_fee)$")]
    public string? Source { get; init; }
    public bool ShowOnHome { get; init; }
    public bool ShowOnMenu { get; init; } = true;
    public bool ShowOnOrder { get; init; } = true;
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidUntil { get; init; }
}

public sealed record ProductPolicy
{
    public bool CanOrderAlone { get; init; } = true;
    public string[] EventCategories { get; init; } = [];
}

public sealed record MenuComponentSnapshot(string MenuItemId, string ItemName, string ItemRole,
    int Quantity, string[] EventCategories);
public sealed record MenuLineSnapshot(string Kind, string ReferenceId, string Name, int UnitPrice,
    int Quantity, string[] EventCategories, IReadOnlyList<MenuComponentSnapshot> Components);
public sealed record MenuRuleSnapshot(string Id, string Title, string Description, string? PriceText,
    PricingPolicy Policy);

public sealed class MenuSortRequest
{
    [Required] public string ExpectedRevision { get; init; } = "";
    public string[] PricingRules { get; init; } = [];
    public string[] Categories { get; init; } = [];
    public Dictionary<string, string[]> Items { get; init; } = new();
    public string[] Sets { get; init; } = [];
}

public sealed record MenuQuoteDto(string QuoteToken, int Subtotal, int EligibleMealAmount,
    int MealCreditApplied, int RemainingMealCredit, int TotalAmount,
    IReadOnlyList<MenuLineSnapshot> Lines, IReadOnlyList<MenuRuleSnapshot> Rules, DateTimeOffset ExpiresAt)
{
    public IReadOnlyList<OrderQuoteCharge> Charges { get; init; } = [];
    public bool RequiresStoreConfirmation { get; init; }
}
public sealed record OrderQuoteCharge(string Name,int Quantity,int UnitPrice,int LineTotal);
