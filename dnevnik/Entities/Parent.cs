using dnevnik.Entities;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("parents")]
public class Parent
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(20)]
    [Column("phone")]
    public string? Phone { get; set; }

    public ICollection<ParentStudent> ParentStudents { get; set; } = new List<ParentStudent>();
}