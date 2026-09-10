using MarketPos.Data;

namespace MarketPos.Link;

/// <summary>
/// What the shop does when asked about its categories.
///
/// <para>
/// Written once and mapped by both servers, so a client cannot get a different answer depending
/// on which of them it found. Every method calls the repository — the same one the shop's own
/// back office calls — so the permission checks, the refusals and the audit entries happen here,
/// on the machine that owns the books, and not in a client that could be argued with.
/// </para>
/// </summary>
public static class ShopCategoriesApi
{
    public static IReadOnlyList<Models.CategoryRow> List(bool includeInactive) =>
        CategoryRepository.List(includeInactive);

    public static CategorySaved Create(NewCategory asked)
    {
        var name = asked.Name.Trim();
        if (name.Length == 0) return new CategorySaved(false, 0, "Give the category a name.");

        try
        {
            return new CategorySaved(true, CategoryRepository.Create(name, asked.Icon, asked.Image), string.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            // The shop decides who may do this, not the screen that asked.
            return new CategorySaved(false, 0, "You are not allowed to manage categories.");
        }
        catch (Exception problem)
        {
            return new CategorySaved(false, 0, Plainly(problem));
        }
    }

    public static CategorySaved Rename(int id, RenameCategory asked)
    {
        try
        {
            CategoryRepository.Rename(id, asked.OldName, asked.NewName, asked.Icon, asked.Image);
            return new CategorySaved(true, id, string.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            return new CategorySaved(false, id, "You are not allowed to manage categories.");
        }
        catch (Exception problem)
        {
            return new CategorySaved(false, id, Plainly(problem));
        }
    }

    public static CategorySaved Delete(int id)
    {
        var name = CategoryRepository.List(includeInactive: true)
                                     .FirstOrDefault(c => c.Id == id)?.Name ?? string.Empty;

        try
        {
            var went = CategoryRepository.Delete(id, name, out var problem);
            return new CategorySaved(went, id, problem);
        }
        catch (UnauthorizedAccessException)
        {
            return new CategorySaved(false, id, "You are not allowed to manage categories.");
        }
        catch (Exception problem)
        {
            return new CategorySaved(false, id, Plainly(problem));
        }
    }

    public static CategorySaved SetActive(int id, SetCategoryActive asked)
    {
        var name = CategoryRepository.List(includeInactive: true)
                                     .FirstOrDefault(c => c.Id == id)?.Name ?? string.Empty;

        try
        {
            var done = CategoryRepository.SetActive(id, name, asked.Active, out var problem);
            return new CategorySaved(done, id, problem);
        }
        catch (UnauthorizedAccessException)
        {
            return new CategorySaved(false, id, "You are not allowed to manage categories.");
        }
        catch (Exception problem)
        {
            return new CategorySaved(false, id, Plainly(problem));
        }
    }

    /// <summary>
    /// The database's own words are for the log, not for a shopkeeper. A UNIQUE constraint is
    /// the one that reaches a screen often enough to be worth saying properly.
    /// </summary>
    private static string Plainly(Exception problem) =>
        problem.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            ? "There is already a category with that name."
            : problem.Message;
}
