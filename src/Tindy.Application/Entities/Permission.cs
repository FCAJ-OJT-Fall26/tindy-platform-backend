namespace Tindy.Application.Entities;

public class Permission
{
    public Guid PermissionId { get; set; }

    public string PermissionName { get; set; } = null!;
    public ICollection<RolePermission> RolePermissions { get; set; }
        = new List<RolePermission>();
}