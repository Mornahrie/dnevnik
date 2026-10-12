using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("roles")]
public class Role
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    [Column("name")]
    public string Name { get; set; } = null!;

    public ICollection<User> Users { get; set; } = new List<User>();
}