using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

public sealed class UsersClient
{
    private readonly HttpClient _http;

    public UsersClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<List<UserItem>>> ListAsync() =>
        ApiSender.SendAsync<List<UserItem>>(ct => _http.GetAsync(ApiRoutes.Users, ct));

    public Task<ApiResult<CreateUserResponse>> CreateAsync(CreateUserRequest request) =>
        ApiSender.SendAsync<CreateUserResponse>(ct => _http.PostAsJsonAsync(ApiRoutes.Users, request, ct));

    public Task<ApiResult<UserItem>> UpdateAsync(Guid id, UpdateUserRequest request) =>
        ApiSender.SendAsync<UserItem>(ct => _http.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Users, id), request, ct));

    public Task<ApiResult<DependencyList>> DependenciesAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.GetAsync(ApiRoutes.Dependencies(ApiRoutes.Users, id), ct));

    public Task<ApiResult<DependencyList>> DeleteAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.DeleteAsync(ApiRoutes.ById(ApiRoutes.Users, id), ct));

    public Task<ApiResult<List<RoleItem>>> RolesAsync() =>
        ApiSender.SendAsync<List<RoleItem>>(ct => _http.GetAsync(ApiRoutes.Roles, ct));

    public Task<ApiResult<RoleItem>> SaveRoleAsync(Guid? id, SaveRoleRequest request) =>
        ApiSender.SendAsync<RoleItem>(ct => id is { } existing
            ? _http.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Roles, existing), request, ct)
            : _http.PostAsJsonAsync(ApiRoutes.Roles, request, ct));

    public Task<ApiResult<DependencyList>> RoleDependenciesAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.GetAsync(ApiRoutes.Dependencies(ApiRoutes.Roles, id), ct));

    public Task<ApiResult<DependencyList>> DeleteRoleAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.DeleteAsync(ApiRoutes.ById(ApiRoutes.Roles, id), ct));

    public Task<ApiResult<ResetPasswordResponse>> ResetPasswordAsync(Guid id) =>
        ApiSender.SendAsync<ResetPasswordResponse>(ct => _http.PostAsync(ApiRoutes.UserResetPassword(id), null, ct));

    public Task<ApiResult<UserItem>> SetActiveAsync(Guid id, bool active) =>
        ApiSender.SendAsync<UserItem>(ct => _http.PostAsync(
            active ? ApiRoutes.Activate(ApiRoutes.Users, id) : ApiRoutes.Deactivate(ApiRoutes.Users, id), null, ct));
}
