namespace Tindy.Application.Entities;

public class Role
{
    public Guid RoleId { get; set; }

    public string RoleName { get; set; } = null!;
    public ICollection<UserRole> UserRoles { get; set; }
        = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; }
        = new List<RolePermission>();        
}