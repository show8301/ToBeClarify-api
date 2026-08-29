using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using MySqlConnector;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Content;
using ToBeClarify.Api.Services.Media;

namespace ToBeClarify.Api.Services.Admin.Content;

public sealed class AdminContentService : IAdminContentService
{
    private readonly IAdminContentRepository _repository;
    private readonly IAppClock _clock;
    private readonly MediaUrlService _mediaUrls;
    private readonly AdminMediaUploadService _mediaUpload;

    public AdminContentService(
        IAdminContentRepository repository,
        IAppClock clock,
        MediaUrlService mediaUrls,
        AdminMediaUploadService mediaUpload)
    {
        _repository = repository;
        _clock = clock;
        _mediaUrls = mediaUrls;
        _mediaUpload = mediaUpload;
    }

    public async Task<IReadOnlyList<AdminSiteSettingDto>> GetSiteSettingsAsync(CancellationToken cancellationToken)
        => (await _repository.GetSiteSettingsAsync(cancellationToken)).Select(MapSiteSetting).ToArray();

    public async Task<AdminSiteSettingDto> SaveSiteSettingAsync(string settingKey, SaveSiteSettingRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var key = Required(settingKey, "SETTING_KEY_REQUIRED");
        if (request.SettingValue.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new BusinessException("Setting value is required.", "SETTING_VALUE_REQUIRED");
        if (string.Equals(key, "siteVisibility", StringComparison.OrdinalIgnoreCase))
        {
            EnsureDeveloper(actor);
            ValidateSiteVisibility(request.SettingValue);
        }
        var current = await _repository.GetSiteSettingAsync(key, cancellationToken);
        await _repository.UpsertSiteSettingAsync(current?.Id ?? NewId(), key, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return MapSiteSetting((await _repository.GetSiteSettingAsync(key, cancellationToken))!);
    }

    public async Task<IReadOnlyList<AdminNavigationItemDto>> GetNavigationItemsAsync(CancellationToken cancellationToken)
        => (await _repository.GetNavigationItemsAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<AdminNavigationItemDto> SaveNavigationItemAsync(string? id, SaveNavigationItemRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        ValidatePlacement(request.Placement);
        var entityId = string.IsNullOrWhiteSpace(id) ? NewId() : Required(id, "NAVIGATION_ID_REQUIRED");
        await _repository.UpsertNavigationItemAsync(entityId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return Map((await _repository.GetNavigationItemsAsync(cancellationToken)).Single(x => x.Id == entityId));
    }

    public async Task DeleteNavigationItemAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        await _repository.DeleteNavigationItemAsync(Required(id, "NAVIGATION_ID_REQUIRED"), ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminHomeCarouselDto>> GetHomeCarouselsAsync(CancellationToken cancellationToken)
        => (await _repository.GetHomeCarouselsAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<AdminHomeCarouselDto> SaveHomeCarouselAsync(string? id, SaveHomeCarouselRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var albumId = Required(request.AlbumId, "CAROUSEL_ALBUM_ID_REQUIRED");
        if (await _repository.GetGalleryAlbumAsync(albumId, cancellationToken) is null)
            throw new NotFoundException("Gallery album not found.", "CAROUSEL_ALBUM_NOT_FOUND");
        var entityId = string.IsNullOrWhiteSpace(id) ? NewId() : Required(id, "CAROUSEL_ID_REQUIRED");
        await _repository.UpsertHomeCarouselAsync(entityId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return Map((await _repository.GetHomeCarouselsAsync(cancellationToken)).Single(x => x.Id == entityId));
    }

    public async Task DeleteHomeCarouselAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        await _repository.DeleteHomeCarouselAsync(Required(id, "CAROUSEL_ID_REQUIRED"), ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminHomeSlideDto>> GetHomeSlidesAsync(CancellationToken cancellationToken)
        => (await _repository.GetHomeSlidesAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<AdminHomeSlideDto> SaveHomeSlideAsync(string? id, SaveHomeSlideRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        if (string.IsNullOrWhiteSpace(request.MediaId))
            throw new BusinessException("A slide image is required.", "HOME_SLIDE_IMAGE_REQUIRED");

        var entityId = string.IsNullOrWhiteSpace(id) ? NewId() : Required(id, "HOME_SLIDE_ID_REQUIRED");
        var existing = string.IsNullOrWhiteSpace(id) ? null : await _repository.GetHomeSlideAsync(entityId, cancellationToken);
        await _repository.UpsertHomeSlideAsync(entityId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        if (existing?.MediaId is not null && !string.Equals(existing.MediaId, request.MediaId, StringComparison.Ordinal))
            await _mediaUpload.DeleteHomeAssetAsync(existing.MediaId, cancellationToken);

        return Map((await _repository.GetHomeSlidesAsync(cancellationToken)).Single(x => x.Id == entityId));
    }

    public async Task DeleteHomeSlideAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var entityId = Required(id, "HOME_SLIDE_ID_REQUIRED");
        var existing = await _repository.GetHomeSlideAsync(entityId, cancellationToken)
            ?? throw new NotFoundException("Home slide not found.", "HOME_SLIDE_NOT_FOUND");
        await _repository.DeleteHomeSlideAsync(entityId, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        await _mediaUpload.DeleteHomeAssetAsync(existing.MediaId, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminShopRuleDto>> GetShopRulesAsync(CancellationToken cancellationToken)
        => (await _repository.GetShopRulesAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<AdminShopRuleDto> SaveShopRuleAsync(string? id, SaveShopRuleRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var entityId = string.IsNullOrWhiteSpace(id) ? NewId() : Required(id, "SHOP_RULE_ID_REQUIRED");
        await _repository.UpsertShopRuleAsync(entityId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return Map((await _repository.GetShopRulesAsync(cancellationToken)).Single(x => x.Id == entityId));
    }

    public async Task DeleteShopRuleAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        await _repository.DeleteShopRuleAsync(Required(id, "SHOP_RULE_ID_REQUIRED"), ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminStaffMemberListItemDto>> GetStaffMembersAsync(ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var rows = await _repository.GetStaffMembersAsync(cancellationToken);
        return rows.Select(row => new AdminStaffMemberListItemDto(
            row.Id, row.DisplayName, row.AvatarMediaId, _mediaUrls.BuildUrl(row.AvatarMediaId, "card"),
            row.RoleTitle, row.IsWorkingToday, row.BufferMinutes, row.IsNominatable,
            row.SortOrder, row.IsActive)).ToArray();
    }

    public async Task<AdminStaffMemberDto> GetStaffMemberAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var staffId = Required(id, "STAFF_ID_REQUIRED");
        var row = await _repository.GetStaffMemberAsync(staffId, cancellationToken)
            ?? throw new NotFoundException("Staff member not found.", "STAFF_NOT_FOUND");
        return await MapStaffAsync(row, cancellationToken);
    }

    public async Task<AdminStaffMemberDto> SaveStaffMemberAsync(string id, SaveStaffMemberRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var staffId = ResolveStaffId(id, actor);
        var existing = await _repository.GetStaffMemberAsync(staffId, cancellationToken)
            ?? throw new NotFoundException("Staff member not found.", "STAFF_NOT_FOUND");
        ValidateStaffRequest(request);
        if (!CanManageAll(actor)) request.SortOrder = existing.SortOrder;
        await _repository.SaveStaffMemberAsync(staffId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetStaffMemberAsync(staffId, actor, cancellationToken);
    }

    public async Task<AdminStaffMemberDto> UpdateStaffMemberStatusAsync(string id, UpdateStaffMemberStatusRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        if (!request.IsWorkingToday.HasValue && !request.IsActive.HasValue)
            throw new BusinessException("At least one staff status is required.", "STAFF_STATUS_REQUIRED");

        var staffId = ResolveStaffId(id, actor);
        var existing = await _repository.GetStaffMemberAsync(staffId, cancellationToken)
            ?? throw new NotFoundException("Staff member not found.", "STAFF_NOT_FOUND");
        await _repository.UpdateStaffMemberStatusAsync(staffId, request.IsWorkingToday, request.IsActive,
            ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return await GetStaffMemberAsync(existing.Id, actor, cancellationToken);
    }

    public async Task ReorderStaffMembersAsync(ReorderStaffMembersRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        if (request.Items.Count == 0) throw new BusinessException("At least one staff member is required.", "STAFF_ORDER_REQUIRED");
        var currentIds = (await _repository.GetStaffMembersAsync(cancellationToken)).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (request.Items.Any(item => !currentIds.Contains(item.Id)))
            throw new BusinessException("One or more staff members do not exist.", "STAFF_ORDER_INVALID");
        await _repository.ReorderStaffMembersAsync(request.Items, ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }

    public async Task DeleteStaffMemberAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var staffId = Required(id, "STAFF_ID_REQUIRED");
        if (await _repository.StaffMemberHasAdminAccountAsync(staffId, cancellationToken))
            throw StaffMemberHasAdminAccount();

        try
        {
            await _repository.DeleteStaffMemberAsync(
                staffId, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1451)
        {
            throw StaffMemberHasAdminAccount();
        }
    }

    public async Task<IReadOnlyList<AdminGalleryAlbumDto>> GetGalleryAlbumsAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.GetGalleryAlbumsAsync(cancellationToken);
        var result = new List<AdminGalleryAlbumDto>(rows.Count);
        foreach (var row in rows) result.Add(await MapGalleryAlbumAsync(row, cancellationToken));
        return result;
    }

    public async Task<AdminGalleryAlbumDto> SaveGalleryAlbumAsync(string? id, SaveGalleryAlbumRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.MediaId)))
            throw new BusinessException("Every gallery item must reference a media asset.", "GALLERY_ITEM_MEDIA_REQUIRED");
        var entityId = string.IsNullOrWhiteSpace(id) ? NewId() : Required(id, "GALLERY_ALBUM_ID_REQUIRED");
        await _repository.UpsertGalleryAlbumAsync(entityId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        var row = await _repository.GetGalleryAlbumAsync(entityId, cancellationToken)
            ?? throw new NotFoundException("Gallery album not found.", "GALLERY_ALBUM_NOT_FOUND");
        return await MapGalleryAlbumAsync(row, cancellationToken);
    }

    public async Task DeleteGalleryAlbumAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        await _repository.DeleteGalleryAlbumAsync(Required(id, "GALLERY_ALBUM_ID_REQUIRED"), ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminPricingRuleDto>> GetPricingRulesAsync(CancellationToken cancellationToken)
        => (await _repository.GetPricingRulesAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<AdminPricingRuleDto> SavePricingRuleAsync(string? id, SavePricingRuleRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var entityId = string.IsNullOrWhiteSpace(id) ? NewId() : Required(id, "PRICING_RULE_ID_REQUIRED");
        await _repository.UpsertPricingRuleAsync(entityId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return Map((await _repository.GetPricingRulesAsync(cancellationToken)).Single(x => x.Id == entityId));
    }

    public async Task DeletePricingRuleAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        await _repository.DeletePricingRuleAsync(Required(id, "PRICING_RULE_ID_REQUIRED"), ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }

    public async Task<AdminMenuDto> GetMenuAsync(CancellationToken cancellationToken)
    {
        var pricingTask = _repository.GetPricingRulesAsync(cancellationToken);
        var menuTask = _repository.GetMenuAsync(cancellationToken);
        await Task.WhenAll(pricingTask, menuTask);
        var menu = await menuTask;
        var items = menu.Items.Select(Map).ToArray();
        var sets = menu.Sets.Select(set => new AdminMenuSetDto(set.Id, set.SetName, set.SetDescription, set.SetPrice,
            set.MediaId, _mediaUrls.BuildUrl(set.MediaId, "card"), set.SortOrder, set.IsAvailable,
            menu.SetItems.Where(item => item.SetId == set.Id).Select(Map).ToArray())).ToArray();
        return new AdminMenuDto((await pricingTask).Select(Map).ToArray(),
            menu.Categories.Select(category => new AdminMenuCategoryDto(category.Id, category.CategoryName,
                category.CategoryDescription, category.SortOrder, category.IsEnabled,
                items.Where(item => item.CategoryId == category.Id).ToArray())).ToArray(), sets);
    }

    public async Task<AdminMenuCategoryDto> SaveMenuCategoryAsync(string? id, SaveMenuCategoryRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var entityId = string.IsNullOrWhiteSpace(id) ? NewId() : Required(id, "MENU_CATEGORY_ID_REQUIRED");
        await _repository.UpsertMenuCategoryAsync(entityId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return (await GetMenuAsync(cancellationToken)).Categories.Single(x => x.Id == entityId);
    }

    public async Task DeleteMenuCategoryAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        try
        {
            await _repository.DeleteMenuCategoryAsync(Required(id, "MENU_CATEGORY_ID_REQUIRED"), ActorId(actor), _clock.LocalDateTime, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new BusinessException(ex.Message, "MENU_CATEGORY_HAS_ITEMS");
        }
    }

    public async Task<AdminMenuItemDto> SaveMenuItemAsync(string? id, SaveMenuItemRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var entityId = string.IsNullOrWhiteSpace(id) ? NewId() : Required(id, "MENU_ITEM_ID_REQUIRED");
        await _repository.UpsertMenuItemAsync(entityId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return (await GetMenuAsync(cancellationToken)).Categories.SelectMany(x => x.Items).Single(x => x.Id == entityId);
    }

    public async Task DeleteMenuItemAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        try
        {
            await _repository.DeleteMenuItemAsync(Required(id, "MENU_ITEM_ID_REQUIRED"), ActorId(actor), _clock.LocalDateTime, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new BusinessException(ex.Message, "MENU_ITEM_USED_BY_SET");
        }
    }

    public async Task<AdminMenuSetDto> SaveMenuSetAsync(string? id, SaveMenuSetRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var entityId = string.IsNullOrWhiteSpace(id) ? NewId() : Required(id, "MENU_SET_ID_REQUIRED");
        await _repository.SaveMenuSetAsync(entityId, request, ActorId(actor), _clock.LocalDateTime, cancellationToken);
        return (await GetMenuAsync(cancellationToken)).Sets.Single(x => x.Id == entityId);
    }

    public async Task DeleteMenuSetAsync(string id, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        await _repository.DeleteMenuSetAsync(Required(id, "MENU_SET_ID_REQUIRED"), ActorId(actor), _clock.LocalDateTime, cancellationToken);
    }

    private async Task<AdminStaffMemberDto> MapStaffAsync(AdminStaffMemberRow row, CancellationToken cancellationToken)
    {
        var servicesTask = _repository.GetStaffServicesAsync(row.Id, cancellationToken);
        var galleryTask = _repository.GetStaffGalleryAsync(row.Id, cancellationToken);
        await Task.WhenAll(servicesTask, galleryTask);
        return new AdminStaffMemberDto(row.Id, row.DisplayName, row.Nickname, row.AvatarMediaId,
            _mediaUrls.BuildUrl(row.AvatarMediaId, "card"), row.RoleTitle, row.ShortBio,
            row.ProfileBio, row.IsWorkingToday, row.CurrentStatus, row.StatusText, row.TodayShift,
            row.BufferMinutes, row.IsNominatable, row.SortOrder, row.IsActive,
            (await servicesTask).Select(Map).ToArray(), (await galleryTask).Select(item => new AdminStaffGalleryItemDto(
                item.Id, item.StaffId, item.MediaId, _mediaUrls.BuildUrl(item.MediaId, "full"),
                item.SortOrder, item.IsPublished)).ToArray());
    }

    private static void ValidateStaffRequest(SaveStaffMemberRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new BusinessException("Display name is required.", "STAFF_DISPLAY_NAME_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.ShortBio))
            throw new BusinessException("Card bio is required.", "STAFF_SHORT_BIO_REQUIRED");
        if (request.BufferMinutes is < 0 or > 1440)
            throw new BusinessException("Buffer minutes must be between 0 and 1440.", "STAFF_BUFFER_MINUTES_INVALID");
        if ((request.Services ?? []).Any(service => service.ServiceType is not ("common" or "special")))
            throw new BusinessException("Service type must be common or special.", "INVALID_SERVICE_TYPE");
        if ((request.Services ?? []).Any(service => service.Price is < 0
                || service.DurationMinutes is < 0 or > 1440
                || service.AdditionalPersonPrice is < 0))
            throw new BusinessException("Service price, duration and additional-person price must be non-negative, and duration cannot exceed 1440 minutes.", "STAFF_SERVICE_NUMERIC_VALUE_INVALID");
        if ((request.Gallery ?? []).Any(item => string.IsNullOrWhiteSpace(item.MediaId)))
            throw new BusinessException("Every staff gallery item must reference a media asset.", "STAFF_GALLERY_MEDIA_REQUIRED");
    }

    private static void ValidatePlacement(string placement)
    {
        if (placement is not ("navbar" or "footer" or "both"))
            throw new BusinessException("Placement must be navbar, footer, or both.", "INVALID_NAVIGATION_PLACEMENT");
    }

    private static bool CanManageAll(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.RoleClaimType) is AdminRole.Developer or AdminRole.Manager;

    private static void EnsureDeveloper(ClaimsPrincipal actor)
    {
        if (actor.FindFirstValue(AdminAuthConstants.RoleClaimType) != AdminRole.Developer)
            throw new ForbiddenException("This action requires developer permission.", "ADMIN_DEVELOPER_REQUIRED");
    }

    private static void ValidateSiteVisibility(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new BusinessException("Site visibility must be a JSON object.", "SITE_VISIBILITY_INVALID");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "home", "staff", "gallery", "menu", "guestbook", "liveUpdate", "staffRanking", "monetaryRanking", "menuHidden"
        };
        foreach (var property in value.EnumerateObject())
        {
            if (!keys.Contains(property.Name) || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new BusinessException("Site visibility values must be boolean page flags.", "SITE_VISIBILITY_INVALID");
        }
    }

    private static void EnsureManager(ClaimsPrincipal actor)
    {
        if (!CanManageAll(actor)) throw new ForbiddenException("This action requires manager permission.", "ADMIN_MANAGER_REQUIRED");
    }

    private static string OwnStaffId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.StaffMemberIdClaimType)
            ?? throw new ForbiddenException("This account is not linked to a staff member.", "STAFF_ACCOUNT_NOT_LINKED");

    private static string ResolveStaffId(string id, ClaimsPrincipal actor)
    {
        var requested = Required(id, "STAFF_ID_REQUIRED");
        return CanManageAll(actor) ? requested : string.Equals(requested, OwnStaffId(actor), StringComparison.Ordinal)
            ? requested
            : throw new ForbiddenException("You can only edit your own staff profile.", "STAFF_SCOPE_FORBIDDEN");
    }

    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
            ?? actor.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedException();

    private static string Required(string? value, string errorCode)
        => string.IsNullOrWhiteSpace(value) ? throw new BusinessException("Id is required.", errorCode) : value.Trim();

    private static string NewId() => Guid.NewGuid().ToString("D");

    private static ConflictException StaffMemberHasAdminAccount()
        => new(
            "This staff member is linked to an admin account and cannot be deleted.",
            "STAFF_MEMBER_HAS_ADMIN_ACCOUNT");

    private AdminSiteSettingDto MapSiteSetting(AdminSiteSettingRow row)
    {
        var value = ParseJson(row.SettingValue);
        if (!string.Equals(row.SettingKey, "shopInfo", StringComparison.OrdinalIgnoreCase)
            || value.ValueKind != JsonValueKind.Object)
            return new AdminSiteSettingDto(row.Id, row.SettingKey, value, row.Description, row.IsActive);

        var json = JsonNode.Parse(value.GetRawText()) as JsonObject;
        var mediaId = json?["heroImageMediaId"]?.GetValue<string>();
        var legacyUrl = json?["heroImage"]?.GetValue<string>();
        var heroUrl = _mediaUrls.BuildUrl(mediaId, legacyUrl, "hero");
        if (json is not null && heroUrl is not null)
        {
            json["heroImage"] = heroUrl;
            using var document = JsonDocument.Parse(json.ToJsonString());
            value = document.RootElement.Clone();
        }
        return new AdminSiteSettingDto(row.Id, row.SettingKey, value, row.Description, row.IsActive);
    }

    private static AdminSiteSettingDto Map(AdminSiteSettingRow row)
        => new(row.Id, row.SettingKey, ParseJson(row.SettingValue), row.Description, row.IsActive);

    private static AdminNavigationItemDto Map(AdminNavigationItemRow row)
        => new(row.Id, row.Label, row.RoutePath, row.Placement, row.ParentItemId, row.SortOrder, row.IsDropdown, row.IsEnabled);

    private AdminHomeCarouselDto Map(AdminHomeCarouselRow row)
        => new(row.Id, row.AlbumId, row.OverrideTitle, row.OverrideSummary, row.OverrideMediaId,
            _mediaUrls.BuildUrl(row.OverrideMediaId, "hero"),
            row.EventTimeSnapshot, row.CtaLabel, row.SortOrder, row.IsEnabled);

    private AdminHomeSlideDto Map(AdminHomeSlideRow row)
        => new(row.Id, row.MediaId, _mediaUrls.BuildUrl(row.MediaId, "hero"), row.SortOrder, row.IsEnabled, row.DisplaySeconds);

    private static AdminShopRuleDto Map(AdminShopRuleRow row)
        => new(row.Id, row.RuleText, row.RuleNote, row.SortOrder, row.IsEnabled);

    private async Task<AdminGalleryAlbumDto> MapGalleryAlbumAsync(AdminGalleryAlbumRow row, CancellationToken cancellationToken)
    {
        var items = await _repository.GetGalleryItemsAsync(row.Id, cancellationToken);
        return new AdminGalleryAlbumDto(row.Id, row.AlbumTitle, row.AlbumDescription, row.CoverMediaId,
            _mediaUrls.BuildUrl(row.CoverMediaId, "hero"), row.PeriodText, row.EndsAt,
            row.DetailContent, items.Select(item => new AdminGalleryItemDto(item.Id, item.MediaId,
                _mediaUrls.BuildUrl(item.MediaId, "full"),
                _mediaUrls.BuildUrl(item.MediaId, "thumbnail"),
                item.Title, item.Caption, item.ShotAt, item.SortOrder, item.IsPublished)).ToArray(),
            row.SortOrder, row.IsPublished);
    }

    private static AdminPricingRuleDto Map(AdminPricingRuleRow row)
        => new(row.Id, row.Title, row.Description, row.PriceText, row.SortOrder, row.IsEnabled);

    private AdminMenuItemDto Map(AdminMenuItemRow row)
        => new(row.Id, row.CategoryId, row.ItemName, row.ItemDescription, row.Price, row.MediaId,
            _mediaUrls.BuildUrl(row.MediaId, "card"), ParseNullableJson(row.Tags), row.SortOrder, row.IsAvailable);

    private static AdminMenuSetItemDto Map(AdminMenuSetItemRow row)
        => new(row.Id, row.MenuItemId, row.ItemName, row.ItemRole, row.Quantity, row.SortOrder);

    private static AdminStaffServiceDto Map(AdminStaffServiceRow row)
        => new(row.Id, row.StaffId, row.ServiceType, row.ServiceName, row.ServiceDescription, row.PriceText,
            row.Price, row.DurationMinutes, row.IsNominatable, row.AdditionalPersonPrice,
            row.SortOrder, row.IsEnabled);

    private static JsonElement ParseJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    private static JsonElement? ParseNullableJson(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : ParseJson(value);
}
