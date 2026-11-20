namespace Milkshake.Domain.Orders;

/// <summary>
/// One milkshake in an order, with the names and fees of its choices as they were when the order was placed
/// (AC-7.2). The identifiers keep the link to the catalogue; the snapshot columns are what the order displays, so
/// renaming, repricing or deactivating a choice later changes nothing here. Immutable once created.
/// </summary>
public sealed class OrderItem
{
    public const int NameMaxLength = 100;

    private OrderItem()
    {
    }

    internal OrderItem(
        int number,
        int flavourId, string flavourName, decimal flavourFee,
        int consistencyId, string consistencyName, decimal consistencyFee,
        int toppingId, string toppingName, decimal toppingFee,
        decimal price)
    {
        Number = number;
        FlavourId = flavourId;
        FlavourName = flavourName;
        FlavourFee = flavourFee;
        ConsistencyId = consistencyId;
        ConsistencyName = consistencyName;
        ConsistencyFee = consistencyFee;
        ToppingId = toppingId;
        ToppingName = toppingName;
        ToppingFee = toppingFee;
        Price = price;
    }

    public int Id { get; private set; }

    public int OrderId { get; private set; }

    /// <summary>1-based position in the order ("Milkshake 1").</summary>
    public int Number { get; private set; }

    public int FlavourId { get; private set; }

    public string FlavourName { get; private set; } = string.Empty;

    public decimal FlavourFee { get; private set; }

    public int ConsistencyId { get; private set; }

    public string ConsistencyName { get; private set; } = string.Empty;

    public decimal ConsistencyFee { get; private set; }

    public int ToppingId { get; private set; }

    public string ToppingName { get; private set; } = string.Empty;

    public decimal ToppingFee { get; private set; }

    /// <summary>Flavour + consistency + topping fee (A-02).</summary>
    public decimal Price { get; private set; }
}
