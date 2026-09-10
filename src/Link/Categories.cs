using System.Net.Http.Json;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// The shop's categories, on the machine that owns them. The repository, unchanged: its
/// permission checks, its refusals and its audit entries are the shop's rules and they stay
/// exactly where they were.
/// </summary>
public sealed class LocalCategories : ICategoryService
{
    public IReadOnlyList<CategoryRow> List(bool includeInactive = false) =>
        CategoryRepository.List(includeInactive);

    public int Create(string name, string icon = "", string image = "") =>
        CategoryRepository.Create(name, icon, image);

    public void Rename(int id, string oldName, string newName, string icon, string image) =>
        CategoryRepository.Rename(id, oldName, newName, icon, image);

    public bool Delete(int id, string name, out string problem) =>
        CategoryRepository.Delete(id, name, out problem);

    public bool SetActive(int id, string name, bool active, out string problem) =>
        CategoryRepository.SetActive(id, name, active, out problem);
}

/// <summary>
/// The shop's categories, asked for over the wire.
///
/// <para>
/// Every method here is a request to the shop to do something, and the shop decides. A refusal
/// — a category that still has products in it, a cashier without the right to manage them —
/// comes back as the shop's own words, because it is the shop that refused. The client neither
/// makes those decisions nor could be trusted to.
/// </para>
///
/// <para>
/// A shop that cannot be reached throws. That is deliberate and it is the whole point: there is
/// no local table to fall back to, and a screen that quietly showed an empty list would be
/// telling the owner their shop has no categories.
/// </para>
/// </summary>
public sealed class RemoteCategories : ICategoryService
{
    public IReadOnlyList<CategoryRow> List(bool includeInactive = false) =>
        Api.Get<List<CategoryRow>>($"categories/all?includeInactive={includeInactive}")
        ?? new List<CategoryRow>();

    public int Create(string name, string icon = "", string image = "") =>
        Api.Post<CategorySaved>("categories", new NewCategory(name, icon, image))?.Id ?? 0;

    public void Rename(int id, string oldName, string newName, string icon, string image) =>
        Api.Put<CategorySaved>($"categories/{id}", new RenameCategory(oldName, newName, icon, image));

    public bool Delete(int id, string name, out string problem)
    {
        var said = Api.Delete<CategorySaved>($"categories/{id}");
        problem = said?.Problem ?? string.Empty;
        return said?.Ok ?? false;
    }

    public bool SetActive(int id, string name, bool active, out string problem)
    {
        var said = Api.Put<CategorySaved>($"categories/{id}/active", new SetCategoryActive(active));
        problem = said?.Problem ?? string.Empty;
        return said?.Ok ?? false;
    }
}

// ---------------------------------------------------------------- what crosses the wire

/// <summary>A category the shop is being asked to file.</summary>
public sealed record NewCategory(string Name, string Icon, string Image);

/// <summary>A category the shop is being asked to rename.</summary>
public sealed record RenameCategory(string OldName, string NewName, string Icon, string Image);

/// <summary>Hiding or restoring one.</summary>
public sealed record SetCategoryActive(bool Active);

/// <summary>What the shop made of it: the row's id, or the reason there is not one.</summary>
public sealed record CategorySaved(bool Ok, int Id, string Problem);
