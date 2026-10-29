using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class Role : Entity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<User> Users { get; set; } = new List<User>();
}
