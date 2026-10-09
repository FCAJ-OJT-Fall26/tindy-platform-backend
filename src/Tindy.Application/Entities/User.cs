namespace Tindy.Application.Entities;

public class User
{
    public Guid UserId {get; set;}
    public string Username {get; set;} = null!;
    public string usernameNormalized {get; set;} = null!;
    public string Status {get; set;} = null!;
    public DateTime CreateAt {get; set;} 
    public DateTime? UpdateAt {get;set;}
    public DateTime? DeleteAt {get;set;}
}