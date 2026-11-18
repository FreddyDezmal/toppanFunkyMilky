using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Management;
using Milkshake.Domain.Restaurants;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Management;

/// <summary>Manager maintenance of pickup restaurants. Audited by the SaveChanges interceptor; xmin-protected.</summary>
internal sealed class RestaurantManagementService(MilkshakeDbContext dbContext) : IRestaurantManagementService
{
    private const string Label = "restaurant";

    public async Task<IReadOnlyList<RestaurantSummary>> ListAsync(StatusFilter status, CancellationToken cancellationToken)
    {
        IQueryable<Restaurant> query = dbContext.Restaurants.AsNoTracking();
        query = status switch
        {
            StatusFilter.Active => query.Where(r => r.IsActive),
            StatusFilter.Inactive => query.Where(r => !r.IsActive),
            _ => query,
        };

        List<Restaurant> found = await query.OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return found.ConvertAll(ToSummary);
    }

    public async Task<RestaurantSummary?> GetAsync(int id, CancellationToken cancellationToken)
    {
        Restaurant? restaurant = await dbContext.Restaurants.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return restaurant is null ? null : ToSummary(restaurant);
    }

    public async Task<ManagementResult<RestaurantSummary>> CreateAsync(CreateRestaurantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Dictionary<string, string[]> errors = Validate(command.Name, command.Address);
        if (errors.Count > 0)
        {
            return ManagementResult<RestaurantSummary>.Invalid(errors);
        }

        string name = command.Name.Trim();
        if (await IsNameTakenAsync(name, exceptId: null, cancellationToken))
        {
            return DuplicateName();
        }

        var restaurant = new Restaurant(name, command.Address);
        dbContext.Restaurants.Add(restaurant);
        return await SaveAsync(restaurant, cancellationToken);
    }

    public async Task<ManagementResult<RestaurantSummary>> UpdateAsync(int id, UpdateRestaurantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Dictionary<string, string[]> errors = Validate(command.Name, command.Address);
        if (errors.Count > 0)
        {
            return ManagementResult<RestaurantSummary>.Invalid(errors);
        }

        Restaurant? restaurant = await dbContext.Restaurants.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (restaurant is null)
        {
            return ManagementResult<RestaurantSummary>.NotFound();
        }

        if (restaurant.Version != command.Version)
        {
            return ManagementResult<RestaurantSummary>.Conflict();
        }

        string name = command.Name.Trim();
        if (await IsNameTakenAsync(name, id, cancellationToken))
        {
            return DuplicateName();
        }

        restaurant.Rename(name);
        restaurant.ChangeAddress(command.Address);
        return await SaveAsync(restaurant, cancellationToken);
    }

    public async Task<ManagementResult<RestaurantSummary>> SetActiveAsync(int id, bool active, uint version, CancellationToken cancellationToken)
    {
        Restaurant? restaurant = await dbContext.Restaurants.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (restaurant is null)
        {
            return ManagementResult<RestaurantSummary>.NotFound();
        }

        if (restaurant.Version != version)
        {
            return ManagementResult<RestaurantSummary>.Conflict();
        }

        if (active)
        {
            restaurant.Activate();
        }
        else
        {
            restaurant.Deactivate();
        }

        return await SaveAsync(restaurant, cancellationToken);
    }

    private static Dictionary<string, string[]> Validate(string? name, string? address)
    {
        var errors = new Dictionary<string, string[]>();
        ManagementValidation.Name(errors, ManagementFields.Name, name, Restaurant.NameMaxLength);
        ManagementValidation.OptionalText(errors, ManagementFields.Address, "Address", address, Restaurant.AddressMaxLength);
        return errors;
    }

    private async Task<ManagementResult<RestaurantSummary>> SaveAsync(Restaurant restaurant, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ManagementResult<RestaurantSummary>.Conflict();
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return DuplicateName();
        }

        return ManagementResult<RestaurantSummary>.Success(ToSummary(restaurant));
    }

    private Task<bool> IsNameTakenAsync(string name, int? exceptId, CancellationToken cancellationToken)
    {
        string lowered = name.ToLowerInvariant();
        return dbContext.Restaurants.AnyAsync(r => r.Name.ToLower() == lowered && (exceptId == null || r.Id != exceptId), cancellationToken);
    }

    private static RestaurantSummary ToSummary(Restaurant r) => new(r.Id, r.Name, r.Address, r.IsActive, r.Version);

    private static ManagementResult<RestaurantSummary> DuplicateName() =>
        ManagementResult<RestaurantSummary>.Invalid(ManagementFields.Name, ManagementMessages.DuplicateName(Label));
}